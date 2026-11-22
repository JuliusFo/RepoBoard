using System.Text.Json;

namespace RepoBoard.Core.IO;

/// <summary>
/// Writes a JSON file so that a reader never sees half of it (<see cref="AtomicFile"/>): if writing fails (disk full,
/// cancelled, a value that cannot be serialized), the old file stays as it was.
/// </summary>
public static class AtomicJsonFile
{
    /// <summary>Writes <paramref name="value"/> to <paramref name="path"/>, creating its directory if needed.</summary>
    public static Task WriteAsync<T>(string path, T value, JsonSerializerOptions options, CancellationToken cancellationToken) =>
        AtomicFile.WriteAsync(path, (stream, ct) => JsonSerializer.SerializeAsync(stream, value, options, ct), cancellationToken);
}
