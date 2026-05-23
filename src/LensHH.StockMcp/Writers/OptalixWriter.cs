using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LensHH.StockMcp.Writers
{
    // Writes Optalix .OTX lens files from an LhltFile DTO. Engine-free
    // port of LensHH.Core.IO.OptalixWriter. Curvature derived from
    // Radius (Curvature = 1/Radius; Radius=Infinity → 0); primary
    // wavelength derived from the IsPrimary flag.
    public static class OptalixWriter
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void Write(LhltFile system, string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("VERS 11.82");
            sb.AppendLine($"FILE {filePath}");

            if (!string.IsNullOrEmpty(system.Title))
                sb.AppendLine($"REM 1 {system.Title}");

            sb.AppendLine("RAIM 0");
            if (system.RayAiming == RayAimingMode.Real)
                sb.AppendLine("RAIM 2");

            sb.AppendLine(string.Format(Inv, "EPD {0:G14}", system.Aperture.Value));

            if (system.Wavelengths.Count > 0)
            {
                var wlSb = new StringBuilder("WL");
                var wtSb = new StringBuilder("WTW");
                foreach (var wl in system.Wavelengths)
                {
                    wlSb.Append(string.Format(Inv, " {0:F7}", wl.Value));
                    wtSb.Append(string.Format(Inv, " {0:G}", wl.Weight));
                }
                sb.AppendLine(wlSb.ToString());
                sb.AppendLine(wtSb.ToString());
                sb.AppendLine($"REF {GetPrimaryWavelengthIndex(system) + 1}");
            }

            // FTYP: 0 = object height, 1 = angle.
            int ftyp = system.FieldType == FieldType.ObjectAngle ? 1 : 0;
            sb.AppendLine($"FTYP {ftyp}");
            sb.AppendLine($"NFLD {system.Fields.Count}");
            for (int i = 0; i < system.Fields.Count; i++)
            {
                var f = system.Fields[i];
                sb.AppendLine(string.Format(Inv, "FLD {0} {1:G14} {2:G14} {3:F0} 1 0",
                    i + 1, 0.0, f.Y, f.Weight * 100));
            }

            sb.AppendLine("! Surface data :");
            for (int i = 0; i < system.Surfaces.Count; i++)
            {
                var s = system.Surfaces[i];
                sb.AppendLine($"SUR {i}");

                bool isMirror = !string.IsNullOrEmpty(s.Material)
                    && s.Material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase);
                bool hasAspheric = s.Type == SurfaceType.EvenAsphere
                    || s.Conic != 0
                    || (s.AsphericCoefficients != null && s.AsphericCoefficients.Any(c => c != 0));

                string sut;
                if (hasAspheric && isMirror) sut = "AM";
                else if (hasAspheric) sut = "A";
                else if (isMirror) sut = "M";
                else sut = "S";
                sb.AppendLine($"  SUT {sut}");

                double curvature = double.IsInfinity(s.Radius) ? 0.0 : 1.0 / s.Radius;
                sb.AppendLine(string.Format(Inv, "  CUY {0:E16}", curvature));

                double thi;
                if (i == system.Surfaces.Count - 1) thi = -999.0;
                else if (double.IsPositiveInfinity(s.Thickness)) thi = 1e20;
                else thi = s.Thickness;
                sb.AppendLine(string.Format(Inv, "  THI {0:E16}", thi));

                if (!string.IsNullOrEmpty(s.Material) && !isMirror)
                    sb.AppendLine($"  GLA {s.Material}");

                if (s.IsStop)
                    sb.AppendLine("  STO");

                if (s.SemiDiameter > 0)
                    sb.AppendLine(string.Format(Inv,
                        "  APE  1 {0:G12} {0:G12} 0 0 0 1 0 0 1 ''", s.SemiDiameter));

                double obscR = s.InnerRadius > 0 ? s.InnerRadius
                             : s.ObscurationRadius > 0 ? s.ObscurationRadius
                             : 0;
                if (obscR > 0)
                    sb.AppendLine(string.Format(Inv,
                        "  APE  2 {0:G12} {0:G12} 0 0 0 1 0 1", obscR));

                if (!string.IsNullOrWhiteSpace(s.Comment))
                    sb.AppendLine($"  COM {s.Comment.Trim()}");

                // VAR — variable flags. Stock lenses never set these so the
                // list stays empty and no VAR line is emitted.
                var vars = new List<string>();
                if (s.CurvatureVariable) vars.Add("CUY");
                if (s.ThicknessVariable) vars.Add("THI");
                if (s.ConicVariable)     vars.Add("K");
                if (s.AsphericVariable != null)
                {
                    string[] aspNames = { "A", "B", "C", "D" };
                    for (int v = 0; v < aspNames.Length; v++)
                    {
                        int idx = v + 1;
                        if (idx < s.AsphericVariable.Length && s.AsphericVariable[idx])
                            vars.Add(aspNames[v]);
                    }
                }
                if (vars.Count > 0)
                    sb.AppendLine($"  VAR {vars.Count} {string.Join(" ", vars)}");

                // Aspheric line: conic + 8 coefficients A..H (r⁴..r¹⁸) + 2 trailing zeros (I, J)
                if (hasAspheric)
                {
                    var aspSb = new StringBuilder("  ASP ");
                    aspSb.Append(string.Format(Inv, " {0:G14}", s.Conic));
                    var aspCoeffs = s.AsphericCoefficients;
                    for (int c = 0; c < 8; c++)
                    {
                        int idx = c + 1;
                        double coeff = (aspCoeffs != null && idx < aspCoeffs.Length) ? aspCoeffs[idx] : 0.0;
                        aspSb.Append(string.Format(Inv, " {0:E10}", coeff));
                    }
                    aspSb.Append(" 0.000000000 0.000000000");
                    sb.AppendLine(aspSb.ToString());
                }
            }

            File.WriteAllText(filePath, sb.ToString());
        }

        private static int GetPrimaryWavelengthIndex(LhltFile s)
        {
            for (int i = 0; i < s.Wavelengths.Count; i++)
                if (s.Wavelengths[i].IsPrimary) return i;
            return 0;
        }
    }
}
