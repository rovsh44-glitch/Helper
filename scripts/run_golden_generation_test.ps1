Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "common\Resolve-HelperPaths.ps1")
$pathConfig = Get-HelperPathConfig -WorkspaceRoot (Join-Path $PSScriptRoot "..")
Import-HelperEnvFile -Path (Join-Path $pathConfig.HelperRoot ".env.local")

$apiKey = $env:HELPER_API_KEY
if ([string]::IsNullOrWhiteSpace($apiKey)) {
    throw "HELPER_API_KEY is required. Set it in .env.local or the current environment."
}

$apiBaseRoot = if ($env:VITE_HELPER_API_BASE) { $env:VITE_HELPER_API_BASE.TrimEnd('/') } else { "http://localhost:5000" }
$apiBase = "$apiBaseRoot/api/helper/generate"
$promptsFile = Join-Path $pathConfig.HelperRoot "HELPER_GOLDEN_PROMPTS_LIST_2026.md"
$reportFile = Join-Path $pathConfig.DocRoot "GENERATION_TEST_REPORT_20260305.md"
$sourceExtensions = @(".cs", ".xaml", ".js", ".cjs", ".mjs", ".ts", ".tsx", ".py", ".json")

New-Item -ItemType Directory -Force -Path $pathConfig.DocRoot | Out-Null

$content = Get-Content -Path $promptsFile -Raw -Encoding UTF8
$matches = [regex]::Matches($content, '### (.+)\r?\n\*   \*\*RU:\*\* "(.+)"\r?\n\*   \*\*EN:\*\* "(.+)"')

$results = @()
$total = $matches.Count * 2
$current = 0

Write-Host "Starting generation of $total projects..."

foreach ($match in $matches) {
    $templateName = $match.Groups[1].Value.Trim()
    $ruPrompt = $match.Groups[2].Value.Trim()
    $enPrompt = $match.Groups[3].Value.Trim()

    $prompts = @(
        @{ Lang = "RU"; Prompt = $ruPrompt },
        @{ Lang = "EN"; Prompt = $enPrompt }
    )

    foreach ($p in $prompts) {
        $current++
        $safeName = $templateName -replace "[^a-zA-Z0-9]", "_"
        $outputPath = Join-Path $pathConfig.ProjectsRoot "GoldenTest_$($safeName)_$($p.Lang)"

        Write-Host "[$current/$total] Generating $($templateName) ($($p.Lang))..."

        $body = @{
            Prompt = $p.Prompt
            OutputPath = $outputPath
        } | ConvertTo-Json -Depth 5 -Compress

        $sw = [Diagnostics.Stopwatch]::StartNew()
        try {
            $res = Invoke-RestMethod -Uri $apiBase -Method Post -Headers @{ "X-Api-Key" = $apiKey } -ContentType "application/json; charset=utf-8" -Body $body -TimeoutSec 300
            $sw.Stop()

            $success = [bool]$res.success
            $errors = @()
            if ($res.errors) {
                $errors = @($res.errors)
            }

            $actualPath = $res.projectPath
            $duration = $sw.Elapsed.TotalSeconds
            $fileCount = 0
            $sourceFileCount = 0
            $zeroByteSourceCount = 0
            $routeMatched = $false
            $goldenTemplateMatched = $false
            $routedTemplateId = ""
            $domainMismatchCount = 0

            if ($actualPath -and (Test-Path $actualPath)) {
                $files = Get-ChildItem -Path $actualPath -Recurse -File
                $fileCount = @($files).Count
                $sourceFiles = @($files | Where-Object { $sourceExtensions -contains $_.Extension.ToLowerInvariant() })
                $sourceFileCount = $sourceFiles.Count
                $zeroByteSourceCount = @($sourceFiles | Where-Object { $_.Length -eq 0 }).Count

                $validationReportPath = Join-Path $actualPath "validation_report.json"
                if (Test-Path $validationReportPath) {
                    try {
                        $validation = Get-Content -Path $validationReportPath -Raw -Encoding UTF8 | ConvertFrom-Json
                        $routeMatched = ($validation.RouteMatched -eq $true)
                        $goldenTemplateMatched = ($validation.GoldenTemplateMatched -eq $true)
                        $routedTemplateId = [string]$validation.RoutedTemplateId
                        $projectName = [string]$validation.ProjectName
                        $isChessTemplate = ($templateName -match "Chess")
                        $chessDrift = (($projectName -match "Chess") -or ($routedTemplateId -match "Chess")) -and -not $isChessTemplate
                        if ($chessDrift) {
                            $domainMismatchCount = 1
                        }
                    }
                    catch {
                        $errors += "Failed to parse validation_report.json: $($_.Exception.Message)"
                    }
                }
            }

            if ($zeroByteSourceCount -gt 0) {
                $success = $false
                $errors += "Zero-byte source files detected: $zeroByteSourceCount"
            }

            if (-not $routeMatched) {
                $success = $false
                $errors += "RouteMatched is false or missing."
            }

            if (-not $goldenTemplateMatched) {
                $success = $false
                $errors += "GoldenTemplateMatched is false or missing."
            }

            if ($domainMismatchCount -gt 0) {
                $success = $false
                $errors += "Domain mismatch detected (Chess drift)."
            }

            if (-not $success -and @($errors).Count -eq 0) {
                $errors += "Generation failed with no diagnostic errors."
            }

            $results += @{
                Template = $templateName
                Lang = $p.Lang
                Prompt = $p.Prompt
                Success = $success
                Duration = $duration
                FileCount = $fileCount
                SourceFileCount = $sourceFileCount
                ZeroByteSourceCount = $zeroByteSourceCount
                RouteMatched = $routeMatched
                GoldenTemplateMatched = $goldenTemplateMatched
                DomainMismatchCount = $domainMismatchCount
                RoutedTemplateId = $routedTemplateId
                Errors = $errors
                Path = $actualPath
            }
        }
        catch {
            $sw.Stop()
            $errText = $_.Exception.Message
            if ($_.Exception.Response) {
                $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
                $errText += " | Body: " + $reader.ReadToEnd()
            }

            $results += @{
                Template = $templateName
                Lang = $p.Lang
                Prompt = $p.Prompt
                Success = $false
                Duration = $sw.Elapsed.TotalSeconds
                FileCount = 0
                SourceFileCount = 0
                ZeroByteSourceCount = 0
                RouteMatched = $false
                GoldenTemplateMatched = $false
                DomainMismatchCount = 0
                RoutedTemplateId = ""
                Errors = @($errText)
                Path = $outputPath
            }
            Write-Host "Error: $errText"
        }
    }
}

$successCount = @($results | Where-Object { $_.Success -eq $true }).Count
$failedCount = @($results | Where-Object { $_.Success -eq $false }).Count

$report = "# Generation Run Report for 17 Golden Templates (34 requests)`n`n"
$report += "Date: $(Get-Date)`n"
$report += "Total Requests: $total`n"
$report += "Success: $successCount`n"
$report += "Failed: $failedCount`n`n"

$report += "| Template | Lang | Status | Files | Duration (s) | Errors | Route | Golden | Mismatch | Zero-byte sources |`n"
$report += "| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |`n"

foreach ($r in $results) {
    $status = if ($r.Success) { "OK" } else { "FAIL" }
    $errs = if ($r.Errors) { @($r.Errors).Count } else { 0 }
    $route = if ($r.RouteMatched) { "yes" } else { "no" }
    $golden = if ($r.GoldenTemplateMatched) { "yes" } else { "no" }
    $mismatch = $r.DomainMismatchCount
    $report += "| $($r.Template) | $($r.Lang) | $status | $($r.FileCount) | $($r.Duration.ToString('F2')) | $errs | $route | $golden | $mismatch | $($r.ZeroByteSourceCount) |`n"
}

$report += "`n## File Details`n"
foreach ($r in $results) {
    if ($r.Success) {
        $report += "`n### $($r.Template) ($($r.Lang))`n- Path: ``$($r.Path)``"
        if (Test-Path "$($r.Path)\template.json") {
            $report += "`n- template.json: Found"
        }
        $files = Get-ChildItem -Path $r.Path -Recurse -File | Select-Object -First 5
        $report += "`n- Sample Files:`n"
        foreach ($f in $files) {
            $report += "  - $($f.Name)`n"
        }
    }
}

Set-Content -Path $reportFile -Value $report -Encoding UTF8
Write-Host "Report saved to $reportFile"
