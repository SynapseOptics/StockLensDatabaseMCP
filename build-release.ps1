# Build all StockLensDatabaseMCP release artifacts in one shot.
#
# Produces (in installer\Output\ and release\):
#   installer\Output\StockLensDatabaseMCP-Setup-{Version}.exe
#       Windows installer (Inno Setup; bundles MCP + configurator + catalog)
#   release\stocklens-mcp-unix-{Version}.tar.gz
#       Cross-platform framework-dependent build for Mac + Linux.
#       Contains LensHH.StockMcp + transitive deps + catalogs/ + a
#       short README. User needs .NET 8 runtime on their machine.
#   release\catalogs.zip
#       Standalone catalog (SQLite + Lenses/*.lhlt + Glass/*.AGF). Unversioned filename
#       so scripts/fetch-catalog.{sh,ps1} can always hit
#       /releases/latest/download/catalogs.zip on GitHub.
#
# Catalog source is the sibling SynapseLensHH-LT working tree by default;
# override with -CatalogSource or the STOCK_CATALOGS_DIR env var.

[CmdletBinding()]
param(
    [string]$Version = "1.0.4",
    [string]$CatalogSource = "",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$repo = $PSScriptRoot

# Resolve catalog source: explicit param > env var > sibling LT tree
if (-not $CatalogSource) {
    if ($env:STOCK_CATALOGS_DIR) {
        $CatalogSource = $env:STOCK_CATALOGS_DIR
    } else {
        $CatalogSource = Join-Path $repo "..\SynapseLensHH-LT\LensHH-LT\catalogs"
    }
}
$CatalogSource = [System.IO.Path]::GetFullPath($CatalogSource)

$db = Join-Path $CatalogSource "stock-lens-catalog.sqlite"
$lensesDir = Join-Path $CatalogSource "Lenses"
$glassDir = Join-Path $CatalogSource "Glass"
if (-not (Test-Path $db)) {
    Write-Error "Catalog source missing stock-lens-catalog.sqlite at: $CatalogSource"
}
if (-not (Test-Path $lensesDir)) {
    Write-Error "Catalog source missing Lenses\ at: $CatalogSource"
}
if (-not (Test-Path (Join-Path $glassDir "*.AGF"))) {
    Write-Error "Catalog source missing Glass\*.AGF at: $CatalogSource"
}

Write-Host "=== StockLensDatabaseMCP release $Version ===" -ForegroundColor Cyan
Write-Host "Catalog source: $CatalogSource"

# ── 1. Publish the MCP (framework-dependent, cross-platform) ──────
$staging = Join-Path $repo "release\staging\stocklens-mcp-$Version"
$releaseDir = Join-Path $repo "release"
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
New-Item -ItemType Directory -Force -Path $staging | Out-Null
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

Write-Host "`n=== Publishing MCP (framework-dependent) ===" -ForegroundColor Cyan
dotnet publish (Join-Path $repo "src\LensHH.StockMcp\LensHH.StockMcp.csproj") `
    -c Release `
    -o $staging `
    --nologo `
    | Out-Host
if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish failed" }

# ── 2. Bundle catalog into staging ────────────────────────────────
$stagingCatalogs = Join-Path $staging "catalogs"
New-Item -ItemType Directory -Force -Path (Join-Path $stagingCatalogs "Lenses") | Out-Null

Write-Host "`n=== Copying catalog into staging ===" -ForegroundColor Cyan
Copy-Item $db (Join-Path $stagingCatalogs "stock-lens-catalog.sqlite")
# Only .lhlt files — Lenses/ also contains .zar / .zmx vendor source
# files (~480 MB) that the runtime doesn't need. /S = recursive, skip
# empty dirs (excludes branches that hold only .zar archives).
$null = robocopy $lensesDir (Join-Path $stagingCatalogs "Lenses") *.lhlt /S /NFL /NDL /NJH /NJS /NP
# robocopy's exit codes 0-7 are success (8+ = failure); explicitly accept
if ($LASTEXITCODE -ge 8) { Write-Error "robocopy of Lenses/ failed (exit $LASTEXITCODE)" }
$LASTEXITCODE = 0
# The glass catalogs: the exporters give each glass as its catalog has it (Code V private
# glass, Optiland refractiveindex.info data), and name it with its catalog.
New-Item -ItemType Directory -Force -Path (Join-Path $stagingCatalogs "Glass") | Out-Null
Copy-Item (Join-Path $glassDir "*.AGF") (Join-Path $stagingCatalogs "Glass")

# Drop a tiny README in the tarball
$tarReadmePath = Join-Path $staging "README-UNIX.md"
@"
# StockLensDatabaseMCP — Mac / Linux quick-start

This archive is a framework-dependent build of the LensHH-LT stock-lens
MCP server, bundled with the catalog (~7,600 lenses). Requires the
**.NET 8 runtime** on the host machine.

## Install .NET 8 runtime (one-time)

- macOS:   ``brew install --cask dotnet``  (or download from https://dot.net)
- Debian:  ``sudo apt install -y dotnet-runtime-8.0``
- Fedora:  ``sudo dnf install dotnet-runtime-8.0``

## Run

``````
tar xzf stocklens-mcp-unix-$Version.tar.gz
cd stocklens-mcp-$Version
dotnet ./LensHH.StockMcp.dll          # the server reads stdio; press Ctrl-C to stop
``````

The catalog auto-resolves at ``./catalogs/stock-lens-catalog.sqlite``.
Override with the ``LENSHH_CATALOGS_DIR`` environment variable if you
move things around.

## Register with Claude

### Claude Desktop

Edit ``~/Library/Application Support/Claude/claude_desktop_config.json``
(macOS) or ``~/.config/Claude/claude_desktop_config.json`` (Linux):

``````json
{
  "mcpServers": {
    "lenshh-stock": {
      "command": "dotnet",
      "args": ["/absolute/path/to/stocklens-mcp-$Version/LensHH.StockMcp.dll"],
      "env": {
        "LENSHH_CATALOGS_DIR": "/absolute/path/to/stocklens-mcp-$Version/catalogs"
      }
    }
  }
}
``````

### Claude Code

``````
claude mcp add --transport stdio --scope user \
    lenshh-stock \
    --env LENSHH_CATALOGS_DIR=/absolute/path/to/stocklens-mcp-$Version/catalogs \
    -- dotnet /absolute/path/to/stocklens-mcp-$Version/LensHH.StockMcp.dll
``````

Version: $Version
"@ | Set-Content -Path $tarReadmePath -Encoding UTF8

# Also copy LICENSE for completeness
Copy-Item (Join-Path $repo "LICENSE") (Join-Path $staging "LICENSE")

# ── 3. Tarball the staging dir ────────────────────────────────────
$tarName = "stocklens-mcp-unix-$Version.tar.gz"
$tarPath = Join-Path $releaseDir $tarName
if (Test-Path $tarPath) { Remove-Item -Force $tarPath }

Write-Host "`n=== Building tarball ===" -ForegroundColor Cyan
# Windows 10+ ships bsdtar as tar.exe. Use -C to set the parent so the
# archive contains "stocklens-mcp-$Version/..." as the top-level entry
# (rather than the absolute path on the build machine).
$parent = Split-Path $staging -Parent
$leaf   = Split-Path $staging -Leaf
& tar -czf $tarPath -C $parent $leaf
if ($LASTEXITCODE -ne 0) { Write-Error "tar failed" }
$tarSize = (Get-Item $tarPath).Length

# ── 4. Standalone catalog zip (unversioned filename) ──────────────
# Unversioned so scripts/fetch-catalog can hit
# /releases/latest/download/catalogs.zip without knowing the tag.
$catZip = Join-Path $releaseDir "catalogs.zip"
if (Test-Path $catZip) { Remove-Item -Force $catZip }

Write-Host "`n=== Building catalogs.zip ===" -ForegroundColor Cyan
# Compress the catalog files at the staging level so the archive
# layout is "catalogs/stock-lens-catalog.sqlite" + "catalogs/Lenses/...".
Compress-Archive -Path $stagingCatalogs -DestinationPath $catZip -CompressionLevel Optimal
$catZipSize = (Get-Item $catZip).Length

# ── 5. Windows installer (optional skip for dev iterations) ──────
if (-not $SkipInstaller) {
    Write-Host "`n=== Building Windows installer ===" -ForegroundColor Cyan
    $env:STOCK_CATALOGS_DIR = $CatalogSource
    & (Join-Path $repo "installer\build-installer.bat")
    if ($LASTEXITCODE -ne 0) { Write-Error "Windows installer build failed" }
    $exe = Join-Path $repo "installer\Output\StockLensDatabaseMCP-Setup-$Version.exe"
    $exeSize = (Get-Item $exe).Length
}

# ── 6. Summary ────────────────────────────────────────────────────
Write-Host "`n=== Release artifacts ===" -ForegroundColor Green
"  {0,-50} {1,8:N1} MB" -f $tarName, ($tarSize / 1MB) | Write-Host
"  {0,-50} {1,8:N1} MB" -f "catalogs.zip", ($catZipSize / 1MB) | Write-Host
if (-not $SkipInstaller) {
    "  {0,-50} {1,8:N1} MB" -f "StockLensDatabaseMCP-Setup-$Version.exe", ($exeSize / 1MB) | Write-Host
}
Write-Host "`nUpload all three to the GitHub Release for v$Version."
