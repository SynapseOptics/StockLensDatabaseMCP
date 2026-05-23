using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace LensHH.StockMcp
{
    /// <summary>
    /// Stock-lens catalog access — SQLite path resolution + queries.
    /// Engine-free: depends only on Microsoft.Data.Sqlite. Ports the
    /// path-walking logic from LensHH.Mcp.StockLensCatalog but trims the
    /// MeritFunctionEvaluator + LensInsertHelpers coupling that the
    /// original class has via its callers.
    /// </summary>
    public static class StockCatalog
    {
        /// <summary>
        /// Locate <c>stock-lens-catalog.sqlite</c> on disk.
        ///
        /// Probe order:
        ///   1. <c>LENSHH_CATALOGS_DIR</c> environment variable (if set
        ///      and the file is present there)
        ///   2. <c>{exeDir}\..\catalogs\</c> — production install
        ///      (this MCP exe lives in a subfolder of the LT install
        ///      root; catalogs/ is a sibling of that subfolder)
        ///   3. <c>{exeDir}\catalogs\</c> — flat layout (MCP exe and
        ///      catalogs sit in the same directory)
        ///   4. <c>{exeDir}\..\..\..\..\..\catalogs\</c> — dev tree
        ///      from <c>bin\Debug\net8.0\</c> up to the LT repo root
        ///
        /// Throws <see cref="FileNotFoundException"/> with the searched
        /// paths in the message if not found.
        /// </summary>
        public static string ResolveDbPath()
        {
            var env = Environment.GetEnvironmentVariable("LENSHH_CATALOGS_DIR");
            if (!string.IsNullOrWhiteSpace(env))
            {
                var p = Path.Combine(env, "stock-lens-catalog.sqlite");
                if (File.Exists(p)) return p;
            }

            string baseDir = AppContext.BaseDirectory;
            foreach (var rel in new[] {
                Path.Combine("..", "catalogs", "stock-lens-catalog.sqlite"),
                Path.Combine("catalogs", "stock-lens-catalog.sqlite"),
                Path.Combine("..", "..", "..", "..", "..", "catalogs", "stock-lens-catalog.sqlite"),
            })
            {
                var p = Path.GetFullPath(Path.Combine(baseDir, rel));
                if (File.Exists(p)) return p;
            }

            throw new FileNotFoundException(
                "stock-lens-catalog.sqlite not found. Probed paths relative to "
                + baseDir + ", plus the LENSHH_CATALOGS_DIR environment variable. "
                + "Set LENSHH_CATALOGS_DIR to the directory containing the SQLite file.");
        }

        /// <summary>
        /// Resolve a per-lens <c>.lhlt</c> file relative to the catalog's
        /// <c>Lenses/</c> directory. The <paramref name="lhltRelPath"/>
        /// is the value stored in <c>stock_lenses.lhlt_relpath</c> (a
        /// path like <c>EdmundOptics/Achromats/.../45-219.lhlt</c>).
        /// </summary>
        public static string ResolveLhltPath(string lhltRelPath)
        {
            string dbPath = ResolveDbPath();
            string root = Path.GetDirectoryName(dbPath)!;
            string full = Path.GetFullPath(Path.Combine(root, "Lenses", lhltRelPath));
            if (!File.Exists(full))
                throw new FileNotFoundException($"Stock-lens .lhlt not found: {full}");
            return full;
        }

        /// <summary>
        /// Open a read-only connection to the catalog. Caller disposes.
        /// </summary>
        public static SqliteConnection OpenReadOnly()
        {
            var conn = new SqliteConnection($"Data Source={ResolveDbPath()};Mode=ReadOnly");
            conn.Open();
            return conn;
        }

        /// <summary>
        /// Look up (vendor, lhltRelPath) for a given part number.
        /// Throws if not found. <paramref name="vendor"/> is optional —
        /// if null, the first matching part_number is returned; if
        /// provided, the match is constrained to that vendor (useful
        /// when two vendors carry the same part number).
        /// </summary>
        public static (string vendor, string lhltRelPath) ResolvePart(string partNumber, string? vendor)
        {
            using var conn = OpenReadOnly();
            using var cmd = conn.CreateCommand();
            if (vendor != null)
            {
                cmd.CommandText = "SELECT vendor, lhlt_relpath FROM stock_lenses WHERE vendor=@v AND part_number=@p LIMIT 1;";
                cmd.Parameters.AddWithValue("@v", vendor);
            }
            else
            {
                cmd.CommandText = "SELECT vendor, lhlt_relpath FROM stock_lenses WHERE part_number=@p LIMIT 1;";
            }
            cmd.Parameters.AddWithValue("@p", partNumber);
            using var rdr = cmd.ExecuteReader();
            if (!rdr.Read())
                throw new InvalidOperationException(
                    $"Stock lens not found: part_number='{partNumber}'"
                    + (vendor != null ? $", vendor='{vendor}'" : "") + ".");
            string v = rdr.GetString(0);
            if (rdr.IsDBNull(1))
                throw new InvalidOperationException($"Stock lens '{partNumber}' has no .lhlt file recorded.");
            return (v, rdr.GetString(1));
        }
    }
}
