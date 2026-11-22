namespace RepoBoard.Core.Git;

/// <summary>
/// A commit message's subject and body, the way git splits them: the subject is the first paragraph with its lines
/// joined by a space (<c>%s</c>), the body what follows the first blank line. One rule for the history, the commit's
/// details and the activity log – they showed different subjects for a message whose first paragraph has two lines
/// (review 0.4.0, A39).
/// </summary>
public static class CommitMessage
{
    public static string Subject(string message)
    {
        var lines = Lines(message);
        var start = SkipBlank(lines, 0);
        return string.Join(' ', lines[start..EndOfParagraph(lines, start)].Select(l => l.Trim()));
    }

    public static string Body(string message)
    {
        var lines = Lines(message);
        var end = EndOfParagraph(lines, SkipBlank(lines, 0));
        return string.Join('\n', lines[SkipBlank(lines, end)..]).TrimEnd();
    }

    private static string[] Lines(string message) => message.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    private static int SkipBlank(string[] lines, int i)
    {
        while (i < lines.Length && lines[i].Trim().Length == 0)
        {
            i++;
        }

        return i;
    }

    private static int EndOfParagraph(string[] lines, int i)
    {
        while (i < lines.Length && lines[i].Trim().Length > 0)
        {
            i++;
        }

        return i;
    }
}
