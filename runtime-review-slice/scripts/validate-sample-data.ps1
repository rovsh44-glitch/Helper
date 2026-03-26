$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Set-StrictMode -Version Latest

$sampleDataRoot = Resolve-Path (Join-Path $PSScriptRoot '..\sample_data')
$windowsAbsolutePathRegex = [regex]'(?i)\b[a-z]:(?:\\|/)(?!redacted_runtime(?:\\|/))'
$secretPatternRegex = [regex]'(?i)\b(?:api[_ -]?key|bearer|token)\s*[:=]\s*[a-z0-9._-]{8,}'
$externalUrlRegex = [regex]'(?i)https?://(?!localhost\b|127\.0\.0\.1\b)[^\s""'']+'

$issues = [System.Collections.Generic.List[string]]::new()
$files = Get-ChildItem -Path $sampleDataRoot -Recurse -File

foreach ($file in $files) {
    $content = Get-Content -Path $file.FullName -Raw
    $rootPrefix = $sampleDataRoot.Path.TrimEnd('\', '/')
    $relativePath = $file.FullName.Substring($rootPrefix.Length).TrimStart('\', '/')

    if ($windowsAbsolutePathRegex.IsMatch($content)) {
        $issues.Add("$relativePath contains a non-redacted Windows path.")
    }

    if ($secretPatternRegex.IsMatch($content)) {
        $issues.Add("$relativePath contains token-like material.")
    }

    if ($externalUrlRegex.IsMatch($content)) {
        $issues.Add("$relativePath contains a non-local URL.")
    }
}

if ($issues.Count -gt 0) {
    Write-Host ''
    Write-Host 'Sample-data validation failed:'
    foreach ($issue in $issues) {
        Write-Host " - $issue"
    }

    throw 'The checked-in runtime-review-slice sample_data/ tree failed the public redaction validation gate.'
}

Write-Host ''
Write-Host "Validated $($files.Count) sample-data files."
Write-Host 'No non-redacted Windows paths, token-like material, or non-local URLs were found.'
