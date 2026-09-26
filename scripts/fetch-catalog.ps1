# Download and extract the StockLensDatabaseMCP stock-lens catalog from
# GitHub Releases. Defaults to the latest release; pass -Version "v1.0.0"
# to pin to a specific tag.
#
# Usage:
#   .\fetch-catalog.ps1                                  # latest, into .\catalogs\
#   .\fetch-catalog.ps1 -Version v1.0.0                  # specific tag, into .\catalogs\
#   .\fetch-catalog.ps1 -DestParent C:\Tools\lenshh      # latest, into <DestParent>\catalogs\

[CmdletBinding()]
param(
    [string]$Version = "latest",
    [string]$DestParent = "."
)

$ErrorActionPreference = "Stop"
$repo = "SynapseOptics/StockLensDatabaseMCP"

if ($Version -eq "latest") {
    $url = "https://github.com/$repo/releases/latest/download/catalogs.zip"
} else {
    $url = "https://github.com/$repo/releases/download/$Version/catalogs.zip"
}

if (-not (Test-Path -LiteralPath $DestParent)) {
    $DestParent = (New-Item -ItemType Directory -Force -Path $DestParent).FullName
} else {
    $DestParent = (Resolve-Path -LiteralPath $DestParent).Path
}
$dest = Join-Path $DestParent "catalogs"

$tmp = [System.IO.Path]::GetTempFileName() + ".zip"
try {
    Write-Host "Fetching $url"
    Invoke-WebRequest -Uri $url -OutFile $tmp -UseBasicParsing

    if (Test-Path $dest) {
        Write-Warning "$dest already exists — overwriting."
    }

    # The archive's top-level entry is "catalogs/", so extracting into
    # DestParent lands the catalog at $dest exactly.
    Expand-Archive -LiteralPath $tmp -DestinationPath $DestParent -Force

    $db = Join-Path $dest "stock-lens-catalog.sqlite"
    if (-not (Test-Path $db)) {
        throw "Extraction failed: $db not found."
    }

    $size = "{0:N1} MB" -f ((Get-Item $db).Length / 1MB)
    $count = (Get-ChildItem -Path (Join-Path $dest "Lenses") -Filter *.lhlt -Recurse -ErrorAction SilentlyContinue).Count
    $glassCount = (Get-ChildItem -Path (Join-Path $dest "Glass") -Filter *.AGF -ErrorAction SilentlyContinue).Count

    Write-Host ""
    Write-Host "Catalog installed to: $dest"
    Write-Host "  stock-lens-catalog.sqlite  $size"
    Write-Host "  Lenses/*.lhlt              $count files"
    Write-Host "  Glass/*.AGF                $glassCount catalogs"
    Write-Host ""
    Write-Host "Point your MCP at it via:  `$env:LENSHH_CATALOGS_DIR = '$dest'"
}
finally {
    if (Test-Path $tmp) { Remove-Item -Force $tmp }
}
