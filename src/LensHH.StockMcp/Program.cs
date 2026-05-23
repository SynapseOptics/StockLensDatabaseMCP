using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

// Standalone MCP server for the LensHH-LT stock-lens catalog.
//
// Deliberately minimal: no engine reference (LensHH.Core.dll), no
// optical-design runtime, no session state. Catalog access is
// read-only SQLite via Microsoft.Data.Sqlite.
//
// Run from the command line: `LensHH.StockMcp` (the host attaches
// to stdio for MCP clients).
//
// Catalog discovery: tools call StockCatalog.ResolveDbPath() which
// probes the LENSHH_CATALOGS_DIR environment variable first, then
// walks from AppContext.BaseDirectory. See StockCatalog.cs for the
// exact probe order.

var builder = Host.CreateApplicationBuilder(args);

// MCP server logs go to stderr (stdout is reserved for the protocol).
builder.Logging.AddConsole(o =>
{
    o.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "LensHH-Stock",
            Version = "1.0.0"
        };
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
