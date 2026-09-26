using System;
using System.Collections.Generic;
using System.Linq;
using LensHH.StockMcp.Glass;

namespace LensHH.StockMcp.Writers
{
    /// <summary>
    /// One lens to one format: what export_lens does once the lens is read. Kept apart from the
    /// MCP tool so the whole catalog can be exported in a test.
    /// </summary>
    public static class LensExport
    {
        /// <summary>The formats, by the names export_lens accepts: (extension, label).</summary>
        public static readonly Dictionary<string, (string Ext, string Label)> Formats = new(StringComparer.OrdinalIgnoreCase)
        {
            ["lhlt"] = (".lhlt", "lhlt"),
            ["optiland"] = (".json", "Optiland"), ["json"] = (".json", "Optiland"),
            ["zemax"] = (".zmx", "ZEMAX"), ["zmx"] = (".zmx", "ZEMAX"),
            ["oslo"] = (".len", "OSLO"), ["len"] = (".len", "OSLO"),
            ["codev"] = (".seq", "Code V"), ["seq"] = (".seq", "Code V"),
            ["optalix"] = (".otx", "Optalix"), ["otx"] = (".otx", "Optalix"),
        };

        /// <summary>
        /// Writes <paramref name="lens"/> to <paramref name="path"/> in <paramref name="format"/>
        /// and returns the notes the export has for the user: glasses the bundled catalogs do not
        /// have (the file names them, and the program opening it must know them), and for Optiland
        /// where its glasses went. Throws for a lens the format cannot carry, such as an r^2
        /// aspheric term in OSLO, Code V or Optalix.
        /// </summary>
        public static List<string> Write(LhltFile lens, string format, string path, GlassCatalogManager glass,
                                         bool installOptilandGlasses = true)
        {
            var notes = new List<string>();
            string label = Formats[format].Label;
            switch (label)
            {
                case "lhlt": LhltWriter.Write(lens, path); return notes;
                case "ZEMAX": ZmxWriter.Write(lens, path); break;
                case "OSLO": OsloWriter.Write(lens, path, glass); break;
                case "Code V": CodeVWriter.Write(lens, path, glass); break;
                case "Optalix": OptalixWriter.Write(lens, path, glass); break;
                case "Optiland":
                    var export = OptilandWriter.Write(lens, path, glass, installOptilandGlasses);
                    if (export.GlassFolder != null)
                        notes.Add(export.Describe());
                    break;
            }

            var unknown = lens.Surfaces
                .Where(s => !s.IsMirror && !string.IsNullOrEmpty(s.Material)
                            && !s.Material.Equals("AIR", StringComparison.OrdinalIgnoreCase)
                            && glass.GetGlass(s.Material, lens.GlassCatalogs.Count > 0 ? lens.GlassCatalogs : null) == null)
                .Select(s => s.Material)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (unknown.Count > 0)
                notes.Add($"Not in the bundled glass catalogs, so written by name only: {string.Join(", ", unknown)}. "
                        + $"{label} must know {(unknown.Count == 1 ? "this glass" : "these glasses")} by that name.");
            return notes;
        }
    }
}
