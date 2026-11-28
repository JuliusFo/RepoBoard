using Microsoft.AspNetCore.Components;

namespace RepoBoard.UI.Components;

/// <summary>
/// Small inline SVG icons; they inherit the text colour via <c>currentColor</c>. The shapes are taken unchanged from
/// Lucide (<c>lucide-static</c> 1.55.0, ISC, see <c>wwwroot/lib/lucide</c>) – the Lucide name stands in each summary, with
/// the one meaning the icon has in RepoBoard. A new icon comes from the same set (ADR 0001). Most are copied from
/// FerretSharp's <c>Icons.cs</c>.
/// </summary>
internal static class Icons
{
    /// <summary>Lucide <c>search</c>: the filter field.</summary>
    public static readonly MarkupString Search = Svg(
        """<path d="m21 21-4.34-4.34"/><circle cx="11" cy="11" r="8"/>""");

    /// <summary>Lucide <c>x</c>: hides a message or closes a panel (the diff, a dialog) – nothing is removed.</summary>
    public static readonly MarkupString Close = Svg(
        """<path d="M18 6 6 18"/><path d="m6 6 12 12"/>""");

    /// <summary>
    /// Lucide <c>list-x</c>: takes a repository off the board (its folder stays). Its own icon: as an X it stood a row above
    /// the X that hides an error (review 0.4.0, A10).
    /// </summary>
    public static readonly MarkupString RemoveFromBoard = Svg(
        """<path d="M11 12H3"/><path d="M16 6H3"/><path d="M16 18H3"/><path d="m19 10-4 4"/><path d="m15 10 4 4"/>""");

    /// <summary>Lucide <c>chevron-down</c>: an open row or panel.</summary>
    public static readonly MarkupString ChevronDown = Svg(
        """<path d="m6 9 6 6 6-6"/>""");

    /// <summary>Lucide <c>chevron-right</c>: a closed row or panel.</summary>
    public static readonly MarkupString ChevronRight = Svg(
        """<path d="m9 18 6-6-6-6"/>""");

    /// <summary>Lucide <c>arrow-up</c>: push; commits ahead of the upstream.</summary>
    public static readonly MarkupString Push = Svg(
        """<path d="m5 12 7-7 7 7"/><path d="M12 19V5"/>""");

    /// <summary>Lucide <c>arrow-down</c>: pull; commits behind the upstream.</summary>
    public static readonly MarkupString Pull = Svg(
        """<path d="M12 5v14"/><path d="m19 12-7 7-7-7"/>""");

    /// <summary>Lucide <c>download</c>: fetch.</summary>
    public static readonly MarkupString Fetch = Svg(
        """<path d="M12 15V3"/><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><path d="m7 10 5 5 5-5"/>""");

    /// <summary>Lucide <c>triangle-alert</c>: a warning (an error line, auto-pull skipped).</summary>
    public static readonly MarkupString Warning = Svg(
        """<path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3"/><path d="M12 9v4"/><path d="M12 17h.01"/>""");

    /// <summary>Lucide <c>git-branch</c>: a branch.</summary>
    public static readonly MarkupString Branch = Svg(
        """<path d="M15 6a9 9 0 0 0-9 9V3"/><circle cx="18" cy="6" r="3"/><circle cx="6" cy="18" r="3"/>""");

    /// <summary>Lucide <c>check</c>: up to date; the current branch.</summary>
    public static readonly MarkupString Check = Svg(
        """<path d="M20 6 9 17l-5-5"/>""");

    /// <summary>Lucide <c>refresh-cw</c>: read the status again.</summary>
    public static readonly MarkupString Refresh = Svg(
        """<path d="M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8"/><path d="M21 3v5h-5"/><path d="M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16"/><path d="M8 16H3v5"/>""");

    /// <summary>Lucide <c>plus</c>: add a repository; create a branch.</summary>
    public static readonly MarkupString Plus = Svg(
        """<path d="M5 12h14"/><path d="M12 5v14"/>""");

    /// <summary>Lucide <c>code</c>: open in the code editor.</summary>
    public static readonly MarkupString Code = Svg(
        """<path d="m16 18 6-6-6-6"/><path d="m8 6-6 6 6 6"/>""");

    /// <summary>Lucide <c>folder</c>: open the folder in the Explorer.</summary>
    public static readonly MarkupString Folder = Svg(
        """<path d="M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z"/>""");

    /// <summary>
    /// Lucide <c>square-terminal</c>: open a terminal in the folder. Not from FerretSharp – written without the package at
    /// hand; check it against <c>lucide-static</c> 1.55.0 (backlog).
    /// </summary>
    public static readonly MarkupString Terminal = Svg(
        """<path d="m7 11 2-2-2-2"/><path d="M11 13h4"/><rect width="18" height="18" x="3" y="3" rx="2" ry="2"/>""");

    /// <summary>Lucide <c>sliders-horizontal</c>: the settings.</summary>
    public static readonly MarkupString Settings = Svg(
        """<path d="M10 5H3"/><path d="M12 19H3"/><path d="M14 3v4"/><path d="M16 17v4"/><path d="M21 12h-9"/><path d="M21 19h-5"/><path d="M21 5h-7"/><path d="M8 10v4"/><path d="M8 12H3"/>""");

    /// <summary>Lucide <c>play</c>: resume the paused auto-sync.</summary>
    public static readonly MarkupString Resume = Svg(
        """<path d="M5 5a2 2 0 0 1 3.008-1.728l11.997 6.998a2 2 0 0 1 .003 3.458l-12 7A2 2 0 0 1 5 19z"/>""");

    /// <summary>Lucide <c>layers</c>: a repository's linked worktrees (the count that shows or hides them).</summary>
    public static readonly MarkupString Worktrees = Svg(
        """<path d="M12.83 2.18a2 2 0 0 0-1.66 0L2.6 6.08a1 1 0 0 0 0 1.83l8.58 3.91a2 2 0 0 0 1.66 0l8.58-3.9a1 1 0 0 0 0-1.83z"/><path d="M2 12a1 1 0 0 0 .58.91l8.6 3.91a2 2 0 0 0 1.65 0l8.58-3.9A1 1 0 0 0 22 12"/><path d="M2 17a1 1 0 0 0 .58.91l8.6 3.91a2 2 0 0 0 1.65 0l8.58-3.9A1 1 0 0 0 22 17"/>""");

    /// <summary>Lucide <c>lock</c>: a worktree locked against removal (<c>git worktree lock</c>).</summary>
    public static readonly MarkupString Locked = Svg(
        """<rect width="18" height="11" x="3" y="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/>""");

    /// <summary>Lucide <c>trash-2</c>: removes a worktree – deletes its folder (unlike <see cref="RemoveFromBoard"/>, which only takes a repository off the board).</summary>
    public static readonly MarkupString Trash = Svg(
        """<path d="M10 11v6"/><path d="M14 11v6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6"/><path d="M3 6h18"/><path d="M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>""");

    private static MarkupString Svg(string paths) =>
        new($"""<svg class="icon" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">{paths}</svg>""");
}
