using System.Text.Json;
using System.Text.Unicode;
using RepoBoard.Core.Resources;

namespace RepoBoard.Core.IO;

/// <summary>What reading one of RepoBoard's own JSON files found (<see cref="VersionedJsonFile.ReadAsync{T}"/>).</summary>
public enum JsonFileState
{
    /// <summary>There is no file.</summary>
    Missing,

    /// <summary>Read, and in a version this RepoBoard knows.</summary>
    Ok,

    /// <summary>JSON, but no document in it (<c>null</c>).</summary>
    Empty,

    /// <summary>No JSON, or a shape that does not fit (a value of the wrong type).</summary>
    Broken,

    /// <summary>The file could not be read (access, locked by a virus scanner or a backup).</summary>
    Unreadable,

    /// <summary>Written by a newer RepoBoard (its <c>version</c> is higher): not read, since it may hold shapes this one does not know.</summary>
    Newer,
}

/// <summary>A read of a versioned JSON file: its state, the document (only <see cref="JsonFileState.Ok"/>) and why not.</summary>
/// <param name="Version">The <c>version</c> in the file; null if it has none or it was not read.</param>
/// <param name="Error">What went wrong for <see cref="JsonFileState.Broken"/> and <see cref="JsonFileState.Unreadable"/>.</param>
public sealed record JsonFileRead<T>(JsonFileState State, T? Document, int? Version, Exception? Error = null) where T : class;

/// <summary>
/// Reading RepoBoard's own versioned JSON files one way – each store decides explicitly what a state means for it. Taken
/// from FerretSharp (R5b, review 3.31.0 B2: eight copies with six rules; a newer file with an unknown shape was taken
/// for a broken one and overwritten, and a hand edit could crash the start).
/// </summary>
public static class VersionedJsonFile
{
    private static ReadOnlySpan<byte> Bom => [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// Reads <paramref name="path"/>. The version first: above <paramref name="currentVersion"/> the file is
    /// <see cref="JsonFileState.Newer"/> without deserializing it. A null document is <see cref="JsonFileState.Empty"/>; any
    /// other goes through <paramref name="normalize"/> (a file edited by hand made sane) and is <see cref="JsonFileState.Ok"/>.
    /// </summary>
    public static async Task<JsonFileRead<T>> ReadAsync<T>(string path, int currentVersion, JsonSerializerOptions options, CancellationToken cancellationToken,
        Func<T, T>? normalize = null)
        where T : class
    {
        if (!File.Exists(path))
        {
            return new(JsonFileState.Missing, null, null);
        }

        int? version = null;
        ReadOnlyMemory<byte> utf8 = default;
        try
        {
            // Bytes, not text: decoding to text put U+FFFD in place of bytes that are no UTF-8 (a file saved as ANSI by hand),
            // and the next save kept it (FerretSharp fix/robustness). Such a file is broken.
            utf8 = await File.ReadAllBytesAsync(path, cancellationToken);
            if (utf8.Span.StartsWith(Bom))
            {
                utf8 = utf8[Bom.Length..];
            }

            if (!Utf8.IsValid(utf8.Span))
            {
                throw new JsonException(CoreText.FileNotUtf8);
            }

            using var json = JsonDocument.Parse(utf8);
            var root = json.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("version", out var number) && number.TryGetInt32(out var found))
            {
                version = found;
                if (found > currentVersion)
                {
                    return new(JsonFileState.Newer, null, found);
                }
            }

            var document = root.Deserialize<T>(options);
            return document is null
                ? new(JsonFileState.Empty, null, version)
                : new(JsonFileState.Ok, normalize is null ? document : normalize(document), version);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // NotSupportedException: a shape System.Text.Json cannot bind (edited by hand).
            return new(JsonFileState.Broken, null, version, ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(JsonFileState.Unreadable, null, version, ex);
        }
    }

    /// <summary>
    /// Copies a file a store could not use next to it (<c>config.json.bad-20261011-153000</c>) before it is replaced –
    /// an identical copy kept earlier is reused (every start with the same file would add one). The path of the copy.
    /// </summary>
    /// <exception cref="IOException">The file could not be copied.</exception>
    /// <exception cref="UnauthorizedAccessException">The file could not be copied.</exception>
    public static string KeepCopy(string path, DateTimeOffset now)
    {
        var content = File.ReadAllBytes(path);
        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        if (Directory.EnumerateFiles(folder, Path.GetFileName(path) + ".bad-*").FirstOrDefault(f => File.ReadAllBytes(f).AsSpan().SequenceEqual(content)) is { } kept)
        {
            return kept;
        }

        var copy = $"{path}.bad-{now:yyyyMMdd-HHmmss}";
        File.Copy(path, copy, overwrite: false);
        return copy;
    }
}
