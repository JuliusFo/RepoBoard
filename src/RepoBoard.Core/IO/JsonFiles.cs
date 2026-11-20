using System.Text.Json;
using System.Text.Json.Serialization;

namespace RepoBoard.Core.IO;

/// <summary>
/// How RepoBoard's own JSON files are written: indented, camelCase, enums as text, nulls left out, umlauts and <c>+</c> as
/// they are (files for people to read, not HTML). One place, so the stores cannot drift apart.
/// </summary>
internal static class JsonFiles
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
