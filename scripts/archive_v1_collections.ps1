[CmdletBinding()]
param(
    [string]$QdrantBaseUrl = "http://127.0.0.1:6333",
    [string]$OutputPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
    $OutputPath = Join-Path $PSScriptRoot ("..\doc\chunking_v1_archive_manifest_{0}.md" -f $stamp)
}

$response = Invoke-RestMethod -Method Get -Uri ($QdrantBaseUrl.TrimEnd("/") + "/collections") -TimeoutSec 30
$collections = @($response.result.collections | ForEach-Object { [string]$_.name })
$v1Collections = @($collections | Where-Object { $_ -like "knowledge_*" -and $_ -notlike "*_v2" } | Sort-Object)

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# V1 Collection Archive Manifest")
$lines.Add("")
$lines.Add(('- GeneratedAt: `{0}`' -f (Get-Date).ToString("s")))
$lines.Add(('- QdrantBaseUrl: `{0}`' -f $QdrantBaseUrl))
$lines.Add(('- CollectionCount: `{0}`' -f $v1Collections.Count))
$lines.Add(('- ActiveRetrievalPath: `inactive after cutover; retained for explicit rollback only`'))
$lines.Add("")
$lines.Add("| Collection | Status |")
$lines.Add("| --- | --- |")
foreach ($collection in $v1Collections) {
    $lines.Add(("| {0} | archived_manifest_only |" -f $collection))
}

Set-Content -Path $OutputPath -Value ($lines -join "`r`n") -Encoding UTF8
[PSCustomObject]@{
    OutputPath = $OutputPath
    CollectionCount = $v1Collections.Count
}
