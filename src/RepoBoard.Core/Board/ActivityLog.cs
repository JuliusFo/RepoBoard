using System.Collections.Immutable;

namespace RepoBoard.Core.Board;

public sealed record ActivityEntry(DateTimeOffset Time, string Repo, string Text, bool IsError, bool IsAuto);

/// <summary>What happened, newest first – above all what auto-sync did while nobody watched. Kept for the run only.</summary>
public sealed class ActivityLog
{
    public const int Capacity = 200;
    private ImmutableList<ActivityEntry> _entries = [];

    public IReadOnlyList<ActivityEntry> Entries => _entries;

    public void Add(ActivityEntry entry) =>
        ImmutableInterlocked.Update(ref _entries, list =>
        {
            var added = list.Insert(0, entry);
            return added.Count > Capacity ? added.RemoveAt(added.Count - 1) : added;
        });
}
