namespace RepoBoard.Core.Watch;

/// <summary>
/// Gathers a series of changes per key into one (WP-03): a key is due once nothing changed for <c>quiet</c>, or at the
/// latest <c>longest</c> after its first change – a build that writes for a minute still reads every few seconds. Times
/// are <see cref="TimeProvider.GetTimestamp"/> values (monotonic: a clock set back does not hold a series back), the two
/// spans in the same unit. Not thread-safe; the caller locks.
/// </summary>
internal sealed class ChangeDebouncer<TKey>(long quiet, long longest) where TKey : notnull
{
    private readonly Dictionary<TKey, (long First, long Last, long NotBefore)> _pending = [];

    public int Count => _pending.Count;

    public void Add(TKey key, long at) =>
        _pending[key] = _pending.TryGetValue(key, out var series)
            ? (series.First, Math.Max(at, series.Last), series.NotBefore)
            : (at, at, long.MinValue);

    /// <summary>
    /// A key whose read was skipped (its repository was busy): due again at <paramref name="notBefore"/>, keeping the time
    /// of its last change – a read that began after that change (the one at the end of the operation) makes it unneeded.
    /// Entered with the time of the retry instead, it read a second time after every operation (review 0.4.0, A14).
    /// </summary>
    public void Retry(TKey key, long last, long notBefore) =>
        _pending[key] = _pending.TryGetValue(key, out var series)
            ? (series.First, Math.Max(last, series.Last), Math.Max(notBefore, series.NotBefore))
            : (last, last, notBefore);

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

    private long Due((long First, long Last, long NotBefore) series) =>
        Math.Max(Math.Min(series.Last + quiet, series.First + longest), series.NotBefore);
}
