using RepoBoard.Core.Resources;
using RepoBoard.UI.State;

namespace RepoBoard.UI.Tests.State;

/// <summary>The "Amend last commit" box (WP-09, review WP-09).</summary>
public sealed class AmendDraftTests
{
    [Fact]
    public void Ticking_fills_in_the_last_message_and_unticking_takes_it_back_unless_edited()
    {
        var draft = new AmendDraft();

        draft.Tick("a");
        var box = draft.Fill("", "a", "feat: x");
        Assert.Equal(("feat: x", true, "a"), (box, draft.On, draft.Head));
        Assert.Equal("", draft.Untick(box));

        draft.Tick("a");
        box = draft.Fill("", "a", "feat: x") + " (edited)";
        Assert.Equal("feat: x (edited)", draft.Untick(box));
        Assert.False(draft.On);
    }

    [Fact]
    public void A_message_that_arrives_late_does_not_land()
    {
        var draft = new AmendDraft();

        draft.Tick("a");
        draft.Untick("");
        Assert.Equal("", draft.Fill("", "a", "feat: x")); // unticked meanwhile

        draft.Tick("a");
        Assert.Equal("typed", draft.Fill("typed", "a", "feat: x")); // typed meanwhile
        Assert.Equal("", draft.Fill("", "b", "feat: x"));          // for another HEAD
    }

    [Fact]
    public void Another_commit_as_head_unticks_with_a_note_and_keeps_an_edited_message()
    {
        var draft = new AmendDraft();
        draft.Tick("a");
        var box = draft.Fill("", "a", "feat: x") + " (edited)";

        Assert.Equal(box, draft.Follow(box, "a")); // same HEAD: nothing happens
        Assert.True(draft.On);

        box = draft.Follow(box, "b");
        Assert.Equal(("feat: x (edited)", false), (box, draft.On));
        Assert.Equal(SyncText.Amend_HeadMoved, draft.Note);

        draft.Tick("b");
        Assert.Null(draft.Note);
        draft.Done();
        Assert.Equal((false, null, null), (draft.On, draft.Head, draft.Note));
    }
}
