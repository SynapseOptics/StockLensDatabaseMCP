using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LensHH.StockMcp.Glass;

namespace LensHH.StockMcp.Writers
{
    // Writes OSLO .len lens files from an LhltFile DTO. Engine-free port of LensHH-LT's
    // OsloWriter (1.0.158), which follows what OSLO 6.6 itself writes:
    //   - an object at infinity takes EBR and ANG, a finite one NAO and OBH, converted from the
    //     lens's EPD or F-number and field angle by a paraxial trace;
    //   - the primary wavelength goes first on the WV line, which is how OSLO knows it;
    //   - only an aperture that clips is checked (AP CHK); others go out as AP;
    //   - no word of the LEN NEW name may be a number, or OSLO reads it as the surface count.
    // A surface with an r^2 aspheric term is refused: OSLO's standard asphere has no place for it.
    public static class OsloWriter
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void Write(LhltFile system, string filePath, GlassCatalogManager glass)
        {
            for (int i = 0; i < system.Surfaces.Count; i++)
            {
                var a = system.Surfaces[i].AsphericCoefficients;
                if (a != null && a.Length > 0 && a[0] != 0.0)
                    throw new InvalidOperationException(
                        $"Surface {i} has an r² aspheric term, which OSLO's standard asphere has no place for.");
            }

            var wavelengths = OrderedWavelengths(system);
            double primaryUm = wavelengths.Count > 0 ? wavelengths[0].Value : 0.58756;
            bool infiniteObject = system.Surfaces.Count == 0
                || double.IsInfinity(system.Surfaces[0].Thickness)
                || Math.Abs(system.Surfaces[0].Thickness) >= 1e10;
            double[]? indices = null;
            double[] Indices() => indices ??= glass.BuildRefractiveIndexArray(system, primaryUm);

            var sb = new StringBuilder();
            sb.AppendLine("// OSLO 5.10");
            sb.AppendLine("// Exported from LensHH-LT");

            string title = string.IsNullOrEmpty(system.Title) ? "Untitled" : system.Title;
            sb.AppendLine($"LEN NEW \"{SanitizeOsloLenName(title)}\"");
            sb.AppendLine($"SNO1 \"{title.Replace("\"", "'")}\"");

            // SNO2..SNO10 - up to 9 extra note lines; OSLO drops the rest.
            if (!string.IsNullOrEmpty(system.Notes))
            {
                var noteLines = system.Notes.Replace("\r\n", "\n").Split('\n');
                int slot = 2;
                foreach (var rawLine in noteLines)
                {
                    if (slot > 10) break;
                    sb.AppendLine($"SNO{slot} \"{(rawLine ?? string.Empty).Replace("\"", "'")}\"");
                    slot++;
                }
            }

            if (!string.IsNullOrWhiteSpace(system.Designer))
                sb.AppendLine($"DES \"{system.Designer.Replace("\"", "'")}\"");

            sb.AppendLine("UNI 1.0");

            // Aperture and field, as OSLO writes them. (Any aperture that was not an EPD went out
            // as EBR 5, and a finite object's field as an angle.)
            double maxField = 0;
            foreach (var f in system.Fields)
                if (Math.Abs(f.Y) > maxField) maxField = Math.Abs(f.Y);
            double pupilRadius = system.Aperture.Type == ApertureType.FNumber
                ? Math.Abs(Paraxial.Efl(system, Indices())) / (2.0 * system.Aperture.Value)
                : system.Aperture.Value / 2.0;
            if (infiniteObject)
            {
                if (system.FieldType == FieldType.ObjectHeight)
                    throw new InvalidOperationException(
                        "Fields given as object heights need a finite object; OSLO takes a field angle for an object at infinity.");
                sb.AppendLine(string.Format(Inv, "EBR {0:R}", pupilRadius));
                sb.AppendLine(string.Format(Inv, "ANG {0:R}", maxField));
            }
            else
            {
                double objectToPupil = Math.Abs(Paraxial.EntrancePupilPosition(system, Indices()) + system.Surfaces[0].Thickness);
                double nao = Math.Abs(Indices()[0]) * Math.Sin(Math.Atan(pupilRadius / objectToPupil));
                double obh = system.FieldType == FieldType.ObjectHeight
                    ? maxField
                    : objectToPupil * Math.Tan(maxField * Math.PI / 180.0);
                sb.AppendLine(string.Format(Inv, "NAO {0:R}", nao));
                sb.AppendLine(string.Format(Inv, "OBH {0:R}", obh));
            }

            sb.AppendLine("// SRF 0");
            if (system.Surfaces.Count > 0)
            {
                var s0 = system.Surfaces[0];
                if (Math.Abs(s0.Curvature) > 1e-15)
                    sb.AppendLine(string.Format(Inv, "  RD {0:G10}", s0.Radius));
                double th0 = double.IsPositiveInfinity(s0.Thickness) ? 1e20 : s0.Thickness;
                sb.AppendLine(string.Format(Inv, "  TH {0:E7}", th0));
            }

            for (int i = 1; i < system.Surfaces.Count; i++)
            {
                var s = system.Surfaces[i];
                sb.AppendLine($"NXT // SRF {i}");

                if (Math.Abs(s.Curvature) > 1e-15)
                    sb.AppendLine(string.Format(Inv, "  RD {0:G10}", s.Radius));

                if (s.IsMirror)
                    sb.AppendLine("  RFH");
                else if (!string.IsNullOrEmpty(s.Material))
                    sb.AppendLine($"  GLA {s.Material}");

                if (s.IsStop)
                    sb.AppendLine("  AST");

                if (s.Conic != 0)
                    sb.AppendLine(string.Format(Inv, "  CC {0:E7}", s.Conic));

                // OSLO aspherics: AD..AJ are r^4..r^16, the internal [1]..[7].
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

                // Checked (AP CHK, which blocks rays) only for an aperture that clips: a fixed
                // semi-diameter, or an automatic one held under 100 % of the beam. Any other goes
                // out not checked (AP), which OSLO uses to draw the surface and never to block a
                // ray; an automatic stop is left to OSLO, which sizes it from EBR. (Every
                // semi-diameter went out checked, so an exported lens vignetted where its source
                // did not.)
                bool clips = s.SemiDiameterMode == SemiDiameterMode.Fixed
                    || (s.ClearAperturePercent > 0 && s.ClearAperturePercent < 100.0);
                if (s.SemiDiameter > 0)
                {
                    if (clips)
                        sb.AppendLine(string.Format(Inv, "  AP CHK {0:G10}", s.SemiDiameter));
                    else if (!s.IsStop)
                        sb.AppendLine(string.Format(Inv, "  AP {0:G10}", s.SemiDiameter));
                }

                // Central obscuration / annular pupil.
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

                // Thickness: PK TH/THM if a +-1 scale pickup targets this surface; else plain TH.
                LhltPickup? thPickup = null;
                foreach (var p in system.Pickups)
                {
                    if (p.TargetSurfaceIndex == i
                        && p.Parameter == PickupParameter.Thickness
                        && (Math.Abs(p.ScaleFactor - 1.0) < 1e-12 || Math.Abs(p.ScaleFactor + 1.0) < 1e-12))
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

            // Wavelengths, primary first. (They went in stored order, so an F d C lens with d
            // primary opened in OSLO as an F-line lens.)
            if (wavelengths.Count > 0)
            {
                sb.AppendLine("WV  " + string.Join(" ", wavelengths.Select(w => w.Value.ToString("F5", Inv))));
                sb.AppendLine("WW  " + string.Join(" ", wavelengths.Select(w => w.Weight.ToString("G", Inv))));
            }

            sb.AppendLine($"END {system.Surfaces.Count - 1}");
            File.WriteAllText(filePath, sb.ToString());
        }

        // OSLO has no primary-wavelength keyword: its primary is wavelength 1, the first on the
        // WV line. So the primary goes first and the rest follow short to long - OSLO's own
        // middle, short, long order, d F C for the usual three - each weight with its wavelength.
        private static List<LhltWavelength> OrderedWavelengths(LhltFile system)
        {
            if (system.Wavelengths.Count == 0)
                return new List<LhltWavelength>();
            int primary = system.PrimaryWavelengthIndex;
            return system.Wavelengths
                .Where((_, i) => i != primary)
                .OrderBy(w => w.Value)
                .Prepend(system.Wavelengths[primary])
                .ToList();
        }

        // OSLO's LEN NEW name: at most 32 characters, no double quotes, and no word that is a
        // number - OSLO reads one as the surface count ("62478 Negative Achromatic Lens" is
        // refused with "Maximum number of surfaces"). Such words are dropped; SNO1 keeps the
        // full title. The words are dropped AFTER cutting to 32 characters as well as before: a cut
        // can leave a new number at the end ("POSITIVE DOUBLET; 6.00MM DIA; 100.00MM EFL" cut to
        // "...; 6.00MM DIA; 10"), which OSLO would read the same way.
        private static string SanitizeOsloLenName(string title)
        {
            static string NoNumbers(string t) => string.Join(" ", t
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !double.TryParse(w, NumberStyles.Float, Inv, out _)));
            string s = NoNumbers(title.Replace("\"", "'"));
            while (s.Length > 32)
                s = NoNumbers(s.Substring(0, 32));
            return s.Length == 0 ? "Untitled" : s;
        }
    }
}
