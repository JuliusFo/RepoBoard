using System.Collections.Immutable;

namespace RepoBoard.Core.Board;

/// <summary>What an entry reports – what a notification is decided on (ADR 0006), never the text.</summary>
public enum ActivityKind
{
    Info,
    Error,

    /// <summary>A pull or fast-forward moved the branch.</summary>
    Pulled,

    /// <summary>An auto-fetch found new commits on the upstream (auto-pull off).</summary>
    NewCommits,

    /// <summary>Auto-pull left new commits alone (diverged, merge running …).</summary>
    Skipped,
}

/// <param name="RepoPath">The repository the entry is about; null for the app itself (configuration, a folder scan).</param>
public sealed record ActivityEntry(DateTimeOffset Time, string Repo, string Text, ActivityKind Kind, bool IsAuto, string? RepoPath = null)
{
    public bool IsError => Kind == ActivityKind.Error;
}

/// <summary>What happened, newest first – above all what auto-sync did while nobody watched. Kept for the run only.</summary>
public sealed class ActivityLog
{
    public const int Capacity = 200;
    private ImmutableList<ActivityEntry> _entries = [];

    public IReadOnlyList<ActivityEntry> Entries => _entries;

    /// <summary>Each new entry, on the thread that added it (any).</summary>
    public event Action<ActivityEntry>? Added;

    public void Add(ActivityEntry entry)
    {
        ImmutableInterlocked.Update(ref _entries, list =>
        {
            var added = list.Insert(0, entry);
            return added.Count > Capacity ? added.RemoveAt(added.Count - 1) : added;
        });
        Added?.Invoke(entry);
    }
}
