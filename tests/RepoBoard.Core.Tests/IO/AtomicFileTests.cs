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

    /// <summary>
    /// The first instance ends between the second's failed open and its look for the file (the file goes with the lock):
    /// the second took that for "the folder cannot be written" and started without the lock (code review R1, K4).
    /// </summary>
    [Fact]
    public async Task A_lock_let_go_while_another_tries_is_taken_or_held_never_lost()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "File locks are advisory outside Windows.");
        var paths = new AppPaths(_folder.Combine("data"));
        using var stop = new CancellationTokenSource();
        var failures = 0;
        void TakeAndLetGo(int processId)
        {
            try
            {
                if (InstanceLock.TryAcquire(paths, processId, out var held, out _))
                {
                    held.Dispose();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Interlocked.Increment(ref failures); // Program.Main started without the lock
            }
        }

        var first = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                TakeAndLetGo(1);
            }
        }, Ct);
        for (var i = 0; i < 3_000; i++)
        {
            TakeAndLetGo(2);
        }

        await stop.CancelAsync();
        await first;
        Assert.Equal(0, failures);
    }

    public void Dispose() => _folder.Dispose();
}
