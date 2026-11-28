using System.Runtime.InteropServices;
using RepoBoard.Core.IO;
using RepoBoard.Core.Resources;

namespace RepoBoard.App.Services;

/// <summary>
/// The Windows recycle bin (WP-06, ADR 0016) through <c>SHFileOperation</c> with <c>FOF_ALLOWUNDO</c>: a discarded new file
/// can be restored from there. No confirmation, no progress or error window – RepoBoard asked already and reports failures
/// itself – but <c>FOF_WANTNUKEWARNING</c>: where a file cannot be recycled (a network share, a drive without a recycle
/// bin) Windows asks before it deletes it for good. On a thread of its own in a single-threaded apartment, as the shell
/// expects; never on the UI thread.
/// </summary>
internal sealed class ShellRecycleBin : IRecycleBin
{
    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;
    private const ushort FOF_WANTNUKEWARNING = 0x4000;

    public Task<string?> RecycleAsync(IReadOnlyList<string> files, CancellationToken ct)
    {
        if (files.Count == 0)
        {
            return Task.FromResult<string?>(null);
        }

        var done = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                done.SetResult(Recycle(files));
            }
            catch (Exception ex)
            {
                done.SetResult(ex.Message);
            }
        })
        {
            IsBackground = true,
            Name = "RecycleBin",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    private static string? Recycle(IReadOnlyList<string> files)
    {
        var operation = new ShFileOperation
        {
            Function = FO_DELETE,
            // A list of full paths, each ended by NUL, the list by another.
            From = string.Concat(files.Select(f => f + '\0')) + '\0',
            Flags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING,
        };
        var result = SHFileOperationW(ref operation);
        return result != 0 ? TextFormat.Format(SyncText.Recycle_Failed, $"0x{result:X}")
            : operation.AnyOperationsAborted ? SyncText.Recycle_Cancelled
            : null;
    }

    /// <summary><c>SHFILEOPSTRUCTW</c> (x64 layout).</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOperation
    {
        public IntPtr Window;
        public uint Function;
        public string From;
        public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool AnyOperationsAborted;
        public IntPtr NameMappings;
        public string? ProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref ShFileOperation operation);
}
