using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LensHH.StockMcp.Writers
{
    // Writes Code V .seq sequence files from an LhltFile DTO.
    // Engine-free port of LensHH.Core.IO.CodeVWriter; PrimaryWavelength
    // is derived from the IsPrimary flag, all other field access maps
    // 1:1 from OpticalSystem to LhltFile.
    public static class CodeVWriter
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void Write(LhltFile system, string filePath)
        {
            var sb = new StringBuilder();
            sb.AppendLine("! Lens exported from LensHH-LT");
            sb.AppendLine("RDM;LEN");
            sb.AppendLine("DIM M");

            if (system.Aperture.Type == ApertureType.EPD)
                sb.AppendLine(D("EPD", system.Aperture.Value));
            else
                sb.AppendLine(D("FNO", system.Aperture.Value));

            if (!string.IsNullOrEmpty(system.Title))
                sb.AppendLine($"TIT '{system.Title}'");

            if (system.Wavelengths.Count > 0)
            {
                var wlSb = new StringBuilder("WL  ");
                var wtSb = new StringBuilder("WTW  ");
                foreach (var wl in system.Wavelengths)
                {
                    wlSb.Append(string.Format(Inv, " {0:F4}", wl.Value * 1000.0));
                    wtSb.Append(string.Format(Inv, " {0:G}", wl.Weight));
                }
                sb.AppendLine(wlSb.ToString());
                sb.AppendLine(wtSb.ToString());
                sb.AppendLine($"REF {GetPrimaryWavelengthIndex(system) + 1}");
            }

            if (system.Fields.Count > 0)
            {
                var xSb = new StringBuilder("XAN  ");
                var ySb = new StringBuilder("YAN  ");
                var fwSb = new StringBuilder("WTF  ");
                foreach (var f in system.Fields)
                {
                    xSb.Append("  0.00000");
                    ySb.Append(string.Format(Inv, " {0:F5}", f.Y));
                    fwSb.Append(string.Format(Inv, " {0:F0}", f.Weight * 100));
                }
                sb.AppendLine(xSb.ToString());
                sb.AppendLine(ySb.ToString());
                sb.AppendLine(fwSb.ToString());
            }

            for (int i = 0; i < system.Surfaces.Count; i++)
            {
                var s = system.Surfaces[i];
                string prefix;
                if (i == 0) prefix = "SO";
                else if (i == system.Surfaces.Count - 1) prefix = "SI";
                else prefix = "S";

                double radius = double.IsInfinity(s.Radius) || double.IsNaN(s.Radius) ? 0.0 : s.Radius;
                double thickness = double.IsPositiveInfinity(s.Thickness) ? 1e20 : s.Thickness;

                bool isMirror = !string.IsNullOrEmpty(s.Material)
                    && s.Material.Equals("MIRROR", StringComparison.OrdinalIgnoreCase);
                string material = isMirror ? "REFL"
                    : string.IsNullOrEmpty(s.Material) ? "AIR"
                    : CatalogNamesToCodeV(s.Material!);

                sb.AppendLine(string.Format(Inv, "{0} {1:G14} {2:G14} {3}", prefix, radius, thickness, material));

                if (s.IsStop)
                    sb.AppendLine("  STO");
                if (s.SemiDiameter > 0 && s.SemiDiameterMode == SemiDiameterMode.Fixed)
                    sb.AppendLine(string.Format(Inv, "  CIR {0:G14}", s.SemiDiameter));

                var aspCoeffs = s.AsphericCoefficients;
                bool hasAspCoeffs = aspCoeffs != null && aspCoeffs.Any(c => c != 0);
                if (hasAspCoeffs && aspCoeffs != null)
                {
                    // Internal [1]=r⁴, [2]=r⁶, … → Code V A=r⁴, B=r⁶, …
                    var aspSb = new StringBuilder("  ASP");
                    string[] coeffNames = { "A", "B", "C", "D", "E", "F", "G", "H" };
                    for (int c = 0; c < coeffNames.Length; c++)
                    {
                        int idx = c + 1;
                        double coeff = idx < aspCoeffs.Length ? aspCoeffs[idx] : 0.0;
                        aspSb.Append(string.Format(Inv, " ; {0} {1:E10}", coeffNames[c], coeff));
                    }
                    sb.AppendLine(aspSb.ToString());
                }

                if (s.Conic != 0)
                    sb.AppendLine(string.Format(Inv, "  CON ; K {0:G14}", s.Conic));
            }

            sb.AppendLine("GO");
            File.WriteAllText(filePath, sb.ToString());
        }

        private static string D(string keyword, double value)
            => string.Format(Inv, "{0} {1:G14}", keyword, value);

        // Code V drops the dash in Schott N-prefix glass names
        // (N-SF10 → NSF10). Inverse of CodeVReader's CodeVNamesToCatalog.
        private static string CatalogNamesToCodeV(string name)
        {
            if (name.Length >= 2 && name[0] == 'N' && name[1] == '-')
                return "N" + name.Substring(2);
            return name;
        }

        private static int GetPrimaryWavelengthIndex(LhltFile s)
        {
            for (int i = 0; i < s.Wavelengths.Count; i++)
                if (s.Wavelengths[i].IsPrimary) return i;
            return 0;
        }
    }
}
