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
| `export_lhlt` | Byte-copy a stock lens's native `.lhlt` prescription to a user-specified path. |
| `list_vendors` | Distinct vendors in the catalog with part counts. |
| `list_glasses` | Distinct glass names with usage counts (via SQLite `json_each` on `glass_names_json`). |

## Catalog location

At startup the server probes for `stock-lens-catalog.sqlite` in this order:

1. `$env:LENSHH_CATALOGS_DIR` (override via environment variable)
2. `{exeDir}\..\catalogs\stock-lens-catalog.sqlite` (production install layout, MCP exe in a subfolder)
3. `{exeDir}\catalogs\stock-lens-catalog.sqlite` (flat layout)
4. `{exeDir}\..\..\..\..\..\catalogs\stock-lens-catalog.sqlite` (dev-tree layout from `bin\Debug\net8.0\`)

The `.lhlt` files are expected at `{catalogsDir}\Lenses\<vendor>\...\<part>.lhlt` (the relative path is stored in the SQLite `lhlt_relpath` column).

The catalog database itself is **not** included in this repo. It ships
with the [LensHH-LT installer](https://github.com/SynapseOptics/LensHH-LT/releases)
under `{LT-install}\catalogs\`. Point `LENSHH_CATALOGS_DIR` at that
directory, or copy the `catalogs/` folder to a location next to the
StockMcp exe.

## Building

Requires .NET 8 SDK.

```pwsh
dotnet build StockLensDatabaseMCP.sln -c Release
```

Outputs:

- `src/LensHH.StockMcp/bin/Release/net8.0/LensHH.StockMcp.exe`
- `src/ConfigureLensHHStockMcp/bin/Release/net8.0-windows/ConfigureLensHHStockMcp.exe`

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
  --env LENSHH_CATALOGS_DIR="C:\Path\To\catalogs" `
  lenshh-stock -- "C:\Path\To\LensHH.StockMcp.exe"
```

## Roadmap

This is the P1 release: query + `.lhlt` export. Planned:

- **P2** — export to ZEMAX (`.zmx`), Code V (`.seq`), OSLO (`.len`),
  Optalix (`.otx`), and Optiland (`.json`) via a pure-DTO writer
  layer that doesn't pull in the LensHH-LT engine.
- **P3** — element-replacement workflow: read a user-provided
  prescription in any of the six formats, replace a designated
  element with a stock part, write the result in the same (or a
  different) format.

## License

See [LICENSE](LICENSE).
