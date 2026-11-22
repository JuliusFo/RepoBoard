using RepoBoard.Core.Resources;

namespace RepoBoard.UI.State;

/// <summary>
/// "Amend last commit" in the commit box (WP-09): ticked for one HEAD, filled in with that commit's message, untouched by
/// a message that arrives too late, and unticked with a note when another commit became HEAD meanwhile – a parallel
/// session committed (review WP-09). Each step takes the box's text and gives back what it should show.
/// </summary>
public sealed class AmendDraft
{
    /// <summary>The message filled in from the last commit; an unedited one goes again on untick.</summary>
    private string? _filled;

    public bool On { get; private set; }

    /// <summary>HEAD when ticked: the board amends only that commit (<c>expectedHead</c>).</summary>
    public string? Head { get; private set; }

    /// <summary>Why the box was unticked by itself; shown beside it until the next tick or commit.</summary>
    public string? Note { get; private set; }

    public void Tick(string? head) => (On, Head, Note, _filled) = (true, head, null, null);

    /// <summary>The last commit's message arrived: into the box only if still ticked for that HEAD and nothing was typed.</summary>
    public string Fill(string box, string? head, string message)
    {
        if (!On || Head != head || !string.IsNullOrWhiteSpace(box))
        {
            return box;
        }

        _filled = message;
        return message;
    }

    /// <summary>Unticked: a filled-in message nobody edited goes again.</summary>
    public string Untick(string box)
    {
        var next = box == _filled ? "" : box;
        (On, Head, _filled) = (false, null, null);
        return next;
    }

    /// <summary>A new status: is another commit HEAD now, the box unticks and says why.</summary>
    public string Follow(string box, string? head)
    {
        if (!On || head == Head)
        {
            return box;
        }

        var next = Untick(box);
        Note = SyncText.Amend_HeadMoved;
        return next;
    }

    /// <summary>Committed (or amended): everything starts again.</summary>
    public void Done() => (On, Head, Note, _filled) = (false, null, null, null);
}
