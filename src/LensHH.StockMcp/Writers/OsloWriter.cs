using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace LensHH.StockMcp.Writers
{
    // Writes OSLO .len lens files from an LhltFile DTO. Engine-free
    // port of LensHH.Core.IO.OsloWriter; uses LhltFile in place of
    // OpticalSystem and the standalone enums. Curvature is derived
    // from Radius (1/Radius; Radius=Infinity → 0).
    public static class OsloWriter
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void Write(LhltFile system, string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// OSLO 5.10");
            sb.AppendLine("// Exported from LensHH-LT");

            string title = string.IsNullOrEmpty(system.Title) ? "Untitled" : system.Title;
            // LEN NEW: 32-char cap, no embedded quotes. SNO1 holds the
            // full title without truncation.
            sb.AppendLine($"LEN NEW \"{SanitizeOsloLenName(title)}\"");
            sb.AppendLine($"SNO1 \"{title.Replace("\"", "'")}\"");

            // SNO2..SNO10 — up to 9 extra note lines; OSLO drops the rest.
            if (!string.IsNullOrEmpty(system.Notes))
            {
                var noteLines = system.Notes.Replace("\r\n", "\n").Split('\n');
                int slot = 2;
                foreach (var rawLine in noteLines)
                {
                    if (slot > 10) break;
                    string clean = (rawLine ?? string.Empty).Replace("\"", "'");
                    sb.AppendLine($"SNO{slot} \"{clean}\"");
                    slot++;
                }
            }

            if (!string.IsNullOrWhiteSpace(system.Designer))
                sb.AppendLine($"DES \"{system.Designer.Replace("\"", "'")}\"");

            sb.AppendLine("UNI 1.0");

            double ebr = system.Aperture.Type == ApertureType.EPD
                ? system.Aperture.Value / 2.0 : 5.0;
            sb.AppendLine(string.Format(Inv, "EBR {0:G8}", ebr));

            double maxField = 0;
            foreach (var f in system.Fields)
                if (Math.Abs(f.Y) > maxField) maxField = Math.Abs(f.Y);
            sb.AppendLine(string.Format(Inv, "ANG {0:G8}", maxField));

            sb.AppendLine("// SRF 0");
            if (system.Surfaces.Count > 0)
            {
                var s0 = system.Surfaces[0];
                double th0 = double.IsPositiveInfinity(s0.Thickness) ? 1e20 : s0.Thickness;
                sb.AppendLine(string.Format(Inv, "  TH {0:E7}", th0));
            }

            for (int i = 1; i < system.Surfaces.Count; i++)
            {
                var s = system.Surfaces[i];
                sb.AppendLine($"NXT // SRF {i}");

                double curvature = double.IsInfinity(s.Radius) ? 0.0 : 1.0 / s.Radius;
                if (!double.IsInfinity(s.Radius) && Math.Abs(curvature) > 1e-15)
                    sb.AppendLine(string.Format(Inv, "  RD {0:G10}", s.Radius));

                bool isMirror = !string.IsNullOrEmpty(s.Material)
                    && s.Material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase);

                if (isMirror)
                    sb.AppendLine("  RFH");
                else if (!string.IsNullOrEmpty(s.Material))
                    sb.AppendLine($"  GLA {s.Material}");

                if (s.IsStop)
                    sb.AppendLine("  AST");

                if (s.Conic != 0)
                    sb.AppendLine(string.Format(Inv, "  CC {0:E7}", s.Conic));

                // OSLO aspherics: AD..AJ map to [1]..[7] (r⁴, r⁶, …, r¹⁶)
                if (s.AsphericCoefficients != null)
                {
                    string[] aspKeywords = { "AD", "AE", "AF", "AG", "AH", "AI", "AJ" };
                    for (int c = 0; c < aspKeywords.Length; c++)
                    {
                        int idx = c + 1;
                        if (idx < s.AsphericCoefficients.Length && s.AsphericCoefficients[idx] != 0)
                            sb.AppendLine(string.Format(Inv, "  {0} {1:E7}", aspKeywords[c], s.AsphericCoefficients[idx]));
                    }
                }

                if (s.SemiDiameter > 0)
                    sb.AppendLine(string.Format(Inv, "  AP CHK {0:G8}", s.SemiDiameter));

                // Central obscuration / annular pupil
                double obscR = s.InnerRadius > 0 ? s.InnerRadius
                             : s.ObscurationRadius > 0 ? s.ObscurationRadius
                             : 0;
                if (obscR > 0)
                {
                    sb.AppendLine("  APN 1");
                    sb.AppendLine(string.Format(Inv, "  AY1 A {0:G8}", -obscR));
                    sb.AppendLine(string.Format(Inv, "  AY2 A {0:G8}",  obscR));
                    sb.AppendLine(string.Format(Inv, "  AX1 A {0:G8}", -obscR));
                    sb.AppendLine(string.Format(Inv, "  AX2 A {0:G8}",  obscR));
                    sb.AppendLine("  ATP A 1");
                    sb.AppendLine("  AAC A 2");
                }

                // Thickness: PK TH/THM if a ±1 scale pickup targets this surface; else plain TH.
                LhltPickup? thPickup = null;
                foreach (var p in system.Pickups)
                {
                    if (p.TargetSurfaceIndex == i
                        && p.Parameter == PickupParameter.Thickness
                        && (Math.Abs(p.ScaleFactor - 1.0) < 1e-12
                            || Math.Abs(p.ScaleFactor + 1.0) < 1e-12))
                    {
                        thPickup = p;
                        break;
                    }
                }

                if (thPickup != null)
                {
                    int relOffset = thPickup.SourceSurfaceIndex - i;
                    string pkType = thPickup.ScaleFactor < 0 ? "THM" : "TH";
                    sb.AppendLine(string.Format(Inv, "  PK {0} {1} {2:G10}", pkType, relOffset, thPickup.Offset));
                }
                else
                {
                    double th = i < system.Surfaces.Count - 1 ? s.Thickness : 0;
                    sb.AppendLine(string.Format(Inv, "  TH {0:G10}", th));
                }

                if (s.HasMarginalRaySolve)
                    sb.AppendLine("CALLBACK  1");
            }

            if (system.Wavelengths.Count > 0)
            {
                var wvSb = new StringBuilder("WV ");
                var wwSb = new StringBuilder("WW ");
                foreach (var wl in system.Wavelengths)
                {
                    wvSb.Append(string.Format(Inv, " {0:F5}", wl.Value));
                    wwSb.Append(string.Format(Inv, " {0:G}", wl.Weight));
                }
                sb.AppendLine(wvSb.ToString());
                sb.AppendLine(wwSb.ToString());
            }

            sb.AppendLine($"END {system.Surfaces.Count - 1}");
            File.WriteAllText(filePath, sb.ToString());
        }

        private static string SanitizeOsloLenName(string title)
        {
            string s = title.Replace("\"", "'");
            var collapsed = new StringBuilder(s.Length);
            bool prevWs = false;
            foreach (char c in s)
            {
                bool ws = char.IsWhiteSpace(c);
                if (ws)
                {
                    if (!prevWs && collapsed.Length > 0) collapsed.Append(' ');
                    prevWs = true;
                }
                else { collapsed.Append(c); prevWs = false; }
            }
            string trimmed = collapsed.ToString().TrimEnd();
            return trimmed.Length > 32 ? trimmed.Substring(0, 32).TrimEnd() : trimmed;
        }
    }
}
