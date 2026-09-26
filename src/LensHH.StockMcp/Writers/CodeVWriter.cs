using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LensHH.StockMcp.Glass;

namespace LensHH.StockMcp.Writers
{
    // Writes Code V .seq sequence files from an LhltFile DTO. Engine-free port of LensHH-LT's
    // CodeVWriter (1.0.158), which follows what Code V itself writes, as Zemax's
    // CODEV-to-OpticStudio converter reads it:
    //   - the aperture as EPD, or FNO for an object at infinity (at a finite object an F-number
    //     goes out as the EPD it gives, Code V's FNO being defined at infinity);
    //   - an asphere as ASP followed by its K and A..G lines; a conic alone as CON and K. (ASP
    //     then CON makes the surface a plain conic in Code V, losing its terms.)
    //   - a glass as Code V names it - no punctuation - qualified NAME_CATALOG when the glass's
    //     catalog is one Code V has, so the glass and not merely its name travels; a glass from
    //     any other catalog goes out as a private glass (PRV), its index at each wavelength.
    // A surface with an r^2 aspheric term is refused: Code V's asphere starts at r^4.
    public static class CodeVWriter
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // Code V's names for the r^4, r^6 ... r^16 terms.
        private static readonly string[] CoefficientNames = { "A", "B", "C", "D", "E", "F", "G" };

        // The vendor catalogs Code V ships, and so the only ones a qualifier may name.
        private static readonly string[] CodeVCatalogs = { "HOYA", "OHARA", "SCHOTT", "CDGM", "SUMITA", "HIKARI", "CORNING" };

        public static void Write(LhltFile system, string filePath, GlassCatalogManager glass)
        {
            bool infiniteObject = system.Surfaces.Count == 0
                || double.IsInfinity(system.Surfaces[0].Thickness)
                || Math.Abs(system.Surfaces[0].Thickness) >= 1e10;
            double primaryUm = system.Wavelengths.Count > 0 ? system.Wavelengths[system.PrimaryWavelengthIndex].Value : 0.58756;

            var sb = new StringBuilder();
            sb.AppendLine("! Lens exported from LensHH-LT");
            sb.AppendLine("RDM;LEN");
            // Code V quotes the title, so an apostrophe inside would end it early.
            if (!string.IsNullOrEmpty(system.Title))
                sb.AppendLine($"TIT '{system.Title.Replace("'", "")}'");
            sb.AppendLine("DIM M");

            if (system.Aperture.Type == ApertureType.EPD)
                sb.AppendLine(string.Format(Inv, "EPD {0:R}", system.Aperture.Value));
            else if (infiniteObject)
                sb.AppendLine(string.Format(Inv, "FNO {0:R}", system.Aperture.Value));
            else
                sb.AppendLine(string.Format(Inv, "EPD {0:R}",
                    Math.Abs(Paraxial.Efl(system, glass.BuildRefractiveIndexArray(system, primaryUm))) / system.Aperture.Value));

            if (system.Wavelengths.Count > 0)
            {
                sb.AppendLine("WL  " + string.Join(" ", system.Wavelengths.Select(w => (w.Value * 1000.0).ToString("F4", Inv))));
                sb.AppendLine("WTW " + string.Join(" ", system.Wavelengths.Select(w => w.Weight.ToString("G", Inv))));
                sb.AppendLine($"REF {system.PrimaryWavelengthIndex + 1}");
            }

            // A glass from a catalog Code V does not ship (LightPath, MISC, ...) goes into a private
            // glass catalog, given by its index at each wavelength, as Code V writes one. By name
            // it would not resolve in Code V at all, and stripping its punctuation can make it
            // another glass's name: LightPath has both D-ZLAF52LA_M and D-ZLAF52LAM.
            var privateGlass = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // material -> PRV name
            var privateIndices = new List<(string Name, double[] N)>();
            foreach (var s in system.Surfaces)
            {
                if (s.IsMirror || string.IsNullOrEmpty(s.Material) || privateGlass.ContainsKey(s.Material)) continue;
                var g = glass.GetGlass(s.Material, system.GlassCatalogs.Count > 0 ? system.GlassCatalogs : null);
                if (g == null || CodeVCatalogs.Contains(CodeVCatalog(g.Catalog))) continue;
                if (system.Wavelengths.Count == 0)
                    throw new InvalidOperationException($"Glass {s.Material} needs the lens's wavelengths to be written as a Code V private glass.");
                string name = Stripped(s.Material);
                if (name.Length > 16) name = name.Substring(0, 16);
                string unique = name;
                for (int k = 2; privateIndices.Any(p => p.Name.Equals(unique, StringComparison.OrdinalIgnoreCase)); k++)
                    unique = name + k.ToString(Inv);
                privateGlass[s.Material] = unique;
                privateIndices.Add((unique, system.Wavelengths.Select(w => g.GetIndex(w.Value)).ToArray()));
            }
            if (privateIndices.Count > 0)
            {
                sb.AppendLine("PRV");
                sb.AppendLine("PWL " + string.Join(" ", system.Wavelengths.Select(w => (w.Value * 1000.0).ToString("F4", Inv))));
                foreach (var (name, n) in privateIndices)
                    sb.AppendLine($"'{name}' " + string.Join(" ", n.Select(v => v.ToString("R", Inv))));
                sb.AppendLine("END");
            }

            if (system.Fields.Count > 0)
            {
                bool heights = system.FieldType == FieldType.ObjectHeight;
                if (heights && infiniteObject)
                    throw new InvalidOperationException(
                        "Fields given as object heights need a finite object; Code V takes a field angle for an object at infinity.");
                string x = heights ? "XOB" : "XAN", y = heights ? "YOB" : "YAN";
                sb.AppendLine(x + " " + string.Join(" ", system.Fields.Select(_ => "0.0")));
                sb.AppendLine(y + " " + string.Join(" ", system.Fields.Select(f => f.Y.ToString("R", Inv))));
                sb.AppendLine("WTF " + string.Join(" ", system.Fields.Select(f => (f.Weight * 100).ToString("F0", Inv))));
            }

            for (int i = 0; i < system.Surfaces.Count; i++)
            {
                var s = system.Surfaces[i];
                bool isImage = i == system.Surfaces.Count - 1;
                string prefix = i == 0 ? "SO" : isImage ? "SI" : "S";

                double radius = double.IsInfinity(s.Radius) || double.IsNaN(s.Radius) ? 0.0 : s.Radius;
                double thickness = isImage ? 0.0 : double.IsPositiveInfinity(s.Thickness) ? 1e20 : s.Thickness;

                string material = s.IsMirror ? "REFL"
                    : string.IsNullOrEmpty(s.Material) ? "AIR"
                    : privateGlass.TryGetValue(s.Material, out var prv) ? prv
                    : ToCodeVMaterial(s.Material, system, glass);

                if (isImage)
                    sb.AppendLine(string.Format(Inv, "SI {0:G14} 0", radius));
                else
                    sb.AppendLine(string.Format(Inv, "{0} {1:G14} {2:G14} {3}", prefix, radius, thickness, material));

                if (s.IsStop)
                    sb.AppendLine("  STO");
                if (s.SemiDiameter > 0 && s.SemiDiameterMode == SemiDiameterMode.Fixed)
                    sb.AppendLine(string.Format(Inv, "  CIR {0:G14}", s.SemiDiameter));
                double obscuration = s.InnerRadius > 0 ? s.InnerRadius : s.ObscurationRadius;
                if (obscuration > 0)
                    sb.AppendLine(string.Format(Inv, "  CIR OBS {0:G14}", obscuration));

                var a = s.AsphericCoefficients ?? Array.Empty<double>();
                if (a.Length > 0 && a[0] != 0.0)
                    throw new InvalidOperationException(
                        $"Surface {i} has an r² aspheric term, which Code V's asphere has no place for.");
                if (a.Skip(1).Any(c => c != 0))
                {
                    sb.AppendLine("  ASP");
                    sb.AppendLine(string.Format(Inv, "  K {0:R}", s.Conic));
                    var terms = new List<string>();
                    for (int c = 1; c < a.Length && c <= CoefficientNames.Length; c++)
                        terms.Add(string.Format(Inv, "{0} {1:E14}", CoefficientNames[c - 1], a[c]));
                    for (int t = 0; t < terms.Count; t += 4)
                        sb.AppendLine("  " + string.Join(" ; ", terms.Skip(t).Take(4)));
                }
                else if (s.Conic != 0)
                {
                    sb.AppendLine("  CON");
                    sb.AppendLine(string.Format(Inv, "  K {0:R}", s.Conic));
                }
            }

            sb.AppendLine("GO");
            File.WriteAllText(filePath, sb.ToString());
        }

        // A glass as Code V names it: every character Code V does not accept in a glass name
        // dropped (N-BK7 is NBK7; the underscore goes too, being Code V's catalog separator), then
        // _CATALOG when the glass the lens uses is from a catalog Code V ships. (Only the N- dash
        // was dropped, and no catalog written: SK16 is in both SCHOTT and SUMITA, with different
        // dispersion.)
        private static string ToCodeVMaterial(string name, LhltFile system, GlassCatalogManager glass)
        {
            string stripped = Stripped(name);
            var g = glass.GetGlass(name, system.GlassCatalogs.Count > 0 ? system.GlassCatalogs : null);
            if (g == null) return stripped;
            string catalog = CodeVCatalog(g.Catalog);
            return CodeVCatalogs.Contains(catalog) ? stripped + "_" + catalog : stripped;
        }

        private static string Stripped(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                if (char.IsAsciiLetterOrDigit(c)) sb.Append(c);
            return sb.Length > 0 ? sb.ToString() : name;
        }

        // Our catalog to Code V's: we split Corning in two (CORNING_B, CORNING_FS), Code V keeps one.
        private static string CodeVCatalog(string catalog) =>
            catalog.StartsWith("CORNING", StringComparison.OrdinalIgnoreCase) ? "CORNING" : catalog.ToUpperInvariant();
    }
}
