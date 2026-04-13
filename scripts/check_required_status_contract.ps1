param(
    [string]$ContractPath = ".github/branch-protection.required-status-checks.json"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$resolvedContractPath = if ([System.IO.Path]::IsPathRooted($ContractPath)) {
    $ContractPath
}
else {
    Join-Path $repoRoot $ContractPath
}

if (-not (Test-Path -LiteralPath $resolvedContractPath)) {
    throw "[RequiredStatusContract] Contract file not found: $ContractPath"
}

$contract = Get-Content -Path $resolvedContractPath -Raw | ConvertFrom-Json
if ([string]::IsNullOrWhiteSpace([string]$contract.protectedBranch)) {
    throw "[RequiredStatusContract] protectedBranch is required."
}

$requiredChecks = @($contract.requiredStatusChecks)
if ($requiredChecks.Count -eq 0) {
    throw "[RequiredStatusContract] requiredStatusChecks must contain at least one context."
}

$contexts = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::OrdinalIgnoreCase)
foreach ($check in $requiredChecks) {
    $context = [string]$check.context
    $workflow = [string]$check.workflow
    $job = [string]$check.job

    if ([string]::IsNullOrWhiteSpace($context) -or [string]::IsNullOrWhiteSpace($workflow) -or [string]::IsNullOrWhiteSpace($job)) {
        throw "[RequiredStatusContract] Each required status check must define context, workflow, and job."
    }

    if (-not $contexts.Add($context)) {
        throw "[RequiredStatusContract] Duplicate required status context: $context"
    }

    $resolvedWorkflowPath = Join-Path $repoRoot $workflow
    if (-not (Test-Path -LiteralPath $resolvedWorkflowPath)) {
        throw "[RequiredStatusContract] Workflow file not found for context '$context': $workflow"
    }

    $workflowText = Get-Content -Path $resolvedWorkflowPath -Raw
    if ($workflowText -notmatch ("(?m)^\s{{2,}}{0}:\s*$" -f [Regex]::Escape($job))) {
        throw "[RequiredStatusContract] Workflow '$workflow' does not declare job '$job' for context '$context'."
    }
}

Write-Host ("[RequiredStatusContract] Passed. Branch '{0}' requires contexts: {1}." -f $contract.protectedBranch, ($contexts -join ", ")) -ForegroundColor Green
