using System.Globalization;
using System.Text.RegularExpressions;

namespace RepoBoard.Core.Git;

public enum DiffLineKind
{
    Context,
    Added,
    Removed,
}

/// <summary>The changed part of a line, against the line it replaces (word highlighting).</summary>
public readonly record struct DiffSpan(int Start, int Length);

/// <summary>One line of a hunk.</summary>
/// <param name="Text">Without the leading <c>+</c>/<c>-</c>/space and without a trailing CR.</param>
/// <param name="EndsWithCr">The line ended with CR in git's output: a Windows line ending git does not convert.</param>
/// <param name="NoNewlineAtEnd">git's "\ No newline at end of file" follows this line.</param>
/// <param name="Changed">The changed parts of a removed or added line paired with its counterpart; empty if the whole line changed.</param>
public sealed record DiffLine(DiffLineKind Kind, int? OldNumber, int? NewNumber, string Text, bool EndsWithCr, bool NoNewlineAtEnd,
    IReadOnlyList<DiffSpan> Changed);

/// <param name="Section">What git writes after the second <c>@@</c>: the function or heading the hunk is in.</param>
public sealed record DiffHunk(int OldStart, int OldCount, int NewStart, int NewCount, string Section, IReadOnlyList<DiffLine> Lines);

/// <summary>
/// The diff of one file as git sees it (ADR 0008): parsed from <c>git diff</c>, so line endings, <c>.gitattributes</c>,
/// binary detection and diff drivers behave as in the terminal.
/// </summary>
/// <param name="IsTruncated">Only the first <c>maxLines</c> lines were kept.</param>
/// <param name="IsTooLarge">Not read at all: the file is above <see cref="MaxFileBytes"/>.</param>
/// <param name="NewFileMode">The file is new on the diff's new side, with this mode (<c>100644</c>); a staging patch creates it (WP-25).</param>
/// <param name="DeletedFileMode">The file is gone on the diff's new side; it had this mode.</param>
public sealed partial record FileDiff(IReadOnlyList<DiffHunk> Hunks, bool IsBinary, bool IsTruncated, bool IsTooLarge = false,
    string? NewFileMode = null, string? DeletedFileMode = null)
{
    public const int MaxLines = 5000;

    /// <summary>git reads a file as a whole for its diff; above this size (in the worktree) the diff is not read.</summary>
    public const long MaxFileBytes = 5 * 1024 * 1024;

    /// <summary>
    /// Token pairs the word highlighting of one file may compare in all (<see cref="DiffWords"/>); beyond them the rest of
    /// the file keeps whole-line colours, so a reformatted file of long lines does not cost seconds (review WP-05).
    /// </summary>
    public const long MaxWordCells = 2_000_000;

    public static readonly FileDiff TooLarge = new([], false, false, IsTooLarge: true);

    /// <summary>No text change to show: only the file mode changed, or nothing at all.</summary>
    public bool HasNoTextChanges => !IsBinary && !IsTooLarge && Hunks.Count == 0;

    [GeneratedRegex(@"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@ ?(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex HunkHeader();

    [GeneratedRegex(@"^(new|deleted) file mode ([0-7]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex FileMode();

    /// <summary>
    /// Parses the output of <c>git diff</c> for one file. The lines of a hunk are counted by its header, so an empty context
    /// line (<c>diff.suppressBlankEmpty</c>) is read as one; a removed block followed by an added block is paired line by
    /// line for word highlighting (<see cref="DiffWords"/>).
    /// </summary>
    public static FileDiff Parse(string output, int maxLines = MaxLines)
    {
        var lines = new Lines(output);
        var hunks = new List<DiffHunk>();
        var wordCells = MaxWordCells;
        var binary = false;
        string? newMode = null, deletedMode = null;
        var kept = 0;
        var truncated = false;
        while (!truncated && lines.Next() is { } line)
        {
            var header = HunkHeader().Match(line.TrimEnd('\r'));
            if (!header.Success)
            {
                // Outside a hunk: the lines of git's file header.
                binary |= line.StartsWith("Binary files ", StringComparison.Ordinal) || line.StartsWith("GIT binary patch", StringComparison.Ordinal);
                if (FileMode().Match(line.TrimEnd('\r')) is { Success: true } mode)
                {
                    if (mode.Groups[1].Value == "new")
                    {
                        newMode = mode.Groups[2].Value;
                    }
                    else
                    {
                        deletedMode = mode.Groups[2].Value;
                    }
                }

                continue;
            }

            int Number(int group) => header.Groups[group].Success ? int.Parse(header.Groups[group].Value, CultureInfo.InvariantCulture) : 1;
            var oldStart = Number(1);
            var oldCount = Number(2);
            var newStart = Number(3);
            var newCount = Number(4);
            var section = header.Groups[5].Value;

            var hunkLines = new List<DiffLine>();
            int oldNo = oldStart, newNo = newStart, oldLeft = oldCount, newLeft = newCount;
            while (lines.Peek() is { } raw && (oldLeft > 0 || newLeft > 0 || raw.StartsWith('\\')))
            {
                lines.Next();
                if (raw.StartsWith('\\'))
                {
                    if (hunkLines.Count > 0)
                    {
                        hunkLines[^1] = hunkLines[^1] with { NoNewlineAtEnd = true };
                    }

                    continue;
                }

                if (kept == maxLines)
                {
                    truncated = true;
                    break;
                }

                var kind = raw.Length == 0 ? DiffLineKind.Context : raw[0] switch
                {
                    '+' => DiffLineKind.Added,
                    '-' => DiffLineKind.Removed,
                    _ => DiffLineKind.Context,
                };
                var text = raw.Length == 0 ? "" : raw[1..];
                var cr = text.EndsWith('\r');
                if (cr)
                {
                    text = text[..^1];
                }

                hunkLines.Add(kind switch
                {
                    DiffLineKind.Added => new DiffLine(kind, null, newNo++, text, cr, false, []),
                    DiffLineKind.Removed => new DiffLine(kind, oldNo++, null, text, cr, false, []),
                    _ => new DiffLine(kind, oldNo++, newNo++, text, cr, false, []),
                });
                oldLeft -= kind == DiffLineKind.Added ? 0 : 1;
                newLeft -= kind == DiffLineKind.Removed ? 0 : 1;
                kept++;
            }

            if (hunkLines.Count > 0)
            {
                hunks.Add(new DiffHunk(oldStart, oldCount, newStart, newCount, section, PairChanges(hunkLines, ref wordCells)));
            }
        }

        return new FileDiff(hunks, binary, truncated, NewFileMode: newMode, DeletedFileMode: deletedMode);
    }

    /// <summary>Removed lines followed by added lines replace each other: the n-th removed one pairs with the n-th added one.</summary>
    private static List<DiffLine> PairChanges(List<DiffLine> lines, ref long wordCells)
    {
        var i = 0;
        while (i < lines.Count)
        {
            if (lines[i].Kind != DiffLineKind.Removed)
            {
                i++;
                continue;
            }

            var removedStart = i;
            while (i < lines.Count && lines[i].Kind == DiffLineKind.Removed)
            {
                i++;
            }

            var addedStart = i;
            while (i < lines.Count && lines[i].Kind == DiffLineKind.Added)
            {
                i++;
            }

            var pairs = Math.Min(addedStart - removedStart, i - addedStart);
            for (var p = 0; p < pairs; p++)
            {
                var (removed, added) = DiffWords.Compare(lines[removedStart + p].Text, lines[addedStart + p].Text, ref wordCells);
                lines[removedStart + p] = lines[removedStart + p] with { Changed = removed };
                lines[addedStart + p] = lines[addedStart + p] with { Changed = added };
            }
        }

        return lines;
    }

    /// <summary>
    /// git's output one line at a time instead of splitting all of it first: parsing stops at <c>maxLines</c>, and a large
    /// diff does not cost a second copy of itself (review WP-05).
    /// </summary>
    private sealed class Lines(string text)
    {
        private int _at;

        public string? Peek() => _at > text.Length ? null : Read(out _);

        public string? Next()
        {
            if (_at > text.Length)
            {
                return null;
            }

            var line = Read(out var next);
            _at = next;
            return line;
        }

        private string Read(out int next)
        {
            var end = text.IndexOf('\n', _at);
            if (end < 0)
            {
                end = text.Length;
            }

            next = end + 1;
            return text[_at..end];
        }
    }
}
