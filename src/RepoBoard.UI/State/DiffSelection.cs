using RepoBoard.Core.Git;

namespace RepoBoard.UI.State;

/// <summary>The file whose diff the opened row shows: its path and which side of the index.</summary>
public sealed record DiffTarget(string Path, bool Staged);

/// <summary>Keeps the diff of the opened row on its file while the status changes underneath (WP-05).</summary>
public static class DiffSelection
{
    /// <summary>
    /// The change <paramref name="target"/> points at in <paramref name="status"/>, and the side to show. Staging or
    /// unstaging moves a file to the other side – the diff follows it there. A file that is in neither list any more
    /// (committed, reverted) ends the selection: null.
    /// </summary>
    public static (FileChange Change, bool Staged)? Resolve(DiffTarget? target, RepoStatus? status)
    {
        if (target is null || status is null)
        {
            return null;
        }

        var same = status.Changes.FirstOrDefault(c => c.Path == target.Path && (target.Staged ? c.IsStaged : c.IsUnstaged));
        if (same is not null)
        {
            return (same, target.Staged);
        }

        var other = status.Changes.FirstOrDefault(c => c.Path == target.Path && (target.Staged ? c.IsUnstaged : c.IsStaged));
        return other is not null ? (other, !target.Staged) : null;
    }
}
