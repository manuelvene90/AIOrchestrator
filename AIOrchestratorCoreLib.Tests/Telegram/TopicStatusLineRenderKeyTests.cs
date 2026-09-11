using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// THE STATUS LINE REPAINTS WHEN ITS BUTTONS CHANGE, not only when its text does — brief D.
///
/// <para>
/// This is the rule the hold toggle depends on and the one the first cut of brief D did not have.
/// The toggle's label carries the held count, so a hold that catches three messages changes the
/// rendering and not one character of the text. Comparing text alone said "nothing moved", no edit
/// was sent, and the count the owner is meant to read never left the process — the feature was
/// decorative.
/// </para>
/// <para>
/// The second half is why this is a KEY and not a special case: a bar that ends up wrong for ANY
/// reason would never be repainted either, because a quiet orchestration's text does not move for
/// hours.
/// </para>
/// </summary>
public class TopicStatusLineRenderKeyTests
{
    static readonly IReadOnlyList<IReadOnlyList<(string Data, string Label)>> NO_BUTTONS = [];

    static IReadOnlyList<IReadOnlyList<(string Data, string Label)>> Bar(params string[] labels)
    {
        return [[.. labels.Select(label => ("data:" + label, label))]];
    }

    [Fact]
    public void TheSameTextAndTheSameButtons_AreTheSameKey()
    {
        Assert.Equal(
            TopicStatusLine_RenderKey.Build("PULSE", Bar("A", "B")),
            TopicStatusLine_RenderKey.Build("PULSE", Bar("A", "B")));
    }

    /// <summary>THE CASE THE FEATURE NEEDS: identical text, one label changed by the held count.</summary>
    [Fact]
    public void AButtonLabelThatChanged_ChangesTheKey_EvenWhenTheTextIsIdentical()
    {
        Assert.NotEqual(
            TopicStatusLine_RenderKey.Build("PULSE", Bar("A", HoldButton_Data.HOLD_LABEL)),
            TopicStatusLine_RenderKey.Build("PULSE", Bar("A", TopicCommandButtons.Describe_ReleaseLabel(3))));
    }

    [Fact]
    public void ChangedTextStillChangesTheKey()
    {
        Assert.NotEqual(
            TopicStatusLine_RenderKey.Build("PULSE one", Bar("A")),
            TopicStatusLine_RenderKey.Build("PULSE two", Bar("A")));
    }

    /// <summary>
    /// Length-prefixed rather than delimited, so a label containing whatever character a delimiter
    /// would have used cannot make two different renderings collide. Asserted, because a collision
    /// here is a repaint that silently never happens.
    /// </summary>
    [Fact]
    public void TwoRenderingsThatWouldCollideUnderAPlainDelimiter_DoNot()
    {
        Assert.NotEqual(
            TopicStatusLine_RenderKey.Build("x", Bar("a|b", "c")),
            TopicStatusLine_RenderKey.Build("x", Bar("a", "b|c")));

        Assert.NotEqual(
            TopicStatusLine_RenderKey.Build("ab", NO_BUTTONS),
            TopicStatusLine_RenderKey.Build("a", Bar("b")));
    }

    /// <summary>The count moving is the whole point: every distinct count is a distinct key.</summary>
    [Fact]
    public void EveryHeldCountIsADistinctKey()
    {
        var keys = Enumerable.Range(0, 5)
            .Select(count => TopicStatusLine_RenderKey.Build("PULSE", Bar(TopicCommandButtons.Describe_ReleaseLabel(count))))
            .ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
    }
}
