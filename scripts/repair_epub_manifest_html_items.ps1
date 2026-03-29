[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)]
    [string[]]$EpubPaths,
    [switch]$CreateBackup
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-OpfPath {
    param(
        [Parameter(Mandatory = $true)][System.IO.Compression.ZipArchive]$Zip
    )

    $containerEntry = $Zip.GetEntry("META-INF/container.xml")
    if ($null -eq $containerEntry) {
        throw "META-INF/container.xml is missing."
    }

    $reader = [System.IO.StreamReader]::new($containerEntry.Open())
    try {
        $containerXml = [xml]$reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }

    $rootfile = $containerXml.container.rootfiles.rootfile
    if ($null -eq $rootfile -or [string]::IsNullOrWhiteSpace([string]$rootfile.'full-path')) {
        throw "OPF rootfile path is missing in container.xml."
    }

    return [string]$rootfile.'full-path'
}

function Repair-Epub {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [bool]$BackupRequested
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "EPUB file not found: $Path"
    }

    if ($BackupRequested) {
        $backupPath = $Path + ".bak"
        Copy-Item -LiteralPath $Path -Destination $backupPath -Force
    }

    $archive = [System.IO.Compression.ZipFile]::Open($Path, [System.IO.Compression.ZipArchiveMode]::Update)
    try {
        $opfPath = Get-OpfPath -Zip $archive
        $opfEntry = $archive.GetEntry($opfPath)
        if ($null -eq $opfEntry) {
            throw "OPF entry is missing: $opfPath"
        }

        $reader = [System.IO.StreamReader]::new($opfEntry.Open())
        try {
            $opfText = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        $xml = [xml]$opfText
        $ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
        $ns.AddNamespace("opf", "http://www.idpf.org/2007/opf")

        $manifestItems = @($xml.SelectNodes("//opf:manifest/opf:item", $ns))
        $updated = 0
        foreach ($item in $manifestItems) {
            $href = [string]$item.GetAttribute("href")
            $mediaType = [string]$item.GetAttribute("media-type")
            if ($href.EndsWith(".html", [System.StringComparison]::OrdinalIgnoreCase) -and
                $mediaType -eq "text/html") {
                $item.SetAttribute("media-type", "application/xhtml+xml")
                $updated++
            }
        }

        if ($updated -gt 0) {
            $opfEntry.Delete()
            $newOpfEntry = $archive.CreateEntry($opfPath, [System.IO.Compression.CompressionLevel]::Optimal)
            $writer = [System.IO.StreamWriter]::new($newOpfEntry.Open(), [System.Text.UTF8Encoding]::new($false))
            try {
                $xml.Save($writer)
            }
            finally {
                $writer.Dispose()
            }
        }

        return [pscustomobject]@{
            File = $Path
            OpfPath = $opfPath
            UpdatedManifestItems = $updated
            BackupCreated = $BackupRequested
        }
    }
    finally {
        $archive.Dispose()
    }
}

$results = foreach ($epubPath in $EpubPaths) {
    Repair-Epub -Path $epubPath -BackupRequested:$CreateBackup.IsPresent
}

$results | ConvertTo-Json -Depth 5
