# StockLensDatabaseMCP

Standalone Model Context Protocol (MCP) server for the **LensHH-LT
stock-lens catalog** — a curated database of ~7,600 catalog lenses
from Edmund Optics, Thorlabs, and Ross Optical, with first-order
properties, full surface prescriptions, and .lhlt-format
serialisations.

The server is **engine-free**: it depends only on
`Microsoft.Data.Sqlite` and `ModelContextProtocol`, with no
reference to the LensHH-LT optical-design runtime. It can be
deployed and used independently of LensHH-LT for catalog browsing
and prescription export.

## Components

| Project | Purpose |
|---|---|
| `src/LensHH.StockMcp/` | The MCP server. Stdio transport. 5 tools (see below). |
| `src/ConfigureLensHHStockMcp/` | Windows GUI utility to register the server with Claude Desktop and Claude Code. WPF, net8.0-windows. |

## MCP tools

| Tool | What it does |
|---|---|
| `search_stock` | Filter the catalog by EFL / diameter / F-number / element-count / vendor / family / glass. Optional ranges. Returns one line per result. |
| `get_lens_details` | Dump the full `stock_lenses` row + every `lens_surfaces` row for a given part number. |
| `export_lens` | Export a stock lens prescription in any supported format (see below). Picks the writer based on the `format` argument. |
| `list_vendors` | Distinct vendors in the catalog with part counts. |
| `list_glasses` | Distinct glass names with usage counts (via SQLite `json_each` on `glass_names_json`). |

### `export_lens` formats

| `format` | Output | Aliases | Notes |
|---|---|---|---|
| `lhlt` | `.lhlt` | — | Native LensHH-LT JSON. Byte-copied from the catalog when `reversed=false`; serialized from DTOs when `reversed=true`. |
| `optiland` | `.json` | `json` | Optiland JSON. Each glass is named strictly, with a `lenshh-<catalog>` catalog, and its dispersion data is written as refractiveindex.info `.yml` files to a `<lens>_glass` folder beside the `.json` and installed into `~/.optiland/catalogs`, so Optiland uses exactly the glass the lens was designed with. |
| `zemax` | `.zmx` | `zmx` | ZEMAX sequential-mode text, UTF-16 LE with BOM. |
| `oslo` | `.len` | `len` | OSLO lens file: primary wavelength first; EBR/ANG, or NAO/OBH for a finite object; only apertures that clip are checked. |
| `codev` | `.seq` | `seq` | Code V sequence file. A glass from a catalog Code V ships is written `NAME_CATALOG` (e.g. `NBK7_SCHOTT`); any other (LightPath, plastics, crystals) as a private glass (`PRV`) with its index at each of the lens's wavelengths. |
| `optalix` | `.otx` | `otx` | Optalix prescription. |

OSLO, Code V and Optalix have no r² aspheric term, so a lens with one (72 surfaces in the catalog) is refused in those formats rather than written without it; Optiland and `.lhlt` carry it. A lens using a glass the bundled catalogs lack is still exported, with a note saying which glass.

All non-lhlt formats are engine-free: each reads the bundled `.lhlt` prescription via standalone DTOs and writes the target format directly, following the same rules as LensHH-LT's exporters. Glasses are resolved by name against the bundled AGF glass catalogs (`catalogs/Glass`).

### Exporting a reversed lens

Pass `reversed=true` to `export_lens` to write the prescription flipped front-to-back. The refractive surface order is reversed, each radius negates, and thickness/material associations shift so the physical lens (and its optical power) are preserved — only orientation changes. OBJ, IMG, and any front-of-system stop plane stay put. Useful for hand-composing Plössl-style systems: pull the same stock doublet twice, once normal and once reversed, then assemble in your design tool.

## Catalog location

The catalog (`stock-lens-catalog.sqlite`, ~7,600 per-lens `.lhlt` prescription files under `Lenses/`, and the AGF glass catalogs under `Glass/`) **ships bundled with the installer** — installed to `{app}\catalogs\` automatically. You do not need a separate LensHH-LT install or download.

At startup the MCP server probes for `stock-lens-catalog.sqlite` in this order:

1. `$env:LENSHH_CATALOGS_DIR` (override — point at a different catalog if needed)
2. `{exeDir}\..\catalogs\stock-lens-catalog.sqlite` (production install layout, the default)
3. `{exeDir}\catalogs\stock-lens-catalog.sqlite` (flat layout, useful for portable copies)
4. `{exeDir}\..\..\..\..\..\catalogs\stock-lens-catalog.sqlite` (dev-tree layout from `bin\Debug\net8.0\`)

The `.lhlt` files are resolved against `{catalogsDir}\Lenses\<vendor>\...\<part>.lhlt` using the relative path stored in the SQLite `lhlt_relpath` column, and glasses against `{catalogsDir}\Glass\*.AGF`.

## Installing on Windows

Download `StockLensDatabaseMCP-Setup-{version}.exe` from the [Releases](https://github.com/SynapseOptics/StockLensDatabaseMCP/releases) page and run it. The catalog ships bundled — no separate download. Launches the configurator post-install to register with Claude Desktop and Claude Code.

## Installing on macOS / Linux

The MCP server is .NET 8 console code and runs unchanged on any OS that has the .NET 8 runtime. The GUI configurator (WPF) is Windows-only — non-Windows users edit the Claude config directly.

**Option A — pre-built tarball** (no .NET SDK needed, just the runtime):

```bash
# 1. install .NET 8 runtime (one-time)
brew install --cask dotnet                       # macOS
sudo apt install -y dotnet-runtime-8.0           # Debian/Ubuntu
sudo dnf install dotnet-runtime-8.0              # Fedora

# 2. download the unix tarball (bundles MCP + catalog)
curl -fLO https://github.com/SynapseOptics/StockLensDatabaseMCP/releases/latest/download/stocklens-mcp-unix-1.0.4.tar.gz
tar xzf stocklens-mcp-unix-1.0.4.tar.gz
cd stocklens-mcp-1.0.4

# 3. register with Claude — see README-UNIX.md inside the tarball for
#    Claude Desktop config and the `claude mcp add` command for Claude Code.
```

**Option B — build from source** (needs the .NET 8 SDK):

```bash
git clone https://github.com/SynapseOptics/StockLensDatabaseMCP
cd StockLensDatabaseMCP
dotnet build src/LensHH.StockMcp/LensHH.StockMcp.csproj -c Release

# Pull the catalog separately (33 MB, not in the repo):
scripts/fetch-catalog.sh                         # latest release
# Point the MCP at it:
export LENSHH_CATALOGS_DIR="$PWD/catalogs"
```

## Building (from source, all platforms)

Requires .NET 8 SDK.

```pwsh
dotnet build StockLensDatabaseMCP.sln -c Release
```

Outputs:

- `src/LensHH.StockMcp/bin/Release/net8.0/LensHH.StockMcp.exe` (or `.dll` on non-Windows; run via `dotnet …/LensHH.StockMcp.dll`)
- `src/ConfigureLensHHStockMcp/bin/Release/net8.0-windows/ConfigureLensHHStockMcp.exe` (Windows only)

To produce the full release artifact set (Windows installer + Unix tarball + catalog zip):

```pwsh
.\build-release.ps1 -Version 1.0.4
```

Outputs land in `installer/Output/` and `release/`. See the script's header for what each artifact is.

## Registering with Claude

Run `ConfigureLensHHStockMcp.exe`. The GUI auto-detects both the MCP
exe and the catalogs directory, then offers one-click buttons to add
or remove the `lenshh-stock` entry from Claude Desktop's config and
Claude Code's user-scope MCP registry.

Manual registration alternatives:

**Claude Desktop** — edit `%APPDATA%\Claude\claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "lenshh-stock": {
      "command": "C:\\Path\\To\\LensHH.StockMcp.exe",
      "args": [],
      "env": {
        "LENSHH_CATALOGS_DIR": "C:\\Path\\To\\catalogs"
      }
    }
  }
}
```

**Claude Code** — from a terminal:

```pwsh
claude mcp add --transport stdio --scope user `
  lenshh-stock `
  --env LENSHH_CATALOGS_DIR="C:\Path\To\catalogs" `
  -- "C:\Path\To\LensHH.StockMcp.exe"
```

## Roadmap

- **P1** — query + `.lhlt` export. **Done.**
- **P2** — export to ZEMAX (`.zmx`), Code V (`.seq`), OSLO (`.len`),
  Optalix (`.otx`), and Optiland (`.json`) via a pure-DTO writer
  layer that doesn't pull in the LensHH-LT engine. **Done** — every
  catalog lens is exported in each format by the test suite, and a
  sample read back with first-order properties unchanged.

## License

See [LICENSE](LICENSE).
