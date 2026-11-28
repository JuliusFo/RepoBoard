namespace RepoBoard.Core.Resources;

/// <summary>A text cut to a length Windows takes (a notification's title and text, the tray's tooltip), ending in "…".</summary>
public static class TextCut
{
    public static string Cut(string text, int limit) => text.Length <= limit ? text : string.Concat(text.AsSpan(0, limit - 1), "…");
}
