using System.Collections.Generic;

namespace LensHH.StockMcp
{
    /// <summary>
    /// One row from the <c>stock_lenses</c> table. Pure data, no
    /// engine references. Used by the MCP tools to format query
    /// results and respond to get_lens_details.
    /// </summary>
    public sealed class StockLensRow
    {
        public string Vendor { get; set; } = "";
        public string PartNumber { get; set; } = "";
        public string Family { get; set; } = "";
        public string SystemName { get; set; } = "";
        public string Description { get; set; } = "";

        // Geometry + first-order
        public double? DiameterMm { get; set; }
        public double? CenterThicknessMm { get; set; }
        public double? TotalTrackMm { get; set; }
        public double? EflMm { get; set; }
        public double? BflMm { get; set; }
        public double? FflMm { get; set; }
        public double? Fnum { get; set; }
        public double? NaImage { get; set; }
        public double? EnpDiameterMm { get; set; }

        // Materials + coatings
        public string GlassCodesJson { get; set; } = "";
        public string GlassNamesJson { get; set; } = "";
        public string Coating { get; set; } = "";

        // Wavelengths
        public double? WavelengthNmPrimary { get; set; }
        public string WavelengthsNmJson { get; set; } = "";

        // Source files
        public string ZmxRelpath { get; set; } = "";
        public string LhltRelpath { get; set; } = "";

        // Build metadata
        public string ImportedAt { get; set; } = "";
        public string ImportStatus { get; set; } = "";
        public string ImportNotesJson { get; set; } = "";

        // Derived
        public int NElements { get; set; }
    }

    /// <summary>
    /// One row from the <c>lens_surfaces</c> table. Pure data.
    /// </summary>
    public sealed class StockSurfaceRow
    {
        public string Vendor { get; set; } = "";
        public string PartNumber { get; set; } = "";
        public int SurfaceIndex { get; set; }
        public double? RadiusMm { get; set; }
        public double? ThicknessMm { get; set; }
        public string GlassName { get; set; } = "";
        public string GlassCatalog { get; set; } = "";
        public double? Conic { get; set; }
        public string AsphericCoeffsJson { get; set; } = "";
        public double? ClearApertureMm { get; set; }
        public string SurfaceType { get; set; } = "";
        public bool IsStop { get; set; }
    }

    /// <summary>
    /// Filter set for <c>search_stock</c>. All fields optional; null /
    /// empty means "don't filter on this column". Matches the columns
    /// exposed in the SQLite <c>stock_lenses</c> table plus the
    /// derived <c>n_elements</c> column.
    /// </summary>
    public sealed class StockLensQuery
    {
        public double? EflMin { get; set; }
        public double? EflMax { get; set; }
        public double? DiameterMin { get; set; }
        public double? DiameterMax { get; set; }
        public double? BflMin { get; set; }
        public double? BflMax { get; set; }
        public double? FnumMin { get; set; }
        public double? FnumMax { get; set; }
        public string? Vendor { get; set; }
        public string? FamilyLike { get; set; }
        public string? GlassLike { get; set; }
        public int? NElements { get; set; }
        public int Limit { get; set; } = 30;
    }
}
