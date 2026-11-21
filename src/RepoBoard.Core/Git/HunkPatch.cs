using System.Globalization;
using System.Text;

namespace RepoBoard.Core.Git;

public enum HunkPatchKind
{
    /// <summary>A patch for <c>git apply --cached</c> (<see cref="HunkPatch.Text"/>).</summary>
    Patch,

    /// <summary>The selection is every change of the file: stage or unstage the file as a whole.</summary>
    WholeFile,

    /// <summary>No change selected.</summary>
    Nothing,

    /// <summary>The hunk is cut off at the diff's line limit: its patch would leave out the rest.</summary>
    Incomplete,

    /// <summary>The text git gave holds U+FFFD – the file is not UTF-8, and a patch would write replacement characters.</summary>
    NotUtf8,

    /// <summary>The selection splits the last line of a file without a final newline from what follows it.</summary>
    SplitsLastLine,
}

/// <summary>
/// Part of one hunk as a patch for the index (WP-25, ADR 0012). Staging (<c>git apply --cached</c>) applies the patch to
/// the index, the diff's old side: a removed line left out stays as context, an added line left out is dropped. Unstaging
/// (<c>--cached --reverse</c>) applies it backwards to the index, now the diff's new side: an added line left out stays as
/// context, a removed one is dropped. Either way git checks every context line against the index and applies all or
/// nothing, so a patch from a diff that is no longer current fails instead of changing the wrong lines.
/// </summary>
public sealed record HunkPatch(HunkPatchKind Kind, string Text = "", bool HasContext = true)
{
    private const string NoNewline = "\\ No newline at end of file\n";

    /// <param name="path">The file's path in the repository (after a rename the new one: the index has only that).</param>
    /// <param name="hunk">Index of the hunk in <paramref name="diff"/>.</param>
    /// <param name="lines">Indexes of the selected lines in the hunk; null selects all of its changes.</param>
    /// <param name="reverse">Unstaging: the patch is applied backwards to the index (the diff's staged side).</param>
    public static HunkPatch Build(string path, FileDiff diff, int hunk, IReadOnlySet<int>? lines, bool reverse)
    {
        var source = diff.Hunks[hunk];
        if (diff.IsTruncated && hunk == diff.Hunks.Count - 1)
        {
            return new HunkPatch(HunkPatchKind.Incomplete);
        }

        bool Selected(int i) => source.Lines[i].Kind != DiffLineKind.Context && (lines is null || lines.Contains(i));

        var picked = Enumerable.Range(0, source.Lines.Count).Count(Selected);
        if (picked == 0)
        {
            return new HunkPatch(HunkPatchKind.Nothing);
        }

        if (!diff.IsTruncated && diff.Hunks.Count == 1 && picked == source.Lines.Count(l => l.Kind != DiffLineKind.Context))
        {
            // Every change of the file: the file-level command also gets new, deleted and renamed files and the mode right.
            return new HunkPatch(HunkPatchKind.WholeFile);
        }

        if (source.Lines.Any(l => l.Text.Contains('�', StringComparison.Ordinal)))
        {
            return new HunkPatch(HunkPatchKind.NotUtf8);
        }

        // The lines of the patch: sign and line. A change left out becomes context on the side the patch applies to.
        var entries = new List<(char Sign, DiffLine Line)>();
        for (var i = 0; i < source.Lines.Count; i++)
        {
            var line = source.Lines[i];
            var sign = line.Kind switch
            {
                DiffLineKind.Context => ' ',
                DiffLineKind.Removed => Selected(i) ? '-' : reverse ? '\0' : ' ',
                _ => Selected(i) ? '+' : reverse ? ' ' : '\0',
            };
            if (sign != '\0')
            {
                entries.Add((sign, line));
            }
        }

        SplitLastLine(entries);
        if (!LastLineOnlyAtEnd(entries, '-') || !LastLineOnlyAtEnd(entries, '+'))
        {
            return new HunkPatch(HunkPatchKind.SplitsLastLine);
        }

        var oldCount = entries.Count(e => e.Sign != '+');
        var newCount = entries.Count(e => e.Sign != '-');

        // Only this hunk changes, so the lines before it are the same on both sides: both start where the side the patch
        // applies to starts. A side without lines names the line before it (unified diff: "-0,0" for an empty side).
        var (anchorStart, anchorCount) = reverse ? (source.NewStart, source.NewCount) : (source.OldStart, source.OldCount);
        var first = anchorCount == 0 ? anchorStart + 1 : anchorStart;
        int Start(int count) => count == 0 ? first - 1 : first;

        var creates = !reverse && diff.NewFileMode is not null;
        var deletes = reverse && diff.DeletedFileMode is not null;
        var text = new StringBuilder();
        text.Append("diff --git ").Append(Quote("a/" + path)).Append(' ').Append(Quote("b/" + path)).Append('\n');
        if (creates)
        {
            text.Append("new file mode ").Append(diff.NewFileMode).Append('\n');
        }

        if (deletes)
        {
            text.Append("deleted file mode ").Append(diff.DeletedFileMode).Append('\n');
        }

        text.Append("--- ").Append(creates ? "/dev/null" : Quote("a/" + path)).Append('\n');
        text.Append("+++ ").Append(deletes ? "/dev/null" : Quote("b/" + path)).Append('\n');
        text.Append(CultureInfo.InvariantCulture, $"@@ -{Start(oldCount)},{oldCount} +{Start(newCount)},{newCount} @@\n");
        foreach (var (sign, line) in entries)
        {
            text.Append(sign).Append(line.Text);
            if (line.EndsWithCr)
            {
                text.Append('\r');
            }

            text.Append('\n');
            if (line.NoNewlineAtEnd)
            {
                text.Append(NoNewline);
            }
        }

        return new HunkPatch(HunkPatchKind.Patch, text.ToString(), entries.Any(e => e.Sign == ' '));
    }

    /// <summary>
    /// A last line without a newline that stays as context while selected lines follow it gets a newline: it is removed
    /// and added again with one (old "a" without newline, "+b" selected: "a\nb").
    /// </summary>
    private static void SplitLastLine(List<(char Sign, DiffLine Line)> entries)
    {
        for (var i = 0; i < entries.Count - 1; i++)
        {
            if (entries[i] is (' ', { NoNewlineAtEnd: true } line))
            {
                entries[i] = ('-', line);
                entries.Insert(i + 1, ('+', line with { NoNewlineAtEnd = false }));
                i++;
            }
        }
    }

    /// <summary>The line marked "no newline at end of file" must be the last one of its side (old: context and "-").</summary>
    private static bool LastLineOnlyAtEnd(List<(char Sign, DiffLine Line)> entries, char otherSide)
    {
        var side = entries.Where(e => e.Sign != otherSide).ToList();
        return side.SkipLast(1).All(e => !e.Line.NoNewlineAtEnd);
    }

    /// <summary>
    /// A path as git writes it in a patch: quoted with C escapes when it has a quote, a backslash, a control character or
    /// leading/trailing spaces; otherwise as is (also with spaces and umlauts, which <c>git apply</c> reads unquoted).
    /// </summary>
    internal static string Quote(string path)
    {
        var plain = path.Trim() == path && !path.Any(c => c is '"' or '\\' || char.IsControl(c));
        if (plain)
        {
            return path;
        }

        var text = new StringBuilder("\"");
        foreach (var b in Encoding.UTF8.GetBytes(path))
        {
            text.Append(b switch
            {
                (byte)'"' => "\\\"",
                (byte)'\\' => "\\\\",
                (byte)'\t' => "\\t",
                (byte)'\n' => "\\n",
                (byte)'\r' => "\\r",
                // Non-ASCII too: git reads a quoted name as bytes.
                < 0x20 or >= 0x7f => "\\" + Convert.ToString(b, 8).PadLeft(3, '0'),
                _ => null,
            } ?? ((char)b).ToString());
        }

        return text.Append('"').ToString();
    }
}
