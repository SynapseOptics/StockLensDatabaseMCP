using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LensHH.StockMcp.Glass;

namespace LensHH.StockMcp.Writers
{
    // Writes Optalix .otx lens files from an LhltFile DTO. Engine-free port of LensHH-LT's
    // OptalixWriter (1.0.158), which follows what Optalix writes, surveyed across the 1019 lens
    // files that ship with it:
    //   - one RAIM line: 2 aims at the real stop (Optalix's default), 1 at the paraxial pupil;
    //   - the aperture as EPD, or FNO for an object at infinity (at a finite object an F-number
    //     goes out as the EPD it gives);
    //   - FTYP 1 for field angles, 2 for object heights;
    //   - FH 1 on a surface whose aperture clips: Optalix's apertures otherwise never block a ray;
    //   - ASP as the conic and eight even terms and a zero; PIM 0 keeps the image where the lens
    //     puts it.
    // A surface with an r^2 aspheric term is refused: Optalix's even asphere starts at r^4.
    public static class OptalixWriter
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void Write(LhltFile system, string filePath, GlassCatalogManager glass)
        {
            bool infiniteObject = system.Surfaces.Count == 0
                || double.IsInfinity(system.Surfaces[0].Thickness)
                || Math.Abs(system.Surfaces[0].Thickness) >= 1e10;
            double primaryUm = system.Wavelengths.Count > 0 ? system.Wavelengths[system.PrimaryWavelengthIndex].Value : 0.58756;

            var sb = new StringBuilder();
            sb.AppendLine("VERS 11.82");
            sb.AppendLine($"FILE {filePath}");
            if (!string.IsNullOrEmpty(system.Title))
                sb.AppendLine($"REM 1 {system.Title}");

            // Optalix writes RAIM 2 (the real stop) in 962 of its 1019 files and never RAIM 0.
            // (This wrote RAIM 0 and then RAIM 2 on a second line.)
            sb.AppendLine($"RAIM {(system.RayAiming == RayAimingMode.Off ? 1 : 2)}");

            // (This wrote EPD with whatever the aperture's value was: an F/6.3 lens went out with
            // an entrance pupil 6.3 mm across.)
            if (system.Aperture.Type == ApertureType.EPD)
                sb.AppendLine(string.Format(Inv, "EPD {0:R}", system.Aperture.Value));
            else if (infiniteObject)
                sb.AppendLine(string.Format(Inv, "FNO {0:R}", system.Aperture.Value));
            else
                sb.AppendLine(string.Format(Inv, "EPD {0:R}",
                    Math.Abs(Paraxial.Efl(system, glass.BuildRefractiveIndexArray(system, primaryUm))) / system.Aperture.Value));

            if (system.Wavelengths.Count > 0)
            {
                sb.AppendLine("WL " + string.Join(" ", system.Wavelengths.Select(w => w.Value.ToString("F7", Inv))));
                sb.AppendLine("WTW " + string.Join(" ", system.Wavelengths.Select(w => Weight(w.Weight))));
                sb.AppendLine($"REF {system.PrimaryWavelengthIndex + 1}");
            }

            // (This wrote 0 for object heights, which is not an Optalix field type.)
            sb.AppendLine($"FTYP {(system.FieldType == FieldType.ObjectHeight ? 2 : 1)}");
            sb.AppendLine($"NFLD {system.Fields.Count}");
            for (int i = 0; i < system.Fields.Count; i++)
            {
                var f = system.Fields[i];
                sb.AppendLine(string.Format(Inv, "FLD {0} {1:R} {2:R} {3} 1 0", i + 1, 0.0, f.Y, Weight(f.Weight)));
            }

            sb.AppendLine("PIM 0");

            sb.AppendLine("! Surface data :");
            for (int i = 0; i < system.Surfaces.Count; i++)
            {
                var s = system.Surfaces[i];
                bool isImage = i == system.Surfaces.Count - 1;
                double thi = isImage ? 0.0 : double.IsPositiveInfinity(s.Thickness) ? 1e20 : s.Thickness;

                sb.AppendLine($"SUR {i}");
                bool hasAspheric = s.Type == SurfaceType.EvenAsphere
                    || s.Conic != 0
                    || (s.AsphericCoefficients != null && s.AsphericCoefficients.Any(c => c != 0));
                // One base type - S sphere or A asphere - and M for a mirror.
                sb.AppendLine($"  SUT {(hasAspheric ? "A" : "S")}{(s.IsMirror ? "M" : "")}");
                sb.AppendLine(string.Format(Inv, "  CUY {0:E16}", s.Curvature));
                sb.AppendLine(string.Format(Inv, "  THI {0:E16}", thi));

                if (!s.IsMirror && !string.IsNullOrEmpty(s.Material))
                    sb.AppendLine($"  GLA {s.Material}");

                if (s.IsStop)
                    sb.AppendLine("  STO");

                if (s.SemiDiameter > 0)
                    sb.AppendLine(string.Format(Inv, "  APE  1 {0:G12} {0:G12} 0 0 0 1 0 0 1 ''", s.SemiDiameter));
                double obscR = s.InnerRadius > 0 ? s.InnerRadius : s.ObscurationRadius > 0 ? s.ObscurationRadius : 0;
                if (obscR > 0)
                    sb.AppendLine(string.Format(Inv, "  APE  2 {0:G12} {0:G12} 0 0 0 1 0 1 1 ''", obscR));
                // FH 1 marks an aperture that clips: a fixed semi-diameter, or an automatic one
                // held under 100 % of the beam. (No FH was written, so an exported lens was not
                // vignetted in Optalix where its source was.)
                bool clips = s.SemiDiameterMode == SemiDiameterMode.Fixed
                    || (s.ClearAperturePercent > 0 && s.ClearAperturePercent < 100.0);
                if (clips && s.SemiDiameter > 0)
                    sb.AppendLine("  FH 1 1");

                if (!string.IsNullOrWhiteSpace(s.Comment))
                    sb.AppendLine($"  COM {s.Comment.Trim()}");

                // VAR - variable flags. Stock lenses never set these.
                var vars = new List<string>();
                if (s.CurvatureVariable) vars.Add("CUY");
                if (s.ThicknessVariable) vars.Add("THI");
                if (s.ConicVariable) vars.Add("K");
                if (s.AsphericVariable != null)
                {
                    string[] aspNames = { "A", "B", "C", "D" };
                    for (int v = 0; v < aspNames.Length; v++)
                        if (v + 1 < s.AsphericVariable.Length && s.AsphericVariable[v + 1])
                            vars.Add(aspNames[v]);
                }
                if (vars.Count > 0)
                    sb.AppendLine($"  VAR {vars.Count} {string.Join(" ", vars)}");

                // The conic, the eight even terms A..H (r^4 to r^18), and a zero - ten values.
                if (hasAspheric)
                {
                    var a = s.AsphericCoefficients ?? Array.Empty<double>();
                    if (a.Length > 0 && a[0] != 0.0)
                        throw new InvalidOperationException(
                            $"Surface {i} has an r² aspheric term, which Optalix's even asphere has no place for.");
                    var aspSb = new StringBuilder("  ASP");
                    aspSb.Append(string.Format(Inv, " {0:R}", s.Conic));
                    for (int c = 1; c <= 8; c++)
                        aspSb.Append(string.Format(Inv, " {0:E10}", c < a.Length ? a[c] : 0.0));
                    aspSb.Append(" 0");
                    sb.AppendLine(aspSb.ToString());
                }
            }

            File.WriteAllText(filePath, sb.ToString());
        }

        private static string Weight(double w) =>
            Math.Max(0, Math.Min(100, (int)Math.Round(w * 100.0))).ToString(Inv);
    }
}
