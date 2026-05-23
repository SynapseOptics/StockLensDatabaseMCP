using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Server;
using LensHH.StockMcp.Writers;

namespace LensHH.StockMcp.Tools
{
    /// <summary>
    /// Read-only MCP tools backed by the stock-lens catalog SQLite.
    /// All five P1 tools live in this single file — small surface, no
    /// engine references, no session state.
    /// </summary>
    [McpServerToolType]
    public class StockTools
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ── search_stock ────────────────────────────────────────────────────

        [McpServerTool, Description(
            "Search the stock-lens catalog. All filters optional; only the provided "
            + "ones are applied. Returns one line per result: '<vendor> <part_number> "
            + "<family> EFL=<mm> f/<fnum> D=<mm> n_elem=<count> | <description>'. The "
            + "part_number can be passed to get_lens_details or export_lhlt. Use for "
            + "queries like 'find achromat doublets, EFL 45-55 mm, diameter <= 25.4'.")]
        public string SearchStock(
            double? eflMin = null,
            double? eflMax = null,
            double? diameterMin = null,
            double? diameterMax = null,
            double? fnumMin = null,
            double? fnumMax = null,
            int? nElements = null,
            string? vendor = null,
            string? familyLike = null,
            string? glassLike = null,
            int limit = 30)
        {
            using var conn = StockCatalog.OpenReadOnly();

            var where = new System.Collections.Generic.List<string> { "import_status = 'ok'" };
            var args = new System.Collections.Generic.List<(string n, object v)>();
            void Add(string clause, string n, object v) { where.Add(clause); args.Add((n, v)); }

            if (eflMin.HasValue)      Add("efl_mm        >= @eflMin",  "@eflMin",  eflMin.Value);
            if (eflMax.HasValue)      Add("efl_mm        <= @eflMax",  "@eflMax",  eflMax.Value);
            if (diameterMin.HasValue) Add("diameter_mm   >= @diaMin",  "@diaMin",  diameterMin.Value);
            if (diameterMax.HasValue) Add("diameter_mm   <= @diaMax",  "@diaMax",  diameterMax.Value);
            if (fnumMin.HasValue)     Add("fnum          >= @fnMin",   "@fnMin",   fnumMin.Value);
            if (fnumMax.HasValue)     Add("fnum          <= @fnMax",   "@fnMax",   fnumMax.Value);
            if (nElements.HasValue)   Add("n_elements     = @nel",     "@nel",     nElements.Value);
            if (!string.IsNullOrWhiteSpace(vendor))     Add("vendor = @vend",      "@vend", vendor!);
            if (!string.IsNullOrWhiteSpace(familyLike)) Add("family LIKE @fam",    "@fam",  familyLike!);
            if (!string.IsNullOrWhiteSpace(glassLike))  Add("glass_names_json LIKE @gl", "@gl", "%" + glassLike! + "%");

            if (limit <= 0 || limit > 200) limit = 30;

            string sql =
                "SELECT vendor, part_number, family, efl_mm, fnum, diameter_mm, "
                + "       n_elements, description "
                + "FROM stock_lenses WHERE " + string.Join(" AND ", where) + " "
                + "ORDER BY ABS(COALESCE(efl_mm,0) - @sortRef), part_number "
                + "LIMIT @lim;";

            double sortRef = (eflMin ?? 0) + (((eflMax ?? eflMin) ?? 0) - (eflMin ?? 0)) * 0.5;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
            cmd.Parameters.AddWithValue("@sortRef", sortRef);
            cmd.Parameters.AddWithValue("@lim", limit);

            var sb = new StringBuilder();
            int count = 0;
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                count++;
                string V(int i) => rdr.IsDBNull(i) ? "?" : rdr.GetValue(i)?.ToString() ?? "?";
                string F(int i, string fmt) => rdr.IsDBNull(i) ? "?" : rdr.GetDouble(i).ToString(fmt, Inv);
                sb.Append(V(0)).Append(' ').Append(V(1)).Append(' ').Append(V(2))
                  .Append(" EFL=").Append(F(3, "0.###"))
                  .Append(" f/").Append(F(4, "0.##"))
                  .Append(" D=").Append(F(5, "0.##"))
                  .Append(" n_elem=").Append(V(6))
                  .Append(" | ").Append(V(7))
                  .Append('\n');
            }
            if (count == 0) return "No stock lenses match the given filters.";
            sb.Insert(0, $"Found {count} stock lens(es) (limit {limit}):\n");
            return sb.ToString();
        }

        // ── get_lens_details ────────────────────────────────────────────────

        [McpServerTool, Description(
            "Return full prescription details for a single stock lens, by part number. "
            + "Includes all metadata fields from stock_lenses (EFL, BFL, F/#, glasses, "
            + "coating, wavelengths, etc.) plus every row from lens_surfaces (radius, "
            + "thickness, glass, conic, clear aperture, stop flag). The vendor argument "
            + "is optional — provide it only if two vendors carry the same part_number.")]
        public string GetLensDetails(string partNumber, string? vendor = null)
        {
            using var conn = StockCatalog.OpenReadOnly();

            // 1. stock_lenses row
            using (var cmd = conn.CreateCommand())
            {
                if (vendor != null)
                {
                    cmd.CommandText = "SELECT * FROM stock_lenses WHERE part_number=@p AND vendor=@v LIMIT 1;";
                    cmd.Parameters.AddWithValue("@v", vendor);
                }
                else
                {
                    cmd.CommandText = "SELECT * FROM stock_lenses WHERE part_number=@p LIMIT 1;";
                }
                cmd.Parameters.AddWithValue("@p", partNumber);

                using var rdr = cmd.ExecuteReader();
                if (!rdr.Read())
                    return $"No stock lens found for part_number='{partNumber}'"
                         + (vendor != null ? $", vendor='{vendor}'" : "") + ".";

                var sb = new StringBuilder();
                sb.AppendLine($"=== {rdr["vendor"]} {rdr["part_number"]} ===");
                for (int i = 0; i < rdr.FieldCount; i++)
                {
                    var name = rdr.GetName(i);
                    if (name == "vendor" || name == "part_number") continue;
                    var value = rdr.IsDBNull(i) ? "—" : rdr.GetValue(i)?.ToString() ?? "—";
                    sb.AppendLine($"  {name,-26} {value}");
                }

                // 2. lens_surfaces rows
                sb.AppendLine();
                sb.AppendLine("Surfaces:");
                sb.AppendLine($"  {"#",-3} {"radius_mm",12} {"thickness_mm",14} {"glass",-12} {"clearAp",10} {"conic",8} {"stop",6} {"type"}");

                using var sCmd = conn.CreateCommand();
                sCmd.CommandText = "SELECT surface_index, radius_mm, thickness_mm, glass_name, "
                                 + "clear_aperture_mm, conic, is_stop, surface_type "
                                 + "FROM lens_surfaces WHERE part_number=@p"
                                 + (vendor != null ? " AND vendor=@v" : "")
                                 + " ORDER BY surface_index;";
                sCmd.Parameters.AddWithValue("@p", partNumber);
                if (vendor != null) sCmd.Parameters.AddWithValue("@v", vendor);

                using var sRdr = sCmd.ExecuteReader();
                while (sRdr.Read())
                {
                    string D(int i, string fmt = "0.####") => sRdr.IsDBNull(i)
                        ? "—" : sRdr.GetDouble(i).ToString(fmt, Inv);
                    string S(int i) => sRdr.IsDBNull(i) ? "—" : sRdr.GetValue(i)?.ToString() ?? "—";
                    sb.AppendLine($"  {sRdr.GetInt32(0),-3} {D(1),12} {D(2),14} {S(3),-12} "
                                + $"{D(4),10} {D(5),8} {(sRdr.GetBoolean(6) ? "STOP" : ""),6} {S(7)}");
                }
                return sb.ToString();
            }
        }

        // ── export_lens ──────────────────────────────────────────────────────

        [McpServerTool, Description(
            "Export a stock lens prescription in any supported optical-design format. "
            + "Engine-free: reads the bundled .lhlt prescription via standalone DTOs "
            + "and writes the requested format directly. Output is byte-identical to "
            + "the equivalent LensHH-LT engine pipeline.\n\n"
            + "Supported formats (case-insensitive):\n"
            + "  lhlt     — native LensHH-LT JSON\n"
            + "  optiland — Optiland .json (alias: json)\n"
            + "  zemax    — ZEMAX .zmx text, UTF-16 LE with BOM (alias: zmx)\n"
            + "  oslo     — OSLO .len (alias: len)\n"
            + "  codev    — Code V .seq sequence file (alias: seq)\n"
            + "  optalix  — Optalix .otx (alias: otx)\n\n"
            + "Provide partNumber, format, and outputPath. If outputPath is an "
            + "existing directory, the filename is derived as {vendor}_{part}{ext} "
            + "(or {vendor}_{part}_rev{ext} when reversed=true).\n\n"
            + "Set reversed=true to export the lens flipped front-to-back — the "
            + "refractive surface order is reversed, each radius is negated, and "
            + "thickness/material associations shift to preserve the physical lens. "
            + "Useful for composing Plössl-style systems by hand: pull each stock "
            + "doublet twice (one normal, one reversed) and assemble in your design "
            + "tool. Optical power is preserved by the reversal; only orientation "
            + "changes. The Title gets a ' (reversed)' suffix to mark the file.\n\n"
            + "The vendor argument is optional — supply only when two vendors share "
            + "the same part_number. Returns resolved source, destination, and file "
            + "size on success.")]
        public string ExportLens(
            string partNumber,
            string format,
            string outputPath,
            string? vendor = null,
            bool reversed = false)
        {
            if (string.IsNullOrWhiteSpace(outputPath))
                return "outputPath is required.";
            if (string.IsNullOrWhiteSpace(format))
                return "format is required (one of: lhlt, optiland, zemax, oslo, codev, optalix).";

            string fmt = format.Trim().ToLowerInvariant();
            string ext;
            string label;
            Action<LhltFile, string>? writer;

            switch (fmt)
            {
                case "lhlt":
                    ext = ".lhlt"; label = "lhlt";     writer = null;                 break;
                case "optiland": case "json":
                    ext = ".json"; label = "Optiland"; writer = OptilandWriter.Write; break;
                case "zemax":    case "zmx":
                    ext = ".zmx";  label = "ZEMAX";    writer = ZmxWriter.Write;      break;
                case "oslo":     case "len":
                    ext = ".len";  label = "OSLO";     writer = OsloWriter.Write;     break;
                case "codev":    case "seq":
                    ext = ".seq";  label = "Code V";   writer = CodeVWriter.Write;    break;
                case "optalix":  case "otx":
                    ext = ".otx";  label = "Optalix";  writer = OptalixWriter.Write;  break;
                default:
                    return $"Unknown format '{format}'. "
                         + "Valid: lhlt, optiland (json), zemax (zmx), oslo (len), codev (seq), optalix (otx).";
            }

            try
            {
                var (resolvedVendor, lhltRel) = StockCatalog.ResolvePart(partNumber, vendor);
                string src = StockCatalog.ResolveLhltPath(lhltRel);

                string dst = outputPath;
                if (Directory.Exists(outputPath))
                {
                    string suffix = reversed ? "_rev" : "";
                    dst = Path.Combine(outputPath, $"{resolvedVendor}_{partNumber}{suffix}{ext}");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dst))!);

                if (writer == null && !reversed)
                {
                    // lhlt + non-reversed = catalog passthrough, byte-copy
                    File.Copy(src, dst, overwrite: true);
                }
                else
                {
                    LhltFile lens = LhltReader.Read(src);
                    if (reversed)
                        lens = LensReversal.Reverse(lens);

                    if (writer == null)
                        LhltWriter.Write(lens, dst);  // lhlt + reversed
                    else
                        writer(lens, dst);            // any non-lhlt format
                }

                long size = new FileInfo(dst).Length;
                string orient = reversed ? " (reversed)" : "";
                return $"Exported {resolvedVendor}/{partNumber}{orient} to {label} format.\n"
                     + $"  source: {src}\n  dest:   {dst}\n  size:   {size} bytes";
            }
            catch (Exception ex)
            {
                return $"export_lens failed: {ex.Message}";
            }
        }

        // ── list_vendors ────────────────────────────────────────────────────

        [McpServerTool, Description(
            "List distinct vendors in the stock-lens catalog with a part count for each. "
            + "Use to discover what vendors are bundled (EdmundOptics, ThorLabs, "
            + "RossOptical, etc.) and how broadly each is covered.")]
        public string ListVendors()
        {
            using var conn = StockCatalog.OpenReadOnly();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT vendor, COUNT(*) AS n FROM stock_lenses "
                            + "WHERE import_status='ok' GROUP BY vendor ORDER BY vendor;";
            using var rdr = cmd.ExecuteReader();

            var sb = new StringBuilder();
            sb.AppendLine("Vendors in catalog:");
            int total = 0;
            while (rdr.Read())
            {
                int n = rdr.GetInt32(1);
                total += n;
                sb.AppendLine($"  {rdr.GetString(0),-20} {n,6} parts");
            }
            sb.AppendLine($"  {"(total)",-20} {total,6} parts");
            return sb.ToString();
        }

        // ── list_glasses ────────────────────────────────────────────────────

        [McpServerTool, Description(
            "List distinct glass names referenced by stock lenses, with a count of "
            + "how many parts use each glass. Pulled from the JSON-encoded "
            + "glass_names array on each row. Useful for discovering which catalog "
            + "glasses are realised in stock parts (so you can target them in glass-"
            + "substitution catalogs like StockGlassesVisible / StockGlassesUV).")]
        public string ListGlasses(int limit = 50)
        {
            using var conn = StockCatalog.OpenReadOnly();
            using var cmd = conn.CreateCommand();

            // SQLite's json_each unrolls a JSON array column row-by-row.
            cmd.CommandText =
                "SELECT je.value AS glass, COUNT(*) AS n "
                + "FROM stock_lenses, json_each(glass_names_json) AS je "
                + "WHERE import_status='ok' AND glass_names_json IS NOT NULL "
                + "GROUP BY glass "
                + "ORDER BY n DESC, glass "
                + "LIMIT @lim;";
            cmd.Parameters.AddWithValue("@lim", Math.Clamp(limit, 1, 500));

            var sb = new StringBuilder();
            sb.AppendLine($"Distinct glasses in catalog (top {limit} by part count):");
            int count = 0;
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                count++;
                sb.AppendLine($"  {rdr.GetString(0),-20} {rdr.GetInt32(1),6} parts");
            }
            if (count == 0) sb.AppendLine("  (no rows — is the catalog populated?)");
            return sb.ToString();
        }
    }
}
