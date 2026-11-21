namespace RepoBoard.Core.Git;

/// <summary>
/// Which words of a changed line differ from the line it replaces: the longest common subsequence of tokens (words,
/// whitespace runs, single other characters). Only a highlight on top of git's line diff – git decides which lines changed.
/// </summary>
public static class DiffWords
{
    /// <summary>Above this many token pairs the comparison is skipped (the whole line stays the change).</summary>
    private const int MaxCells = 250_000;

    /// <returns>The changed spans of each side; empty for a side if the whole line or nothing in it changed.</returns>
    public static (IReadOnlyList<DiffSpan> Old, IReadOnlyList<DiffSpan> New) Compare(string oldText, string newText)
    {
        var unlimited = long.MaxValue;
        return Compare(oldText, newText, ref unlimited);
    }

    /// <inheritdoc cref="Compare(string, string)"/>
    /// <param name="budget">Token pairs left for the file: the comparison is skipped if it needs more, else they are taken from it.</param>
    public static (IReadOnlyList<DiffSpan> Old, IReadOnlyList<DiffSpan> New) Compare(string oldText, string newText, ref long budget)
    {
        var a = Tokens(oldText);
        var b = Tokens(newText);
        var cells = (long)a.Count * b.Count;
        if (a.Count == 0 || b.Count == 0 || cells > MaxCells || cells > budget)
        {
            return ([], []);
        }

        budget -= cells;

        // lengths[i, j]: common subsequence length of a[i..] and b[j..].
        var lengths = new int[a.Count + 1, b.Count + 1];
        for (var i = a.Count - 1; i >= 0; i--)
        {
            for (var j = b.Count - 1; j >= 0; j--)
            {
                lengths[i, j] = Same(oldText, a[i], newText, b[j])
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        var inOld = new bool[a.Count];
        var inNew = new bool[b.Count];
        for (int i = 0, j = 0; i < a.Count && j < b.Count;)
        {
            if (Same(oldText, a[i], newText, b[j]))
            {
                inOld[i++] = true;
                inNew[j++] = true;
            }
            else if (lengths[i + 1, j] >= lengths[i, j + 1])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return (Spans(a, inOld), Spans(b, inNew));
    }

    private static bool Same(string x, DiffSpan tx, string y, DiffSpan ty) =>
        tx.Length == ty.Length && x.AsSpan(tx.Start, tx.Length).SequenceEqual(y.AsSpan(ty.Start, ty.Length));

    private static List<DiffSpan> Tokens(string text)
    {
        var tokens = new List<DiffSpan>();
        var i = 0;
        while (i < text.Length)
        {
            var start = i;
            if (char.IsLetterOrDigit(text[i]) || text[i] == '_')
            {
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                {
                    i++;
                }
            }
            else if (char.IsWhiteSpace(text[i]))
            {
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                {
                    i++;
                }
            }
            else
            {
                i++;
            }

            tokens.Add(new DiffSpan(start, i - start));
        }

        return tokens;
    }

    /// <summary>The tokens outside the common subsequence, adjacent ones merged; empty if none or all of them changed.</summary>
    private static List<DiffSpan> Spans(List<DiffSpan> tokens, bool[] common)
    {
        var spans = new List<DiffSpan>();
        var changed = 0;
        for (var t = 0; t < tokens.Count; t++)
        {
            if (common[t])
            {
                continue;
            }

            changed++;
            if (spans.Count > 0 && spans[^1].Start + spans[^1].Length == tokens[t].Start)
            {
                spans[^1] = spans[^1] with { Length = spans[^1].Length + tokens[t].Length };
            }
            else
            {
                spans.Add(tokens[t]);
            }
        }

        return changed == tokens.Count ? [] : spans;
    }
}
