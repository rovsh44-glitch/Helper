param(
    [string]$ApiBaseUrl = "",
    [string]$UiUrl = "",
    [string]$OutputJsonPath = "temp/verification/ui_workflow_smoke.json",
    [string]$OutputMarkdownPath = "temp/verification/ui_workflow_smoke.md",
    [string]$WorkspaceRoot = "",
    [int]$TimeoutSec = 120,
    [switch]$RequireConfiguredRuntime
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot "common\Resolve-HelperPaths.ps1")
. (Join-Path $PSScriptRoot "common\RuntimeSmokeCommon.ps1")

function Resolve-DefaultWorkspaceRoot {
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $null = Get-HelperPathConfig -WorkspaceRoot (Join-Path $PSScriptRoot "..")
    $explicitWorkspaceRoot = $env:HELPER_RUNTIME_SMOKE_WORKSPACE_ROOT
    if (-not [string]::IsNullOrWhiteSpace($explicitWorkspaceRoot)) {
        return Join-Path $explicitWorkspaceRoot ("ui_workflow_smoke_" + $stamp)
    }

    $projectsRoot = $env:HELPER_PROJECTS_ROOT
    if ([string]::IsNullOrWhiteSpace($projectsRoot)) {
        $dataRoot = $env:HELPER_DATA_ROOT
        if (-not [string]::IsNullOrWhiteSpace($dataRoot)) {
            $projectsRoot = Join-Path $dataRoot "PROJECTS"
        }
    }

    if ([string]::IsNullOrWhiteSpace($projectsRoot)) {
        return $null
    }

    return Join-Path $projectsRoot ("ui_workflow_smoke_" + $stamp)
}

function ConvertTo-FlatArray {
    param(
        [Parameter(ValueFromPipeline = $true)]
        $InputObject
    )

    process {
        if ($null -eq $InputObject) {
            return @()
        }

        $items = @($InputObject)
        if ($items.Count -eq 1 -and
            $items[0] -is [System.Collections.IEnumerable] -and
            -not ($items[0] -is [string]) -and
            -not ($items[0] -is [hashtable]) -and
            -not ($items[0] -is [pscustomobject])) {
            return @($items[0])
        }

        return $items
    }
}

function Invoke-SmokeScenario {
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$Title,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $details = & $Action
        $stopwatch.Stop()
        return [ordered]@{
            id = $Id
            title = $Title
            status = "PASS"
            durationMs = [int]$stopwatch.ElapsedMilliseconds
            details = if ([string]::IsNullOrWhiteSpace([string]$details)) { "ok" } else { [string]$details }
        }
    }
    catch {
        $stopwatch.Stop()
        return [ordered]@{
            id = $Id
            title = $Title
            status = "FAIL"
            durationMs = [int]$stopwatch.ElapsedMilliseconds
            details = $_.Exception.Message
        }
    }
}

function Invoke-JsonRequest {
    param(
        [Parameter(Mandatory = $true)][ValidateSet("GET", "POST", "PUT", "DELETE")] [string]$Method,
        [Parameter(Mandatory = $true)][string]$Uri,
        [hashtable]$Headers,
        $Body = $null
    )

    $request = @{
        Method = $Method
        Uri = $Uri
        TimeoutSec = $TimeoutSec
    }

    if ($Headers) {
        $request.Headers = $Headers
    }

    if ($null -ne $Body) {
        $request.ContentType = "application/json"
        $request.Body = ($Body | ConvertTo-Json -Depth 8)
    }

    return Invoke-RestMethod @request
}

function Write-WorkflowSmokeArtifacts {
    param(
        [Parameter(Mandatory = $true)]$Result,
        [Parameter(Mandatory = $true)][string]$JsonPath,
        [Parameter(Mandatory = $true)][string]$MarkdownPath
    )

    $jsonDirectory = Split-Path -Parent $JsonPath
    $markdownDirectory = Split-Path -Parent $MarkdownPath
    if (-not [string]::IsNullOrWhiteSpace($jsonDirectory)) {
        New-Item -ItemType Directory -Path $jsonDirectory -Force | Out-Null
    }
    if (-not [string]::IsNullOrWhiteSpace($markdownDirectory)) {
        New-Item -ItemType Directory -Path $markdownDirectory -Force | Out-Null
    }

    Set-Content -Path $JsonPath -Value ($Result | ConvertTo-Json -Depth 10) -Encoding UTF8

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# UI Workflow Smoke")
    $lines.Add("")
    $lines.Add(('Generated at: `{0}`' -f $Result.generatedAtUtc))
    $lines.Add(('API base: `{0}`' -f $Result.apiBaseUrl))
    if (-not [string]::IsNullOrWhiteSpace([string]$Result.uiUrl)) {
        $lines.Add(('UI url: `{0}`' -f $Result.uiUrl))
    }
    $lines.Add(('Overall status: `{0}`' -f $Result.status))
    $lines.Add(('Workspace root: `{0}`' -f $Result.workspaceRoot))
    $lines.Add("")
    $lines.Add("| Scenario | Status | Duration ms | Details |")
    $lines.Add("|---|---|---:|---|")
    foreach ($scenario in @($Result.scenarios)) {
        $lines.Add(('| {0} | `{1}` | {2} | {3} |' -f $scenario.title, $scenario.status, $scenario.durationMs, ([string]$scenario.details).Replace("|", "/")))
    }

    if (@($Result.failures).Count -gt 0) {
        $lines.Add("")
        $lines.Add("## Failures")
        $lines.Add("")
        $index = 1
        foreach ($failure in @($Result.failures)) {
            $lines.Add(("{0}. {1}" -f $index, $failure))
            $index += 1
        }
    }

    Set-Content -Path $MarkdownPath -Value ($lines -join "`r`n") -Encoding UTF8
}

function Test-IsPermissionLimitedFailure {
    param(
        [Parameter(Mandatory = $true)]
        [System.Management.Automation.ErrorRecord]$ErrorRecord
    )

    $message = [string]$ErrorRecord.Exception.Message
    $detail = if ($null -ne $ErrorRecord.ErrorDetails) {
        [string]$ErrorRecord.ErrorDetails.Message
    }
    else {
        ""
    }
    return ($message -match "\(403\)\s*Forbidden" -or
        $message -match "Access to the path is denied" -or
        $detail -match "Access to the path is denied")
}

function Get-WorkspaceNodePaths {
    param(
        [Parameter(Mandatory = $true)]
        $Folder
    )

    $paths = New-Object System.Collections.Generic.List[string]

    foreach ($file in @($Folder.files)) {
        if ($null -ne $file -and -not [string]::IsNullOrWhiteSpace([string]$file.path)) {
            $paths.Add([string]$file.path)
        }
    }

    foreach ($child in @($Folder.folders)) {
        if ($null -ne $child -and -not [string]::IsNullOrWhiteSpace([string]$child.path)) {
            $paths.Add([string]$child.path)
        }

        foreach ($nested in @(Get-WorkspaceNodePaths -Folder $child)) {
            $paths.Add([string]$nested)
        }
    }

    return @($paths.ToArray())
}

if ([string]::IsNullOrWhiteSpace($ApiBaseUrl)) {
    $ApiBaseUrl = $env:HELPER_RUNTIME_SMOKE_API_BASE
}

if ([string]::IsNullOrWhiteSpace($UiUrl)) {
    $UiUrl = $env:HELPER_RUNTIME_SMOKE_UI_URL
}

if ([string]::IsNullOrWhiteSpace($ApiBaseUrl)) {
    $skipMessage = "UI workflow smoke skipped because HELPER_RUNTIME_SMOKE_API_BASE was not configured."
    $resultStatus = if ($RequireConfiguredRuntime.IsPresent) { "FAIL" } else { "SKIPPED" }
    $result = [ordered]@{
        schemaVersion = 1
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        apiBaseUrl = ""
        uiUrl = $UiUrl
        workspaceRoot = ""
        status = $resultStatus
        scenarios = @()
        failures = @($skipMessage)
    }
    Write-WorkflowSmokeArtifacts -Result $result -JsonPath $OutputJsonPath -MarkdownPath $OutputMarkdownPath
    if ($RequireConfiguredRuntime.IsPresent) {
        throw "[UI Smoke] HELPER_RUNTIME_SMOKE_API_BASE is required for release verification."
    }

    Write-Host "[UI Smoke] Skipped: HELPER_RUNTIME_SMOKE_API_BASE not set." -ForegroundColor Yellow
    exit 0
}

if ([string]::IsNullOrWhiteSpace($WorkspaceRoot)) {
    $WorkspaceRoot = Resolve-DefaultWorkspaceRoot
}

if ([string]::IsNullOrWhiteSpace($WorkspaceRoot)) {
    $skipMessage = "UI workflow smoke requires HELPER_RUNTIME_SMOKE_WORKSPACE_ROOT, HELPER_PROJECTS_ROOT, or HELPER_DATA_ROOT."
    $resultStatus = if ($RequireConfiguredRuntime.IsPresent) { "FAIL" } else { "SKIPPED" }
    $result = [ordered]@{
        schemaVersion = 1
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        apiBaseUrl = $ApiBaseUrl
        uiUrl = $UiUrl
        workspaceRoot = ""
        status = $resultStatus
        scenarios = @()
        failures = @($skipMessage)
    }
    Write-WorkflowSmokeArtifacts -Result $result -JsonPath $OutputJsonPath -MarkdownPath $OutputMarkdownPath
    if ($RequireConfiguredRuntime.IsPresent) {
        throw "[UI Smoke] Configured runtime workspace root is required for release verification."
    }

    Write-Host "[UI Smoke] Skipped: runtime workspace root is not configured." -ForegroundColor Yellow
    exit 0
}

New-Item -ItemType Directory -Path $WorkspaceRoot -Force | Out-Null

$conversationHeaders = New-SessionHeaders -ApiBase $ApiBaseUrl -Surface "conversation" -RequestedScopes @("chat:read", "chat:write")
$builderHeaders = New-SessionHeaders -ApiBase $ApiBaseUrl -Surface "builder" -RequestedScopes @("chat:read", "chat:write", "tools:execute", "build:run", "fs:write")
$runtimeHeaders = New-SessionHeaders -ApiBase $ApiBaseUrl -Surface "runtime_console" -RequestedScopes @("metrics:read")
$evolutionHeaders = New-SessionHeaders -ApiBase $ApiBaseUrl -Surface "evolution" -RequestedScopes @("evolution:control", "metrics:read")

$workspaceProjectFile = Join-Path $WorkspaceRoot "Project.csproj"
$workspaceEntryFile = Join-Path $WorkspaceRoot "Program.cs"
$workspaceScratchFile = Join-Path $WorkspaceRoot "scratch.txt"

$workspaceProjectContent = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
"@

$workspaceProgramContent = @"
Console.WriteLine("ui_workflow_smoke");
"@

$conversationId = ""
$addedGoal = $null
$strategyResult = $null
$architectureResult = $null

$scenarios = @(
    Invoke-SmokeScenario -Id "readiness" -Title "Readiness and optional UI reachability" -Action {
        $readiness = Invoke-JsonRequest -Method "GET" -Uri "$ApiBaseUrl/api/readiness"
        Assert-Condition ([bool]$readiness.readyForChat) "API readiness is false. Phase=$($readiness.phase)"
        if (-not [string]::IsNullOrWhiteSpace($UiUrl)) {
            $uiResponse = Invoke-WebRequest -UseBasicParsing -Uri $UiUrl -TimeoutSec $TimeoutSec
            Assert-Condition ($uiResponse.StatusCode -ge 200 -and $uiResponse.StatusCode -lt 500) "UI check failed with status $($uiResponse.StatusCode)."
        }
        return "readyForChat=$($readiness.readyForChat)"
    }

    Invoke-SmokeScenario -Id "conversation" -Title "Conversation send and restore" -Action {
        $chatResponse = Invoke-JsonRequest -Method "POST" -Uri "$ApiBaseUrl/api/chat" -Headers $conversationHeaders -Body @{
            message = "__HELPER_SMOKE_READY__"
            maxHistory = 4
            systemInstruction = "deterministic_smoke"
        }
        Assert-Condition ($chatResponse.response -eq "READY") "Unexpected deterministic response '$($chatResponse.response)'."
        Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$chatResponse.conversationId)) "Conversation id was not returned."
        $script:conversationId = [string]$chatResponse.conversationId
        $conversation = Invoke-JsonRequest -Method "GET" -Uri "$ApiBaseUrl/api/chat/$conversationId" -Headers $conversationHeaders
        Assert-Condition (@($conversation.messages).Count -ge 2) "Conversation snapshot did not include the expected turns."
        return "conversationId=$conversationId"
    }

    Invoke-SmokeScenario -Id "settings" -Title "Settings governance path" -Action {
        Assert-Condition (-not [string]::IsNullOrWhiteSpace($conversationId)) "Conversation id was not initialized for settings verification."

        Invoke-JsonRequest -Method "POST" -Uri "$ApiBaseUrl/api/chat/$conversationId/preferences" -Headers $conversationHeaders -Body @{
            longTermMemoryEnabled = $true
            preferredLanguage = "en"
            detailLevel = "detailed"
            warmth = "balanced"
            enthusiasm = "balanced"
            directness = "balanced"
            defaultAnswerShape = "auto"
            sessionMemoryTtlMinutes = 120
            taskMemoryTtlHours = 48
            longTermMemoryTtlDays = 30
        } | Out-Null

        $conversationSnapshot = Invoke-JsonRequest -Method "GET" -Uri "$ApiBaseUrl/api/chat/$conversationId" -Headers $conversationHeaders
        Assert-Condition ($null -ne $conversationSnapshot.preferences) "Conversation preferences were not returned after save."
        Assert-Condition ([string]$conversationSnapshot.preferences.preferredLanguage -eq "en") "Preferred language was not persisted."
        Assert-Condition ([string]$conversationSnapshot.preferences.detailLevel -eq "detailed") "Detail level was not persisted."

        $memorySnapshot = Invoke-JsonRequest -Method "GET" -Uri "$ApiBaseUrl/api/chat/$conversationId/memory" -Headers $conversationHeaders
        Assert-Condition ($null -ne $memorySnapshot.policy) "Memory policy snapshot was not returned."
        Assert-Condition ([int]$memorySnapshot.policy.sessionMemoryTtlMinutes -eq 120) "Session TTL did not round-trip."
        Assert-Condition ([int]$memorySnapshot.policy.taskMemoryTtlHours -eq 48) "Task TTL did not round-trip."
        Assert-Condition ([int]$memorySnapshot.policy.longTermMemoryTtlDays -eq 30) "Long-term TTL did not round-trip."
        return "language=$($conversationSnapshot.preferences.preferredLanguage); memoryItems=$(@($memorySnapshot.items).Count)"
    }

    Invoke-SmokeScenario -Id "goals" -Title "Objectives lifecycle" -Action {
        $goalTitle = "UI smoke goal $(Get-Date -Format 'HHmmss')"
        Invoke-JsonRequest -Method "POST" -Uri "$ApiBaseUrl/api/goals" -Headers $conversationHeaders -Body @{
            title = $goalTitle
            description = "Release-smoke objective"
        } | Out-Null

        $goals = ConvertTo-FlatArray (Invoke-JsonRequest -Method "GET" -Uri "$ApiBaseUrl/api/goals?includeCompleted=true" -Headers $conversationHeaders)
        $script:addedGoal = $goals | Where-Object { $_.title -eq $goalTitle } | Select-Object -First 1
        Assert-Condition ($null -ne $addedGoal) "New goal was not returned by /api/goals."

        Invoke-JsonRequest -Method "POST" -Uri "$ApiBaseUrl/api/goals/$($addedGoal.id)/complete" -Headers $conversationHeaders | Out-Null
        $completedGoals = ConvertTo-FlatArray (Invoke-JsonRequest -Method "GET" -Uri "$ApiBaseUrl/api/goals?includeCompleted=true" -Headers $conversationHeaders)
        $completedGoal = $completedGoals | Where-Object { $_.id -eq $addedGoal.id } | Select-Object -First 1
        Assert-Condition ([bool]$completedGoal.isCompleted) "Goal was not marked completed."

        Invoke-JsonRequest -Method "DELETE" -Uri "$ApiBaseUrl/api/goals/$($addedGoal.id)" -Headers $conversationHeaders | Out-Null
        $remainingGoals = ConvertTo-FlatArray (Invoke-JsonRequest -Method "GET" -Uri "$ApiBaseUrl/api/goals?includeCompleted=true" -Headers $conversationHeaders)
        Assert-Condition (-not ($remainingGoals | Where-Object { $_.id -eq $addedGoal.id })) "Goal delete did not remove the goal."
        return "goalId=$($addedGoal.id)"
    }

    Invoke-SmokeScenario -Id "strategy" -Title "Strategic map planning" -Action {
        $script:strategyResult = Invoke-JsonRequest -Method "POST" -Uri "$ApiBaseUrl/api/strategy/plan" -Headers $builderHeaders -Body @{
            task = "Create a guarded command-line note utility with workspace persistence."
            context = "Prefer deterministic routes and minimal dependencies."
        }
        Assert-Condition ($null -ne $strategyResult.plan) "Strategy plan payload missing."
        Assert-Condition (@($strategyResult.plan.options).Count -gt 0) "Strategy plan returned no options."
        Assert-Condition ($null -ne $strategyResult.route) "Strategy route payload missing."
        Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$strategyResult.route.reason)) "Strategy route reason was empty."
        if ([bool]$strategyResult.route.matched) {
            Assert-Condition (-not [string]::IsNullOrWhiteSpace([string]$strategyResult.route.templateId)) "Matched strategy route did not include templateId."
        }
        $routeDetails = if ([bool]$strategyResult.route.matched) {
            "matched:$($strategyResult.route.templateId)"
        }
        else {
            "unmatched"
        }
        return "route=$routeDetails"
    }

    Invoke-SmokeScenario -Id "architecture" -Title "Architecture planner" -Action {
        $script:architectureResult = Invoke-JsonRequest -Method "POST" -Uri "$ApiBaseUrl/api/architecture/plan" -Headers $builderHeaders -Body @{
            prompt = "Create a guarded command-line note utility with workspace persistence."
            targetOs = "windows"
        }
        Assert-Condition ($null -ne $architectureResult.plan) "Architecture plan payload missing."
        Assert-Condition ([bool]$architectureResult.blueprintValid) "Architecture blueprint was not valid."
        Assert-Condition (@($architectureResult.blueprint.files).Count -gt 0) "Architecture blueprint returned no files."
        return "blueprint=$($architectureResult.blueprint.name)"
    }

    Invoke-SmokeScenario -Id "builder" -Title "Builder workspace lifecycle" -Action {
        $builderNotes = New-Object System.Collections.Generic.List[string]

        Invoke-JsonRequest -Method "POST" -Uri "$ApiBaseUrl/api/workspace/open" -Headers $builderHeaders -Body @{
            projectPath = $WorkspaceRoot
        } | Out-Null

        Invoke-JsonRequest -Method "POST" -Uri "$ApiBaseUrl/api/workspace/node/create" -Headers $builderHeaders -Body @{
            projectPath = $WorkspaceRoot
            parentRelativePath = ""
            name = "Project.csproj"
            isFolder = $false
        } | Out-Null

        Invoke-JsonRequest -Method "POST" -Uri "$ApiBaseUrl/api/workspace/node/create" -Headers $builderHeaders -Body @{
            projectPath = $WorkspaceRoot
            parentRelativePath = ""
            name = "Program.cs"
            isFolder = $false
        } | Out-Null

        Invoke-JsonRequest -Method "POST" -Uri "$ApiBaseUrl/api/workspace/node/create" -Headers $builderHeaders -Body @{
            projectPath = $WorkspaceRoot
            parentRelativePath = ""
            name = "scratch.txt"
            isFolder = $false
        } | Out-Null

        Invoke-JsonRequest -Method "POST" -Uri "$ApiBaseUrl/api/fs/write" -Headers $builderHeaders -Body @{
            path = $workspaceProjectFile
            content = $workspaceProjectContent
        } | Out-Null
        Invoke-JsonRequest -Method "POST" -Uri "$ApiBaseUrl/api/fs/write" -Headers $builderHeaders -Body @{
            path = $workspaceEntryFile
            content = $workspaceProgramContent
        } | Out-Null

        $program = Invoke-JsonRequest -Method "POST" -Uri "$ApiBaseUrl/api/workspace/file/read" -Headers $builderHeaders -Body @{
            projectPath = $WorkspaceRoot
            relativePath = "Program.cs"
        }
        Assert-Condition ([string]$program.content -match "ui_workflow_smoke") "Workspace file read returned unexpected content."

        try {
            Invoke-JsonRequest -Method "POST" -Uri "$ApiBaseUrl/api/workspace/node/rename" -Headers $builderHeaders -Body @{
                projectPath = $WorkspaceRoot
                relativePath = "Program.cs"
                newName = "AppEntry.cs"
            } | Out-Null
            $builderNotes.Add("rename=ok")
        }
        catch {
            if (Test-IsPermissionLimitedFailure -ErrorRecord $_) {
                throw "Workspace rename is permission-limited for the verification runtime."
            }
            else {
                throw
            }
        }

        try {
            Invoke-JsonRequest -Method "POST" -Uri "$ApiBaseUrl/api/workspace/node/delete" -Headers $builderHeaders -Body @{
                projectPath = $WorkspaceRoot
                relativePath = "scratch.txt"
            } | Out-Null
            $builderNotes.Add("delete=ok")
        }
        catch {
            if (Test-IsPermissionLimitedFailure -ErrorRecord $_) {
                throw "Workspace delete is permission-limited for the verification runtime."
            }
            else {
                throw
            }
        }

        $workspaceSnapshot = Invoke-JsonRequest -Method "POST" -Uri "$ApiBaseUrl/api/workspace/open" -Headers $builderHeaders -Body @{
            projectPath = $WorkspaceRoot
        }
        $workspacePaths = @(Get-WorkspaceNodePaths -Folder $workspaceSnapshot.project.root)

        Assert-Condition (@($workspacePaths | Where-Object { $_ -match '(^|[\\/])AppEntry\.cs$' }).Count -gt 0) "Workspace tree did not reflect renamed AppEntry.cs."
        Assert-Condition (@($workspacePaths | Where-Object { $_ -match '(^|[\\/])Program\.cs$' }).Count -eq 0) "Workspace tree still contained Program.cs after rename."

        Assert-Condition (@($workspacePaths | Where-Object { $_ -match '(^|[\\/])scratch\.txt$' }).Count -eq 0) "Workspace tree still contained scratch.txt after delete."

        $buildResult = Invoke-JsonRequest -Method "POST" -Uri "$ApiBaseUrl/api/build" -Headers $builderHeaders -Body @{
            projectPath = $WorkspaceRoot
        }
        Assert-Condition ([bool]$buildResult.success) "Workspace build failed."
        $builderNotes.Add("build=ok")
        return "workspace=$WorkspaceRoot; $($builderNotes -join '; ')"
    }

    Invoke-SmokeScenario -Id "runtime" -Title "Runtime Console surfaces" -Action {
        $controlPlane = Invoke-JsonRequest -Method "GET" -Uri "$ApiBaseUrl/api/control-plane" -Headers $runtimeHeaders
        Assert-Condition ([bool]$controlPlane.readiness.readyForChat) "Control-plane readiness was false."
        $runtimeLogs = Invoke-JsonRequest -Method "GET" -Uri "$ApiBaseUrl/api/runtime/logs?tail=25&maxSources=2" -Headers $runtimeHeaders
        Assert-Condition ($null -ne $runtimeLogs.schemaVersion) "Runtime logs response did not include schemaVersion."
        return "logSchema=$($runtimeLogs.schemaVersion)"
    }

    Invoke-SmokeScenario -Id "capabilities" -Title "Capability catalog" -Action {
        $capabilities = Invoke-JsonRequest -Method "GET" -Uri "$ApiBaseUrl/api/capabilities/catalog" -Headers $runtimeHeaders
        $declaredCount = @($capabilities.declaredCapabilities).Count
        $modelCount = @($capabilities.models).Count
        Assert-Condition (($declaredCount + $modelCount) -gt 0) "Capability catalog returned no entries."
        return "declared=$declaredCount; models=$modelCount"
    }

    Invoke-SmokeScenario -Id "evolution" -Title "Evolution and indexing surfaces" -Action {
        $status = Invoke-JsonRequest -Method "GET" -Uri "$ApiBaseUrl/api/evolution/status" -Headers $evolutionHeaders
        Assert-Condition ($null -ne $status.currentPhase) "Evolution status did not include currentPhase."
        $library = @(Invoke-JsonRequest -Method "GET" -Uri "$ApiBaseUrl/api/evolution/library" -Headers $evolutionHeaders)
        return "phase=$($status.currentPhase); libraryItems=$($library.Count)"
    }
)

$failures = @($scenarios | Where-Object { $_.status -ne "PASS" } | ForEach-Object { "$($_.title): $($_.details)" })
$status = if ($failures.Count -eq 0) { "PASS" } else { "FAIL" }
$result = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    apiBaseUrl = $ApiBaseUrl
    uiUrl = $UiUrl
    workspaceRoot = $WorkspaceRoot
    status = $status
    scenarios = $scenarios
    failures = $failures
}

Write-WorkflowSmokeArtifacts -Result $result -JsonPath $OutputJsonPath -MarkdownPath $OutputMarkdownPath

if ($status -ne "PASS") {
    throw "[UI Smoke] One or more UI workflows failed. See $OutputMarkdownPath."
}

Write-Host "[UI Smoke] Passed. Report: $OutputMarkdownPath" -ForegroundColor Green
