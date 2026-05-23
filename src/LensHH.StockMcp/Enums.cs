namespace LensHH.StockMcp
{
    // Enum definitions mirrored from LensHH.Core.Enums so the standalone
    // MCP can deserialize .lhlt files (which serialize these as strings)
    // without taking a dependency on the engine assembly. Values + names
    // MUST match the engine's definitions exactly so the JSON round-trip
    // is symmetric.
    //
    // Only the subset that LhltFile + the format writers actually
    // reference is included. Merit-function and configuration-editor
    // enums (OperandType, OperationCode, PickupParameter,
    // ConfigOperandType) are excluded for now — the P2 writers only
    // emit prescription data, not merit functions or zoom configs.

    public enum ApertureType
    {
        EPD = 0,
        FNumber = 1
    }

    public enum FieldType
    {
        ObjectAngle = 0,
        ObjectHeight = 1
    }

    public enum SurfaceType
    {
        Standard = 0,
        EvenAsphere = 1
    }

    public enum SemiDiameterMode
    {
        Auto = 0,
        Fixed = 1
    }

    public enum RayAimingMode
    {
        Off = 0,
        Real = 1,
        Robust = 2
    }

    // Pickup parameter — which surface property is slaved to another
    // surface's value. Stock lenses never carry pickups (locked
    // prescriptions), but a .lhlt loaded from a user file might. The
    // OsloWriter port branches on PickupParameter.Thickness, so the
    // standalone DTOs need this enum even if the catalog never uses it.
    // JSON serialization is by name (JsonStringEnumConverter), so
    // numeric values don't need to match the engine's enum exactly —
    // only the member names do.
    public enum PickupParameter
    {
        Radius = 0,
        Thickness = 1,
        Glass = 2,
        SemiDiameter = 3,
        Conic = 4
    }
}
