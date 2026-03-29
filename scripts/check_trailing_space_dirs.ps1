$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$pending = [System.Collections.Generic.Queue[string]]::new()
$pending.Enqueue($root)
$violations = New-Object System.Collections.Generic.List[string]

while ($pending.Count -gt 0) {
    $current = $pending.Dequeue()
    $children = @(Get-ChildItem -LiteralPath $current -Force -Directory -ErrorAction SilentlyContinue)

    foreach ($child in $children) {
        if ($child.Name -ne $child.Name.Trim()) {
            $violations.Add($child.FullName)
        }

        $pending.Enqueue($child.FullName)
    }
}

if ($violations.Count -gt 0) {
    Write-Host "[CI Gate] Directories with trailing/leading spaces detected:" -ForegroundColor Red
    $violations | Sort-Object -Unique | ForEach-Object { Write-Host " - $_" -ForegroundColor Red }
    exit 1
}

Write-Host "[CI Gate] No directories with trailing spaces detected."
