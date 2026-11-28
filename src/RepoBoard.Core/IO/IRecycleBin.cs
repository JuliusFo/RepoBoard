namespace RepoBoard.Core.IO;

/// <summary>
/// The recycle bin of the system (WP-06, ADR 0016): where a discarded new file goes instead of <c>git clean</c>, so it can
/// be restored. Only the host knows it (CLAUDE.md §4); implemented in the App.
/// </summary>
public interface IRecycleBin
{
    /// <summary>
    /// Moves the files (full paths) to the recycle bin; null if all went, else the reason. Never deletes a file for good
    /// without asking: where there is no recycle bin (a network share), the system asks the user.
    /// </summary>
    Task<string?> RecycleAsync(IReadOnlyList<string> files, CancellationToken ct);
}
