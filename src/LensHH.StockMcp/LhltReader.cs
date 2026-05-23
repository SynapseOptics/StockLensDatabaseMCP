using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LensHH.StockMcp
{
    // Pure-DTO .lhlt JSON deserializer. No engine dependency.
    //
    // The LensHH-LT .lhlt format is just System.Text.Json output of
    // LhltFile with these conventions:
    //   - PropertyNamingPolicy = null (PascalCase preserved as-is)
    //   - JsonStringEnumConverter (enums emit as strings)
    //   - NumberHandling.AllowNamedFloatingPointLiterals (so
    //     "Infinity" / "-Infinity" / "NaN" round-trip cleanly —
    //     critical for OBJ surfaces with infinite thickness)
    //
    // The reader is forgiving of extra properties (merit function,
    // configuration editor, variable bounds, etc.) — System.Text.Json
    // ignores them by default. This lets the standalone MCP read
    // .lhlt files written by the full LensHH-LT engine even though
    // the standalone DTOs only model the prescription subset.

    public static class LhltReader
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = null,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters = { new JsonStringEnumConverter() },
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static LhltFile Read(string filePath)
        {
            string json = File.ReadAllText(filePath);
            var file = JsonSerializer.Deserialize<LhltFile>(json, Options)
                       ?? throw new InvalidDataException(
                           $"Failed to deserialize .lhlt JSON from {filePath}.");
            return file;
        }
    }
}
