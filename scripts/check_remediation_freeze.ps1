$ErrorActionPreference = "Stop"

if ($env:HELPER_REMEDIATION_LOCK -ne "1") {
    Write-Host "[CI Gate] Remediation freeze gate skipped."
    exit 0
}

$allowedTopLevel = @(
    "src",
    "components",
    "hooks",
    "services",
    "utils",
    "scripts",
    "doc",
    "README.md",
    ".env.local.example",
    ".gitignore",
    "App.tsx",
    "Run_Helper.bat",
    "Run_Helper_Integrated.bat",
    "package.json",
    "package-lock.json",
    "vite.config.ts"
)

try {
    $changed = git diff --name-only HEAD | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
} catch {
    Write-Host "[CI Gate] Remediation freeze gate skipped: git diff unavailable."
    exit 0
}

$violations = @()
foreach ($path in $changed) {
    $topLevel = ($path -split '[\\/]+')[0]
    if (-not ($allowedTopLevel -contains $topLevel -or $allowedTopLevel -contains $path)) {
        $violations += $path
    }
}

if ($violations.Count -gt 0) {
    Write-Host "[CI Gate] Remediation freeze violation:" -ForegroundColor Red
    $violations | ForEach-Object { Write-Host " - $_" -ForegroundColor Red }
    exit 1
}

Write-Host "[CI Gate] Remediation freeze passed."

