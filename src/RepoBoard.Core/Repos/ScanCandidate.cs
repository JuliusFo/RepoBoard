namespace RepoBoard.Core.Repos;

/// <summary>What a repository's own configuration makes git run on RepoBoard's calls (review 0.4.0, A9).</summary>
public enum RepoRuns
{
    /// <summary><c>core.fsmonitor</c>: a program on every <c>git status</c>.</summary>
    FsMonitor,

    /// <summary><c>core.hooksPath</c>: hooks from another folder.</summary>
    HooksPath,

    /// <summary><c>core.sshCommand</c>: a program instead of ssh on fetch, pull and push.</summary>
    SshCommand,

    /// <summary>Hooks of its own in <c>.git/hooks</c> (not git's <c>*.sample</c>).</summary>
    Hooks,

    /// <summary><c>include.path</c>/<c>includeIf</c>: more configuration from another file, which may set any of the above.</summary>
    Includes,
}

/// <summary>
/// A repository a scan found, read without git – git would already run what its configuration names (review 0.4.0, A9).
/// </summary>
/// <param name="Path">The folder found (a repository or a linked worktree).</param>
/// <param name="Board">The folder that goes on the board: the repository itself, or the main worktree of a linked one.</param>
/// <param name="OutsideScan">The repository on the board lies outside the scanned folder (a linked worktree's main worktree).</param>
/// <param name="Runs">What its configuration makes git run.</param>
public sealed record ScanCandidate(string Path, string Board, bool OutsideScan, IReadOnlyList<RepoRuns> Runs)
{
    /// <summary>Ticked at first: only a repository inside the folder that runs nothing of its own.</summary>
    public bool Plain => !OutsideScan && Runs.Count == 0;
}
