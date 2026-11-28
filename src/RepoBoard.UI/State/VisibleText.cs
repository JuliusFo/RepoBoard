using System.Globalization;
using System.Text;

namespace RepoBoard.UI.State;

/// <summary>
/// Text from a repository as it is, not as it looks: the browser lets Unicode's direction controls (U+202A–U+202E,
/// U+2066–U+2069, the marks U+200E/U+200F/U+061C) reorder a line, so a diff or commit message could show other code than
/// git applies ("Trojan Source", CVE-2021-42574; reproduced in the WebView's engine, review 0.4.0, A24). They are shown
/// as <c>⟨U+202E⟩</c> – only on the screen: the diff a patch is built from keeps them.
/// </summary>
public static class VisibleText
{
    public static string Of(string text)
    {
        if (!text.Any(IsDirectionControl))
        {
            return text;
        }

        var visible = new StringBuilder(text.Length + 16);
        foreach (var c in text)
        {
            if (IsDirectionControl(c))
            {
                visible.Append("⟨U+").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture)).Append('⟩');
            }
            else
            {
                visible.Append(c);
            }
        }

        return visible.ToString();
    }

    private static bool IsDirectionControl(char c) => c is >= '‪' and <= '‮' or >= '⁦' and <= '⁩' or '‎' or '‏' or '؜';
}
