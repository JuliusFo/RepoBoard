using RepoBoard.Core.Repos;
using RepoBoard.UI.Resources;

namespace RepoBoard.UI.State;

/// <summary>
/// Which repositories a scan adds (review 0.4.0, A9): a repository inside the scanned folder that runs nothing of its own
/// is ticked at first; one with hooks, an fsmonitor, an ssh command or included configuration – or whose repository lies
/// outside the folder – only when the user ticks it.
/// </summary>
public sealed class ScanSelection(IReadOnlyList<ScanCandidate> candidates)
{
    private readonly HashSet<ScanCandidate> _selected = [.. candidates.Where(c => c.Plain)];

    public IReadOnlyList<ScanCandidate> Candidates => candidates;

    public bool IsSelected(ScanCandidate candidate) => _selected.Contains(candidate);

    public void Toggle(ScanCandidate candidate)
    {
        if (!_selected.Remove(candidate))
        {
            _selected.Add(candidate);
        }
    }

    /// <summary>The ticked ones, in the order of the list.</summary>
    public IReadOnlyList<ScanCandidate> Selected => [.. candidates.Where(_selected.Contains)];

    /// <summary>What the list says about a repository that runs something of its own.</summary>
    public static string Text(RepoRuns runs) => runs switch
    {
        RepoRuns.FsMonitor => BoardText.Scan_Runs_FsMonitor,
        RepoRuns.HooksPath => BoardText.Scan_Runs_HooksPath,
        RepoRuns.SshCommand => BoardText.Scan_Runs_SshCommand,
        RepoRuns.Hooks => BoardText.Scan_Runs_Hooks,
        RepoRuns.Includes => BoardText.Scan_Runs_Includes,
        RepoRuns.GitDirOnShare => BoardText.Scan_Runs_GitDirOnShare,
        _ => BoardText.Scan_Runs_OtherSettings,
    };
}
