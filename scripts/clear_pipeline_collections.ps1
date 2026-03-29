[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$QdrantBaseUrl = "http://127.0.0.1:6333",
    [ValidateSet("v1", "v2")]
    [string]$PipelineVersion = "v2",
    [string]$ManifestPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $stamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
    $ManifestPath = Join-Path $PSScriptRoot ("..\\doc\\chunking_{0}_collection_clear_manifest_{1}.md" -f $PipelineVersion, $stamp)
}

$response = Invoke-RestMethod -Method Get -Uri ($QdrantBaseUrl.TrimEnd("/") + "/collections") -TimeoutSec 30
$collections = @($response.result.collections | ForEach-Object { [string]$_.name })
$targetCollections = if ($PipelineVersion -eq "v2") {
    @($collections | Where-Object { $_ -like "knowledge_*_v2" } | Sort-Object)
}
else {
    @($collections | Where-Object { $_ -like "knowledge_*" -and $_ -notlike "*_v2" } | Sort-Object)
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add("# Pipeline Collection Clear Manifest")
$lines.Add("")
$lines.Add(('- GeneratedAt: `{0}`' -f (Get-Date).ToString("s")))
$lines.Add(('- QdrantBaseUrl: `{0}`' -f $QdrantBaseUrl))
$lines.Add(('- PipelineVersion: `{0}`' -f $PipelineVersion))
$lines.Add(('- TargetCollectionCount: `{0}`' -f $targetCollections.Count))
$lines.Add("")
$lines.Add("| Collection | Action |")
$lines.Add("| --- | --- |")

foreach ($collection in $targetCollections) {
    if ($PSCmdlet.ShouldProcess($collection, "Delete Qdrant collection")) {
        Invoke-RestMethod -Method Delete -Uri ($QdrantBaseUrl.TrimEnd("/") + "/collections/" + [uri]::EscapeDataString($collection)) -TimeoutSec 120 | Out-Null
        $lines.Add(("| {0} | deleted |" -f $collection))
    }
    else {
        $lines.Add(("| {0} | skipped |" -f $collection))
    }
}

Set-Content -Path $ManifestPath -Value ($lines -join "`r`n") -Encoding UTF8
[PSCustomObject]@{
    ManifestPath = $ManifestPath
    DeletedCollections = $targetCollections.Count
    PipelineVersion = $PipelineVersion
}
