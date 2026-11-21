namespace RepoBoard.Core.Watch;

/// <summary>
/// Gathers a series of changes per key into one (WP-03): a key is due once nothing changed for <c>quiet</c>, or at the
/// latest <c>longest</c> after its first change – a build that writes for a minute still reads every few seconds. Times
/// are <see cref="TimeProvider.GetTimestamp"/> values (monotonic: a clock set back does not hold a series back), the two
/// spans in the same unit. Not thread-safe; the caller locks.
/// </summary>
internal sealed class ChangeDebouncer<TKey>(long quiet, long longest) where TKey : notnull
{
    private readonly Dictionary<TKey, (long First, long Last)> _pending = [];

    public int Count => _pending.Count;

    public void Add(TKey key, long at) =>
        _pending[key] = _pending.TryGetValue(key, out var series) ? (series.First, Math.Max(at, series.Last)) : (at, at);

    /// <summary>When the next key is due; null when nothing waits.</summary>
    public long? NextDue => _pending.Count == 0 ? null : _pending.Values.Min(Due);

    /// <summary>Removes and returns the keys due at <paramref name="now"/>, each with the time of its last change.</summary>
    public IReadOnlyList<(TKey Key, long Last)> TakeDue(long now)
    {
        var due = _pending.Where(p => Due(p.Value) <= now).Select(p => (p.Key, p.Value.Last)).ToList();
        foreach (var (key, _) in due)
        {
            _pending.Remove(key);
        }

        return due;
    }

    private long Due((long First, long Last) series) => Math.Min(series.Last + quiet, series.First + longest);
}
