namespace RepoBoard.Core.Watch;

/// <summary>
/// Gathers a series of changes per key into one (WP-03): a key is due once nothing changed for <c>quiet</c>, or at the
/// latest <c>longest</c> after its first change – a build that writes for a minute still reads every few seconds. Not
/// thread-safe; the caller locks.
/// </summary>
internal sealed class ChangeDebouncer<TKey>(TimeSpan quiet, TimeSpan longest) where TKey : notnull
{
    private readonly Dictionary<TKey, (DateTimeOffset First, DateTimeOffset Last)> _pending = [];

    public int Count => _pending.Count;

    public void Add(TKey key, DateTimeOffset at) =>
        _pending[key] = _pending.TryGetValue(key, out var series) ? (series.First, at > series.Last ? at : series.Last) : (at, at);

    /// <summary>When the next key is due; null when nothing waits.</summary>
    public DateTimeOffset? NextDue => _pending.Count == 0 ? null : _pending.Values.Min(Due);

    /// <summary>Removes and returns the keys due at <paramref name="now"/>, each with the time of its last change.</summary>
    public IReadOnlyList<(TKey Key, DateTimeOffset Last)> TakeDue(DateTimeOffset now)
    {
        var due = _pending.Where(p => Due(p.Value) <= now).Select(p => (p.Key, p.Value.Last)).ToList();
        foreach (var (key, _) in due)
        {
            _pending.Remove(key);
        }

        return due;
    }

    private DateTimeOffset Due((DateTimeOffset First, DateTimeOffset Last) series)
    {
        var calm = series.Last + quiet;
        var latest = series.First + longest;
        return calm < latest ? calm : latest;
    }
}
