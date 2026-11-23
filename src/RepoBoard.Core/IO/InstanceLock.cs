using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace RepoBoard.Core.IO;

/// <summary>
/// One running RepoBoard per data folder (from FerretSharp WP-32): two instances would overwrite each other's config,
/// and both would fetch and pull the same repositories at once. The lock is a file held open
/// exclusively for the life of the process – the system lets go of it when the process ends, also after a crash. Its
/// process id goes into a file of its own (the lock file cannot be read while held), so a second start can bring the
/// running instance forward. Another data folder (<c>--data-dir</c>, tests) is another lock.
/// </summary>
public sealed class InstanceLock : IDisposable
{
    private readonly FileStream _file;

    private const int LockAttempts = 20;

    private InstanceLock(FileStream file) => _file = file;

    /// <summary>
    /// Takes the lock of the data folder and writes this process's id beside it; false if another process holds it, with
    /// that process's id if it could be read.
    /// </summary>
    /// <exception cref="IOException">The folder cannot be created or written for another reason than a running instance.</exception>
    public static bool TryAcquire(AppPaths paths, int processId, [NotNullWhen(true)] out InstanceLock? held, out int? otherProcessId)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Directory.CreateDirectory(paths.Root);
        held = null;
        otherProcessId = null;
        FileStream file;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                file = new FileStream(paths.InstanceLockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
                break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (File.Exists(paths.InstanceLockFile))
                {
                    otherProcessId = ReadProcessId(paths);
                    return false;
                }

                // Gone between the failed open and the look: the other instance just ended (the file goes with its lock, and
                // while Windows deletes it an open fails with "access denied"). Once more – before, this start took it for
                // an unwritable folder and ran without the lock (code review R1, reproduced).
                if (attempt == LockAttempts)
                {
                    throw;
                }

                Thread.Sleep(10);
            }
        }

        try
        {
            File.WriteAllText(paths.InstanceProcessFile, processId.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only bringing this instance forward from a second start fails; the lock itself holds.
        }

        held = new InstanceLock(file);
        return true;
    }

    private static int? ReadProcessId(AppPaths paths)
    {
        try
        {
            return int.TryParse(File.ReadAllText(paths.InstanceProcessFile).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose() => _file.Dispose();
}
