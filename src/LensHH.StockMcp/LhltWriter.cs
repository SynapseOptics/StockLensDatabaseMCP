using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LensHH.StockMcp
{
    // Serializes an LhltFile DTO back to .lhlt JSON. Required for the
    // export_lens lhlt+reversed combination — when reversed=true we can't
    // byte-copy from the catalog and have to write a modified DTO out.
    //
    // Options mirror LhltReader so a file written here round-trips cleanly
    // back through LhltReader, and the engine's own LhltReader will accept
    // it as well (the engine .lhlt format is plain System.Text.Json with
    // the same conventions: PascalCase, named-float literals, enum-as-string).
    public static class LhltWriter
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = null,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters = { new JsonStringEnumConverter() },
            WriteIndented = true,
            // Match the engine writer's behavior: suppress null arrays
            // (AsphericCoefficients / AsphericVariable / etc. when not set).
            // Doesn't affect bool false / int 0 — those still emit.
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public static void Write(LhltFile file, string filePath)
        {
            string json = JsonSerializer.Serialize(file, Options);
            File.WriteAllText(filePath, json);
        }
    }
}
