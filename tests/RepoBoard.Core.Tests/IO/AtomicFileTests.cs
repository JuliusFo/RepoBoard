using RepoBoard.Core;
using RepoBoard.Core.IO;

namespace RepoBoard.Core.Tests.IO;

/// <summary>RepoBoard's own files: written all or nothing, one instance per data folder (review 0.4.0, C3).</summary>
public sealed class AtomicFileTests : IDisposable
{
    private readonly TestFolder _folder = new();

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_write_that_fails_leaves_the_file_as_it_was_and_no_temporary_file()
    {
        var file = _folder.Combine("config.json");
        await File.WriteAllTextAsync(file, "before", Ct);

        await Assert.ThrowsAsync<IOException>(() => AtomicFile.WriteAsync(file, async (stream, ct) =>
        {
            await stream.WriteAsync("half"u8.ToArray(), ct);
            throw new IOException("disk full");
        }, Ct));

        Assert.Equal("before", await File.ReadAllTextAsync(file, Ct));
        Assert.False(File.Exists(file + ".tmp"));
    }

    [Fact]
    public async Task A_cancelled_write_leaves_the_file_as_it_was()
    {
        var file = _folder.Combine("config.json");
        await File.WriteAllTextAsync(file, "before", Ct);
        using var cancel = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AtomicFile.WriteAsync(file, async (stream, ct) =>
        {
            await stream.WriteAsync("half"u8.ToArray(), ct);
            await cancel.CancelAsync();
            ct.ThrowIfCancellationRequested();
        }, cancel.Token));

        Assert.Equal("before", await File.ReadAllTextAsync(file, Ct));
        Assert.False(File.Exists(file + ".tmp"));
    }

    /// <summary>A second instance on the same data folder learns which process holds it (Windows only: elsewhere locks are advisory).</summary>
    [Fact]
    public void One_instance_per_data_folder()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "File locks are advisory outside Windows.");
        var paths = new AppPaths(_folder.Combine("data"));

        Assert.True(InstanceLock.TryAcquire(paths, 4711, out var held, out _));
        using (held)
        {
            Assert.False(InstanceLock.TryAcquire(paths, 4712, out var second, out var other));
            Assert.Null(second);
            Assert.Equal(4711, other);
        }

        Assert.True(InstanceLock.TryAcquire(paths, 4712, out var again, out _)); // let go when the first ends
        again.Dispose();
    }

    public void Dispose() => _folder.Dispose();
}
