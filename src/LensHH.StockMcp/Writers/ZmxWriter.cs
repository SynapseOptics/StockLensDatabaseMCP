using System.Globalization;
using System.IO;
using System.Text;

namespace LensHH.StockMcp.Writers
{
    // Writes ZEMAX .zmx text files from an LhltFile DTO.
    //
    // Engine-free port of LensHH.Core.IO.ZmxWriter. Differences from
    // the engine writer:
    //   * Curvature is computed from LhltSurface.Radius
    //     (Curvature = 1 / Radius; Radius=Infinity → 0) because the
    //     .lhlt schema stores Radius, not Curvature.
    //   * PrimaryWavelengthIndex is derived from the IsPrimary flag on
    //     LhltWavelength entries.
    //   * ClapOuterRadius is not in the .lhlt schema; we fall back to
    //     SemiDiameter — same behavior as the engine writer when a
    //     .lhlt-loaded system is re-exported (engine reader leaves
    //     ClapOuterRadius at 0).
    //
    // Encoding: UTF-16 LE with BOM — required by ZEMAX's own parser.
    // UTF-8 BOM bytes get misread as UTF-16 code units and shift
    // subsequent tokens, exposed by RayAiming=Off rendering as
    // "Paraxial" because the parser misaligned by one byte.
    public static class ZmxWriter
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void Write(LhltFile system, string filePath)
        {
            var sb = new StringBuilder();
            WriteHeader(sb, system);
            WriteAperture(sb, system);
            WriteFieldType(sb, system);
            WriteFields(sb, system);
            WriteWavelengths(sb, system);
            WriteSurfaces(sb, system);
            File.WriteAllText(filePath, sb.ToString(), Encoding.Unicode);
        }

        private static void WriteHeader(StringBuilder sb, LhltFile system)
        {
            sb.AppendLine("VERS 140228 258 40400");
            sb.AppendLine("MODE SEQ");
            if (!string.IsNullOrEmpty(system.Title))
                sb.AppendLine($"TITL {system.Title}");
            sb.AppendLine("UNIT MM X W X CM MR CPMM");
            if (system.GlassCatalogs.Count > 0)
                sb.AppendLine("GCAT " + string.Join(" ", system.GlassCatalogs));
        }

        private static void WriteAperture(StringBuilder sb, LhltFile system)
        {
            switch (system.Aperture.Type)
            {
                case ApertureType.EPD:
                    sb.AppendLine(FormatDouble("ENPD", system.Aperture.Value));
                    break;
                case ApertureType.FNumber:
                    sb.AppendLine(FormatDouble("FNUM", system.Aperture.Value));
                    break;
            }
        }

        private static void WriteFieldType(StringBuilder sb, LhltFile system)
        {
            int ftype = system.FieldType == FieldType.ObjectAngle ? 0 : 1;
            int afocal = system.IsAfocal ? 1 : 0;
            sb.AppendLine($"FTYP {ftype} 0 {system.Fields.Count} {system.Wavelengths.Count} 0 0 {afocal} 0 0");

            // RAIM mode mapping: Real and Robust both map to mode=2; Robust
            // also sets the parts[6] robust-search flag. Off maps to 0.
            bool useRealAiming = system.RayAiming == RayAimingMode.Real
                              || system.RayAiming == RayAimingMode.Robust;
            int zmxRayAimMode = useRealAiming ? 2 : 0;
            int robust = system.RayAiming == RayAimingMode.Robust ? 1 : 0;
            sb.AppendLine($"RAIM 0 {zmxRayAimMode} 1 1 0 {robust} 0 0 0 1");
        }

        private static void WriteFields(StringBuilder sb, LhltFile system)
        {
            sb.Append("XFLN");
            for (int i = 0; i < system.Fields.Count; i++)
                sb.Append(" 0");
            sb.AppendLine();

            sb.Append("YFLN");
            foreach (var field in system.Fields)
                sb.Append(" " + field.Y.ToString(Inv));
            sb.AppendLine();

            sb.Append("FWGN");
            foreach (var field in system.Fields)
                sb.Append(" " + field.Weight.ToString(Inv));
            sb.AppendLine();
        }

        private static void WriteWavelengths(StringBuilder sb, LhltFile system)
        {
            for (int i = 0; i < system.Wavelengths.Count; i++)
            {
                var wl = system.Wavelengths[i];
                sb.AppendLine($"WAVM {i + 1} {wl.Value.ToString(Inv)} {wl.Weight.ToString(Inv)}");
            }

            int primaryIdx = 0;
            for (int i = 0; i < system.Wavelengths.Count; i++)
                if (system.Wavelengths[i].IsPrimary) { primaryIdx = i; break; }
            sb.AppendLine($"PWAV {primaryIdx + 1}");
        }

        private static void WriteSurfaces(StringBuilder sb, LhltFile system)
        {
            foreach (var surface in system.Surfaces)
            {
                sb.AppendLine($"SURF {surface.Index}");

                if (!string.IsNullOrEmpty(surface.Comment))
                    sb.AppendLine($"  COMM {surface.Comment}");

                if (surface.IsStop)
                    sb.AppendLine("  STOP");

                switch (surface.Type)
                {
                    case SurfaceType.Standard:
                        sb.AppendLine("  TYPE STANDARD");
                        break;
                    case SurfaceType.EvenAsphere:
                        sb.AppendLine("  TYPE EVENASPH");
                        break;
                }

                double curvature = double.IsInfinity(surface.Radius) ? 0.0 : 1.0 / surface.Radius;
                sb.AppendLine(FormatDouble("  CURV", curvature));

                if (double.IsPositiveInfinity(surface.Thickness))
                    // ZEMAX requires all-caps INFINITY; mixed-case "Infinity"
                    // is rejected with "Invalid input for thickness".
                    sb.AppendLine("  DISZ INFINITY");
                else
                    sb.AppendLine(FormatDouble("  DISZ", surface.Thickness));

                if (!string.IsNullOrEmpty(surface.Material))
                    sb.AppendLine($"  GLAS {surface.Material} 0 0 0 0 0");

                if (surface.SemiDiameterMode == SemiDiameterMode.Fixed && surface.SemiDiameter > 0)
                    sb.AppendLine($"  DIAM {surface.SemiDiameter.ToString("G17", Inv)} 1 0 0 1 \"\"");

                if (surface.Conic != 0)
                    sb.AppendLine(FormatDouble("  CONI", surface.Conic));

                if (surface.InnerRadius > 0)
                {
                    double clapOuter = surface.SemiDiameter;
                    sb.AppendLine($"  CLAP {surface.InnerRadius.ToString("G17", Inv)} {clapOuter.ToString("G17", Inv)} 0");
                }
                if (surface.ObscurationRadius > 0)
                    sb.AppendLine($"  OBSC 0 {surface.ObscurationRadius.ToString("G17", Inv)} 0");
                if (surface.FloatingApertureRadius > 0)
                    sb.AppendLine($"  FLAP 0 {surface.FloatingApertureRadius.ToString("G17", Inv)} 0");

                if (surface.Type == SurfaceType.EvenAsphere && surface.AsphericCoefficients != null)
                {
                    for (int i = 0; i < surface.AsphericCoefficients.Length; i++)
                    {
                        if (surface.AsphericCoefficients[i] != 0)
                            sb.AppendLine($"  PARM {i + 1} {surface.AsphericCoefficients[i].ToString("E16", Inv)}");
                    }
                }
            }
        }

        private static string FormatDouble(string keyword, double value)
            => $"{keyword} {value.ToString("G17", Inv)}";
    }
}
