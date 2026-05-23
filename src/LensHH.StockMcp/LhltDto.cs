using System.Collections.Generic;

namespace LensHH.StockMcp
{
    // Data transfer objects for the .lhlt JSON format. Mirror of
    // LensHH.Core.IO.LhltFile but trimmed: merit-function +
    // configuration-editor classes are omitted because the format
    // writers (Optiland, ZEMAX, Code V, OSLO, Optalix) emit only
    // prescription data — surfaces, materials, wavelengths, fields,
    // aperture, ray-aiming mode. Those merit/config blocks are
    // skipped during deserialization (System.Text.Json ignores
    // unknown properties by default).
    //
    // Property names and ordering must match the engine's LhltFile so
    // System.Text.Json round-trips cleanly. Enum types are mirrored in
    // Enums.cs; the JsonStringEnumConverter (configured at the call
    // site in LhltReader) handles the string ↔ int conversion.

    public class LhltFile
    {
        public int FormatVersion { get; set; } = 1;
        public string Title { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public string Designer { get; set; } = string.Empty;

        public LhltAperture Aperture { get; set; } = new LhltAperture();
        public FieldType FieldType { get; set; }
        public List<LhltSurface> Surfaces { get; set; } = new List<LhltSurface>();
        public List<LhltWavelength> Wavelengths { get; set; } = new List<LhltWavelength>();
        public List<LhltField> Fields { get; set; } = new List<LhltField>();
        public List<LhltPickup> Pickups { get; set; } = new List<LhltPickup>();
        public RayAimingMode RayAiming { get; set; }
        public bool IsAfocal { get; set; }
        public bool PenalizeVignetting { get; set; }
        public List<string> GlassCatalogs { get; set; } = new List<string>();
    }

    public class LhltAperture
    {
        public ApertureType Type { get; set; }
        public double Value { get; set; }
    }

    public class LhltSurface
    {
        public int Index { get; set; }
        public SurfaceType Type { get; set; }
        public string Comment { get; set; } = string.Empty;
        public double Radius { get; set; } = double.PositiveInfinity;
        public double Thickness { get; set; }
        public string Material { get; set; } = string.Empty;
        public double SemiDiameter { get; set; }
        public SemiDiameterMode SemiDiameterMode { get; set; }
        public double ClearAperturePercent { get; set; } = 100.0;
        public double Conic { get; set; }
        public bool IsStop { get; set; }

        // Aperture obscuration / floating-aperture support
        public double InnerRadius { get; set; }
        public double ObscurationRadius { get; set; }
        public double FloatingApertureRadius { get; set; }

        // Even-asphere coefficients — null when surface is a plain Standard
        public double[]? AsphericCoefficients { get; set; }

        // Variable flags. Stock lenses are locked prescriptions so these
        // are always false / null in the catalog, but OptalixWriter
        // branches on them when re-exporting a user-loaded .lhlt and the
        // OSLO writer reads HasMarginalRaySolve for CALLBACK 1 emission.
        // Variable BOUNDS (min/max) are still omitted — none of the format
        // writers consume them.
        public bool CurvatureVariable { get; set; }
        public bool ThicknessVariable { get; set; }
        public bool ConicVariable { get; set; }
        public bool[]? AsphericVariable { get; set; }
        public bool HasMarginalRaySolve { get; set; }
    }

    public class LhltPickup
    {
        public int TargetSurfaceIndex { get; set; }
        public PickupParameter Parameter { get; set; }
        public int SourceSurfaceIndex { get; set; }
        public int SourceConfigurationIndex { get; set; } = -1;
        public double ScaleFactor { get; set; } = 1.0;
        public double Offset { get; set; }
    }

    public class LhltWavelength
    {
        public double Value { get; set; }
        public double Weight { get; set; } = 1.0;
        public bool IsPrimary { get; set; }
    }

    public class LhltField
    {
        public double Y { get; set; }
        public double Weight { get; set; } = 1.0;
    }
}
