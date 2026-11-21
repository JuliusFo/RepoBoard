using RepoBoard.Core.Git;

namespace RepoBoard.UI.State;

/// <summary>
/// The changed lines picked in an open diff for staging or unstaging (WP-25): a click picks or drops one line, Shift+click
/// picks every changed line from the last click to this one. Lines of one hunk at a time – a click in another hunk starts
/// over, since a patch carries one hunk.
/// </summary>
public sealed class LineSelection
{
    private readonly SortedSet<int> _lines = [];
    private int? _anchor;

    /// <summary>The hunk the picked lines are in; null with nothing picked.</summary>
    public int? Hunk { get; private set; }

    /// <summary>Changes with every pick, so a component can tell it must render again.</summary>
    public int Version { get; private set; }

    public IReadOnlySet<int> Lines => _lines;

    /// <summary>Only added and removed lines can be picked; context stays as it is.</summary>
    public static bool CanPick(DiffLine line) => line.Kind != DiffLineKind.Context;

    public bool IsPicked(int hunk, int line) => Hunk == hunk && _lines.Contains(line);

    /// <summary>How many lines are picked in <paramref name="hunk"/>.</summary>
    public int CountIn(int hunk) => Hunk == hunk ? _lines.Count : 0;

    public void Click(DiffHunk lines, int hunk, int line, bool shift)
    {
        // A range may end on a context line; a single click there picks nothing.
        var range = shift && Hunk == hunk && _anchor is not null;
        if (!range && !CanPick(lines.Lines[line]))
        {
            return;
        }

        if (Hunk != hunk)
        {
            _lines.Clear();
            _anchor = null;
            Hunk = hunk;
        }

        if (range && _anchor is { } from)
        {
            for (var i = Math.Min(from, line); i <= Math.Max(from, line); i++)
            {
                if (CanPick(lines.Lines[i]))
                {
                    _lines.Add(i);
                }
            }
        }
        else if (!_lines.Remove(line))
        {
            _lines.Add(line);
        }

        _anchor = line;
        if (_lines.Count == 0)
        {
            Hunk = null;
        }

        Version++;
    }

    public void Clear()
    {
        if (Hunk is null)
        {
            return;
        }

        _lines.Clear();
        _anchor = null;
        Hunk = null;
        Version++;
    }

    /// <summary>
    /// After the diff was read again (every new status): the picks stay if their hunk reads the same, otherwise they are
    /// dropped – their indexes would point at other lines.
    /// </summary>
    public void KeepFor(FileDiff? before, FileDiff after)
    {
        if (Hunk is not { } hunk)
        {
            return;
        }

        if (before is null || hunk >= before.Hunks.Count || hunk >= after.Hunks.Count || !Same(before.Hunks[hunk], after.Hunks[hunk]))
        {
            Clear();
        }
    }

    private static bool Same(DiffHunk a, DiffHunk b) =>
        (a.OldStart, a.OldCount, a.NewStart, a.NewCount) == (b.OldStart, b.OldCount, b.NewStart, b.NewCount)
        && a.Lines.Select(Key).SequenceEqual(b.Lines.Select(Key));

    private static (DiffLineKind, string, bool, bool) Key(DiffLine line) => (line.Kind, line.Text, line.EndsWithCr, line.NoNewlineAtEnd);
}
