param(
    [switch]$SkipApiBoundary
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$budgetsPath = Join-Path $PSScriptRoot "performance_budgets.json"
$budgets = Get-Content $budgetsPath -Raw | ConvertFrom-Json

$maxViewFileLines = [int]$budgets.frontend.maxViewFileLines
$maxBuilderViewLines = [int]$budgets.frontend.maxBuilderViewLines
$maxSettingsViewLines = [int]$budgets.frontend.maxSettingsViewLines
$maxRuntimeConsoleViewLines = [int]$budgets.frontend.maxRuntimeConsoleViewLines
$maxBuilderWorkspaceSessionLines = [int]$budgets.frontend.maxBuilderWorkspaceSessionLines
$maxBuilderSupportHookLines = [int]$budgets.frontend.maxBuilderSupportHookLines

$viewFiles = Get-ChildItem (Join-Path $repoRoot "components\views") -Filter *.tsx -File
$oversizedViews = @()

foreach ($file in $viewFiles) {
    $lineCount = (Get-Content $file.FullName | Measure-Object -Line).Lines
    if ($lineCount -gt $maxViewFileLines) {
        $oversizedViews += "$($file.Name)=$lineCount"
    }
}

if ($oversizedViews.Count -gt 0) {
    throw "Frontend architecture gate failed: view files exceeded $maxViewFileLines lines: $($oversizedViews -join ', ')."
}

$runtimeConsolePath = Join-Path $repoRoot "components\views\RuntimeConsoleView.tsx"
$runtimeConsoleLines = (Get-Content $runtimeConsolePath | Measure-Object -Line).Lines
if ($runtimeConsoleLines -gt $maxRuntimeConsoleViewLines) {
    throw "Frontend architecture gate failed: RuntimeConsoleView.tsx has $runtimeConsoleLines lines > $maxRuntimeConsoleViewLines."
}

$builderViewPath = Join-Path $repoRoot "components\views\BuilderView.tsx"
$builderViewLines = (Get-Content $builderViewPath | Measure-Object -Line).Lines
if ($builderViewLines -gt $maxBuilderViewLines) {
    throw "Frontend architecture gate failed: BuilderView.tsx has $builderViewLines lines > $maxBuilderViewLines."
}

$builderWorkspaceSessionPath = Join-Path $repoRoot "hooks\useBuilderWorkspaceSession.ts"
$builderWorkspaceSessionLines = (Get-Content $builderWorkspaceSessionPath | Measure-Object -Line).Lines
if ($builderWorkspaceSessionLines -gt $maxBuilderWorkspaceSessionLines) {
    throw "Frontend architecture gate failed: useBuilderWorkspaceSession.ts has $builderWorkspaceSessionLines lines > $maxBuilderWorkspaceSessionLines."
}

$builderSupportHooks = Get-ChildItem (Join-Path $repoRoot "hooks") -Filter useBuilder*.ts -File |
    Where-Object { $_.Name -ne 'useBuilderWorkspaceSession.ts' }
$oversizedBuilderHooks = @()
foreach ($file in $builderSupportHooks) {
    $lineCount = (Get-Content $file.FullName | Measure-Object -Line).Lines
    if ($lineCount -gt $maxBuilderSupportHookLines) {
        $oversizedBuilderHooks += "$($file.Name)=$lineCount"
    }
}

if ($oversizedBuilderHooks.Count -gt 0) {
    throw "Frontend architecture gate failed: builder support hooks exceeded $maxBuilderSupportHookLines lines: $($oversizedBuilderHooks -join ', ')."
}

$settingsViewPath = Join-Path $repoRoot "components\views\SettingsView.tsx"
$settingsViewLines = (Get-Content $settingsViewPath | Measure-Object -Line).Lines
if ($settingsViewLines -gt $maxSettingsViewLines) {
    throw "Frontend architecture gate failed: SettingsView.tsx has $settingsViewLines lines > $maxSettingsViewLines."
}

$builderViewText = Get-Content $builderViewPath -Raw
if ($builderViewText -notmatch 'useBuilderWorkspaceSession') {
    throw "Frontend architecture gate failed: BuilderView.tsx must stay wired through useBuilderWorkspaceSession."
}

if ($builderViewText -match 'services/' -or $builderViewText -match 'fetch\(' -or $builderViewText -match 'localStorage') {
    throw "Frontend architecture gate failed: BuilderView.tsx must stay a shell without direct service/API or storage calls."
}

$settingsViewText = Get-Content $settingsViewPath -Raw
if ($settingsViewText -notmatch 'useSettingsViewState') {
    throw "Frontend architecture gate failed: SettingsView.tsx must stay wired through useSettingsViewState."
}

if ($settingsViewText -match 'services/' -or
    $settingsViewText -match 'conversationApi' -or
    $settingsViewText -match 'settingsPreferenceStorage' -or
    $settingsViewText -match 'localStorage' -or
    $settingsViewText -match 'fetch\(') {
    throw "Frontend architecture gate failed: SettingsView.tsx must stay a shell without direct API, storage, or runtime wiring."
}

node (Join-Path $PSScriptRoot "check_frontend_reachability.mjs")
if ($LASTEXITCODE -ne 0) {
    throw "Frontend architecture gate failed: reachability validation returned exit code $LASTEXITCODE."
}

if (-not $SkipApiBoundary) {
    & (Join-Path $PSScriptRoot "check_ui_api_usage.ps1")
}

Write-Host "[FrontendGate] Architecture checks passed."
