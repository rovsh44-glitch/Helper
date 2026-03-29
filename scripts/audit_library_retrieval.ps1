[CmdletBinding()]
param(
    [string]$ApiBaseUrl = "http://127.0.0.1:5000",
    [string]$QdrantBaseUrl = "http://127.0.0.1:6333",
    [string]$ApiKey = "",
    [string]$PipelineVersion = "v2",
    [int]$Limit = 5,
    [switch]$UseDomainFilter,
    [string]$ReportPath = "",
    [string]$JsonOutputPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-EnvValueFromFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Name
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return ""
    }

    $prefix = "$Name="
    $line = Get-Content -LiteralPath $Path | Where-Object { $_ -match ("^{0}=" -f [regex]::Escape($Name)) } | Select-Object -First 1
    if ($null -eq $line) {
        return ""
    }

    return $line.Substring($prefix.Length).Trim()
}

function Escape-MarkdownCell {
    param([AllowNull()][string]$Value)

    if ($null -eq $Value) {
        return ""
    }

    return ($Value -replace '\|', '\|' -replace "`r?`n", '<br/>')
}

function Get-NonEmptyDomainCounts {
    param([Parameter(Mandatory = $true)][string]$BaseUrl)

    $counts = [ordered]@{}
    $response = Invoke-RestMethod -Method Get -Uri ($BaseUrl.TrimEnd("/") + "/collections") -TimeoutSec 30
    $collections = @($response.result.collections | Where-Object { $_.name -like "knowledge_*_v2" })

    foreach ($collection in $collections) {
        $name = [string]$collection.name
        $domain = $name.Substring("knowledge_".Length)
        if ($domain.EndsWith("_v2", [System.StringComparison]::OrdinalIgnoreCase)) {
            $domain = $domain.Substring(0, $domain.Length - 3)
        }

        $countBody = @{ exact = $true } | ConvertTo-Json
        $count = (Invoke-RestMethod -Method Post -Uri ($BaseUrl.TrimEnd("/") + "/collections/" + $name + "/points/count") -Body $countBody -ContentType "application/json" -TimeoutSec 60).result.count
        if ([int]$count -gt 0) {
            $counts[$domain] = [int]$count
        }
    }

    return $counts
}

function Invoke-RetrievalAuditQuery {
    param(
        [Parameter(Mandatory = $true)][string]$BaseUrl,
        [Parameter(Mandatory = $true)][string]$Key,
        [Parameter(Mandatory = $true)][string]$Query,
        [Parameter(Mandatory = $true)][string]$ExpectedDomain,
        [Parameter(Mandatory = $true)][string]$ResolvedPipelineVersion,
        [Parameter(Mandatory = $true)][int]$ResolvedLimit,
        [Parameter(Mandatory = $true)][bool]$ApplyDomainFilter
    )

    $body = [ordered]@{
        query = $Query
        limit = $ResolvedLimit
        pipelineVersion = $ResolvedPipelineVersion
        includeContext = $true
    }

    if ($ApplyDomainFilter) {
        $body.domain = $ExpectedDomain
    }

    $response = Invoke-RestMethod -Method Post -Uri ($BaseUrl.TrimEnd("/") + "/api/rag/search") -Headers @{ "X-API-KEY" = $Key } -Body ($body | ConvertTo-Json -Depth 6) -ContentType "application/json" -TimeoutSec 120
    $items = @($response)
    $topDomains = @($items | ForEach-Object { [string]$_.metadata.domain })
    $firstExpectedRank = 0
    for ($index = 0; $index -lt $topDomains.Count; $index++) {
        if ($topDomains[$index] -eq $ExpectedDomain) {
            $firstExpectedRank = $index + 1
            break
        }
    }

    $top1 = if ($items.Count -gt 0) { $items[0] } else { $null }
    $top1Title = ""
    $top1SourcePath = ""
    if ($null -ne $top1) {
        $top1Title = [string]$top1.metadata.title
        $top1SourcePath = [string]$top1.metadata.source_path
    }

    return [pscustomobject]@{
        Query = $Query
        Top1Domain = if ($topDomains.Count -gt 0) { $topDomains[0] } else { "" }
        Top1Title = $top1Title
        Top1SourcePath = $top1SourcePath
        Top1Hit = ($topDomains.Count -gt 0 -and $topDomains[0] -eq $ExpectedDomain)
        Top3Hit = (@($topDomains | Select-Object -First 3 | Where-Object { $_ -eq $ExpectedDomain }).Count -gt 0)
        Top5Hit = (@($topDomains | Select-Object -First 5 | Where-Object { $_ -eq $ExpectedDomain }).Count -gt 0)
        FirstExpectedRank = $firstExpectedRank
        ReturnedDomains = (@($topDomains | Select-Object -Unique) -join ", ")
    }
}

$helperRoot = Split-Path -Parent $PSScriptRoot
$envFile = Join-Path $helperRoot ".env.local"
if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    $ApiKey = $env:HELPER_API_KEY
}
if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    $ApiKey = Get-EnvValueFromFile -Path $envFile -Name "HELPER_API_KEY"
}
if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    throw "HELPER_API_KEY is required."
}

$domainQueries = [ordered]@{
    analysis_strategy = @(
        "Что такое стратегия непрямых действий в войне?",
        "Как работает эвристика быстрого и медленного мышления?",
        "В чем состоит хорошая стратегия и плохая стратегия?",
        "Что означает антихрупкость системы?",
        "Как черные лебеди влияют на принятие решений?"
    )
    anatomy = @(
        "Где проходит плечевое сплетение?",
        "Как устроен круг Виллизия?",
        "Какие слои образуют стенку тонкой кишки?",
        "Чем отличаются большеберцовая и малоберцовая кости?",
        "Какие отверстия есть в основании черепа и что через них проходит?"
    )
    art_culture = @(
        "Какие основные эпохи выделяют в истории европейской живописи?",
        "Как развивалась идея единобожия в мировой культуре?",
        "Что делает нехудожественный текст ясным и сильным по стилю?",
        "Как распознавать псевдонауку и суеверное мышление?",
        "Чем отличаются мировые религии в представлении о священном?"
    )
    biology = @(
        "Как устроена клеточная мембрана и как идет транспорт веществ?",
        "Что такое экспрессия генов и как регулируется транскрипция?",
        "Как работает естественный отбор?",
        "Как передается сигнал через синапс?",
        "Чем отличаются митоз и мейоз?"
    )
    chemistry = @(
        "Что определяет кислотность и основность органических соединений?",
        "Как формулируется второй закон термодинамики в физической химии?",
        "Что такое SN1 и SN2 реакции?",
        "Как меняются свойства элементов по периодам и группам?",
        "Что описывает химическое равновесие Ле Шателье?"
    )
    computer_science = @(
        "Что такое dependency injection и зачем он нужен?",
        "Как устроены обобщения и ковариантность в C#?",
        "Какие принципы делают код поддерживаемым в больших проектах?",
        "Что такое machine learning systems в production?",
        "Как управлять зависимостями и архитектурой больших приложений?"
    )
    economics = @(
        "Что такое эластичность спроса и предложения?",
        "Почему одни нации богатеют, а другие нет?",
        "Как статистические искажения мешают понимать мировые тренды?",
        "Что означает системное мышление в экономике?",
        "Как работает сравнительное преимущество в торговле?"
    )
    encyclopedias = @(
        "Где находится пустыня Атакама?",
        "Что такое дофамин в кратком энциклопедическом определении?",
        "Где расположена Новая Зеландия?",
        "Кто такой Евклид?",
        "Что означает термин инфляция?"
    )
    english_lang_lit = @(
        "В чем смысл антиутопии 1984?",
        "Как раскрывается тема власти в Animal Farm?",
        "Что отличает комедии и трагедии Шекспира?",
        "Как развивается образ Элизабет Беннет в Pride and Prejudice?",
        "Что такое двоемыслие в английской литературе XX века?"
    )
    entomology = @(
        "Как устроена социальная организация муравьиной колонии?",
        "Какие этапы включает полное превращение насекомых?",
        "Чем насекомые отличаются от других членистоногих?",
        "Как эволюционировали крылья у насекомых?",
        "Что известно о кастах и коммуникации у муравьев?"
    )
    geology = @(
        "Какие текстуры характерны для магматических пород?",
        "Как образуются золоторудные месторождения?",
        "Что такое ударный кратер и как его распознают?",
        "Как формируются хромитовые и платиновые месторождения?",
        "Как определять минералы под микроскопом?"
    )
    historical_encyclopedias = @(
        "Что известно об Анголе в историко-энциклопедическом контексте?",
        "Кто такой Ибсен?",
        "Что означает экслибрис?",
        "Что можно кратко сказать об Италии как историко-культурной теме?",
        "Что такое СССР в энциклопедическом описании?"
    )
    history = @(
        "Какие причины привели к Первой мировой войне?",
        "Как возникли первые государства Древнего Востока?",
        "Что изменилось в ходе неолитической революции?",
        "Как развивалась Римская империя в период расцвета?",
        "Что такое индустриальная революция и почему она важна?"
    )
    linguistics = @(
        "Что такое эргативность в типологии языков?",
        "Чем аналитические языки отличаются от синтетических?",
        "Что такое порядок слов SOV?",
        "Как выражается грамматическое число в языках мира?",
        "Что такое тональный язык?"
    )
    math = @(
        "Что такое собственные значения матрицы?",
        "Как формулируется теорема о среднем в математическом анализе?",
        "Что такое ряд Тейлора?",
        "Как решаются обыкновенные дифференциальные уравнения первого порядка?",
        "Что означает полнота вещественной прямой?"
    )
    medicine = @(
        "Какие клинические признаки характерны для сердечной недостаточности?",
        "Чем отличаются вирусная и бактериальная пневмония?",
        "Что такое диабет второго типа?",
        "Как диагностируют анемию?",
        "Какие принципы лечения артериальной гипертензии?"
    )
    mythology_religion = @(
        "Кто такой Зевс в греческой мифологии?",
        "Что означает путешествие героя в мифе?",
        "Каковы основные мотивы скандинавской мифологии?",
        "Чем миф отличается от религиозного ритуала?",
        "Кто такие олимпийские боги?"
    )
    neuro = @(
        "Как формируется долговременная память в мозге?",
        "Как работает потенциал действия нейрона?",
        "Что такое долговременная потенциация?",
        "Как зрительная информация проходит по мозгу?",
        "Как базальные ганглии участвуют в движении?"
    )
    philosophy = @(
        "Что такое вещь-в-себе у Канта?",
        "В чем состоит метод Сократа?",
        "Что такое эмпиризм и рационализм?",
        "Как Ницше понимает переоценку ценностей?",
        "Что такое категорический императив?"
    )
    physics = @(
        "Что такое принцип наименьшего действия?",
        "Как записываются уравнения Максвелла?",
        "Что такое каноническое распределение Гиббса?",
        "Как работает квантовое туннелирование?",
        "Что такое тензор энергии-импульса?"
    )
    psychology = @(
        "Что такое эго по Фрейду?",
        "Как Юнг описывает психологические типы?",
        "Что такое индивидуация?",
        "Чем отличаются интроверсия и экстраверсия?",
        "Как соотносятся ид, эго и суперэго?"
    )
    robotics = @(
        "Что такое обратная кинематика манипулятора?",
        "Как записываются параметры Денавита-Хартенберга?",
        "Что изучает кибернетика?",
        "Как работает обратная связь в системе управления?",
        "Что такое рабочее пространство робота?"
    )
    russian_lang_lit = @(
        "Как раскрывается тема войны и мира у Толстого?",
        "Как Чехов строит короткий рассказ?",
        "Что характерно для русской поэзии Серебряного века?",
        "Как Пушкин влияет на русский литературный язык?",
        "В чем особенности русской реалистической прозы XIX века?"
    )
    sci_fi_concepts = @(
        "Что такое психоистория в цикле Основание?",
        "Как работает идея Галактической Империи у Азимова?",
        "Кто такая Мул и почему он важен для Основания?",
        "Что означает кризис Селдона?",
        "Как тема колонизации будущего раскрывается в science fiction?"
    )
    social_sciences = @(
        "Что такое когнитивное искажение?",
        "Как работает эмоциональный интеллект?",
        "Какие факторы влияют на социальное влияние и убеждение?",
        "Как вести принципиальные переговоры?",
        "Что делает моральные суждения разными у разных людей?"
    )
    virology = @(
        "Как классифицируют вирусы?",
        "Что такое РНК-вирус?",
        "Как вирус входит в клетку-хозяина?",
        "Чем отличаются оболочечные и безоболочечные вирусы?",
        "Что такое вирусная репликация?"
    )
}

$nonEmptyDomainCounts = Get-NonEmptyDomainCounts -BaseUrl $QdrantBaseUrl
$targetDomains = @($domainQueries.Keys | Where-Object { $nonEmptyDomainCounts.Contains($_) } | Sort-Object)
if ($targetDomains.Count -eq 0) {
    throw "No non-empty library domains were found in Qdrant."
}

$stamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $helperRoot ("runtime\\library_retrieval_audit_{0}.md" -f $stamp)
}
if ([string]::IsNullOrWhiteSpace($JsonOutputPath)) {
    $JsonOutputPath = Join-Path $helperRoot ("runtime\\library_retrieval_audit_{0}.json" -f $stamp)
}

$results = New-Object System.Collections.Generic.List[object]
foreach ($domain in $targetDomains) {
    $queries = @($domainQueries[$domain])
    for ($index = 0; $index -lt $queries.Count; $index++) {
        $audit = Invoke-RetrievalAuditQuery `
            -BaseUrl $ApiBaseUrl `
            -Key $ApiKey `
            -Query $queries[$index] `
            -ExpectedDomain $domain `
            -ResolvedPipelineVersion $PipelineVersion `
            -ResolvedLimit $Limit `
            -ApplyDomainFilter:$UseDomainFilter.IsPresent

        $results.Add([pscustomobject]@{
            Domain = $domain
            CollectionPointCount = [int]$nonEmptyDomainCounts[$domain]
            QueryIndex = $index + 1
            Query = $queries[$index]
            Top1Domain = $audit.Top1Domain
            Top1Title = $audit.Top1Title
            Top1SourcePath = $audit.Top1SourcePath
            Top1Hit = [bool]$audit.Top1Hit
            Top3Hit = [bool]$audit.Top3Hit
            Top5Hit = [bool]$audit.Top5Hit
            FirstExpectedRank = [int]$audit.FirstExpectedRank
            ReturnedDomains = $audit.ReturnedDomains
        }) | Out-Null
    }
}

$resultArray = $results.ToArray()
$overallTop1 = @($resultArray | Where-Object { $_.Top1Hit }).Count
$overallTop3 = @($resultArray | Where-Object { $_.Top3Hit }).Count
$overallTop5 = @($resultArray | Where-Object { $_.Top5Hit }).Count
$overallCount = [double]$resultArray.Count

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# Library Retrieval Audit")
$lines.Add("")
$lines.Add(("- GeneratedAt: {0}" -f (Get-Date).ToString("s")))
$lines.Add(("- ApiBaseUrl: {0}" -f $ApiBaseUrl))
$lines.Add(("- QdrantBaseUrl: {0}" -f $QdrantBaseUrl))
$lines.Add(("- PipelineVersion: {0}" -f $PipelineVersion))
$lines.Add("- IncludeContext: true")
$lines.Add(("- UseDomainFilter: {0}" -f $UseDomainFilter.IsPresent))
$lines.Add(("- Limit: {0}" -f $Limit))
$lines.Add(("- NonEmptyDomains: {0}" -f $targetDomains.Count))
$lines.Add("- QueriesPerDomain: 5")
$lines.Add(("- TotalQueries: {0}" -f $resultArray.Count))
$lines.Add("")
$lines.Add("## Overall Summary")
$lines.Add("")
$lines.Add(("- Top1DomainHit: {0}/{1} ({2:P1})" -f $overallTop1, [int]$overallCount, ($overallTop1 / $overallCount)))
$lines.Add(("- Top3DomainHit: {0}/{1} ({2:P1})" -f $overallTop3, [int]$overallCount, ($overallTop3 / $overallCount)))
$lines.Add(("- Top5DomainHit: {0}/{1} ({2:P1})" -f $overallTop5, [int]$overallCount, ($overallTop5 / $overallCount)))
$lines.Add("")
$lines.Add("## Domain Summary")
$lines.Add("")
$lines.Add("| Domain | Points | Top1 | Top3 | Top5 | AvgFirstExpectedRank | Common Top1 Misroutes |")
$lines.Add("| --- | ---: | ---: | ---: | ---: | ---: | --- |")

$domainGroups = @($resultArray | Group-Object Domain | Sort-Object Name)
foreach ($group in $domainGroups) {
    $rows = @($group.Group)
    $points = [int]$rows[0].CollectionPointCount
    $top1 = @($rows | Where-Object { $_.Top1Hit }).Count
    $top3 = @($rows | Where-Object { $_.Top3Hit }).Count
    $top5 = @($rows | Where-Object { $_.Top5Hit }).Count
    $ranked = @($rows | Where-Object { $_.FirstExpectedRank -gt 0 } | ForEach-Object { [double]$_.FirstExpectedRank })
    $avgRank = if ($ranked.Count -gt 0) { [math]::Round((($ranked | Measure-Object -Average).Average), 2) } else { 0 }
    $misroutes = @($rows | Where-Object { -not $_.Top1Hit -and -not [string]::IsNullOrWhiteSpace($_.Top1Domain) })
    $misrouteText = if ($misroutes.Count -eq 0) {
        "-"
    }
    else {
        (($misroutes | Group-Object Top1Domain | Sort-Object Count -Descending | Select-Object -First 3 | ForEach-Object { "{0} x{1}" -f $_.Name, $_.Count }) -join ", ")
    }

    $lines.Add(("| {0} | {1} | {2}/5 | {3}/5 | {4}/5 | {5} | {6} |" -f $group.Name, $points, $top1, $top3, $top5, $avgRank, (Escape-MarkdownCell $misrouteText)))
}

$lines.Add("")
$lines.Add("## Query Details")
$lines.Add("")

foreach ($group in $domainGroups) {
    $lines.Add(("### {0}" -f $group.Name))
    $lines.Add("")
    $lines.Add(("Collection points: {0}" -f [int]$group.Group[0].CollectionPointCount))
    $lines.Add("")
    $lines.Add("| # | Query | Top1Domain | Top1Title | FirstExpectedRank | Top1 | Top3 | Top5 | ReturnedDomains |")
    $lines.Add("| ---: | --- | --- | --- | ---: | ---: | ---: | ---: | --- |")
    foreach ($row in @($group.Group | Sort-Object QueryIndex)) {
        $lines.Add(("| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} |" -f `
            $row.QueryIndex, `
            (Escape-MarkdownCell $row.Query), `
            (Escape-MarkdownCell $row.Top1Domain), `
            (Escape-MarkdownCell $row.Top1Title), `
            $row.FirstExpectedRank, `
            $(if ($row.Top1Hit) { "yes" } else { "no" }), `
            $(if ($row.Top3Hit) { "yes" } else { "no" }), `
            $(if ($row.Top5Hit) { "yes" } else { "no" }), `
            (Escape-MarkdownCell $row.ReturnedDomains)))
    }
    $lines.Add("")
}

New-Item -ItemType Directory -Force -Path ([System.IO.Path]::GetDirectoryName($ReportPath)) | Out-Null
Set-Content -LiteralPath $ReportPath -Value ($lines -join "`r`n") -Encoding UTF8
Set-Content -LiteralPath $JsonOutputPath -Value ($resultArray | ConvertTo-Json -Depth 6) -Encoding UTF8

[pscustomobject]@{
    ReportPath = $ReportPath
    JsonOutputPath = $JsonOutputPath
    NonEmptyDomains = $targetDomains.Count
    TotalQueries = $resultArray.Count
    Top1DomainHit = $overallTop1
    Top3DomainHit = $overallTop3
    Top5DomainHit = $overallTop5
}
