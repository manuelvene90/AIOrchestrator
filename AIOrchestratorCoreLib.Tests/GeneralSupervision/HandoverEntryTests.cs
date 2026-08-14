using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.GeneralSupervision;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.GeneralSupervision;

/// <summary>
/// THE PRECONDITION OF A PROMOTION, and the check that makes "nothing moves" safe.
///
/// Promotion ends the solo session and spawns a supervisor onto the same `owner-channel.md`. Nothing
/// is copied and no history is lost, because the supervisor reads the very file the solo was writing
/// — but the solo's IN-CONTEXT state dies with it, and that is the half no file carries. Whatever it
/// worked out and never wrote down is gone the moment its terminal closes.
///
/// So the handover entry is a REQUIREMENT of the request rather than a courtesy: the solo writes down
/// what the supervisor needs to know, and the app refuses the promotion if it did not. A settled
/// design decision, not a preference — the owner tap that follows costs a crew, and a crew briefed
/// from a channel that stops mid-thought is worse than the solo that was already there.
/// </summary>
public class HandoverEntryTests
{
    /// <summary>The ordinary case: the solo files a handover, then asks to be promoted.</summary>
    [Fact]
    public void ASoloHandoverEntryIsFound()
    {
        Assert.True(HandoverEntry_Detector.Has_HandoverEntry(
        [
            Entry(1, ChannelAuthors.Owner, "make the screener stop double-counting"),
            Entry(2, ChannelAuthors.Solo, "HANDOVER — the parser is the hard part, three call sites left"),
        ]));
    }

    /// <summary>
    /// NO ENTRY, NO PROMOTION. This is the whole point of the check: the request arrives from a
    /// session that is about to stop existing.
    /// </summary>
    [Fact]
    public void AChannelWithoutOneIsRefused()
    {
        Assert.False(HandoverEntry_Detector.Has_HandoverEntry(
        [
            Entry(1, ChannelAuthors.Owner, "make the screener stop double-counting"),
            Entry(2, ChannelAuthors.Solo, "done — three call sites fixed, tests green"),
        ]));
    }

    /// <summary>
    /// THE SOLO'S OWN, and nobody else's. The owner asking "can you hand this over?" is not the solo
    /// writing down what it knows — and the owner's words arrive in this very file as `FROM owner`
    /// entries, so without an author gate the request could be satisfied by the person who asked for
    /// it. Same gate the window markers already have, for the same reason.
    /// </summary>
    [Fact]
    public void AnEntryFromAnyoneElseDoesNotCount()
    {
        Assert.False(HandoverEntry_Detector.Has_HandoverEntry(
        [
            Entry(1, ChannelAuthors.Owner, "HANDOVER this to a full crew please"),
            Entry(2, ChannelAuthors.App, "HANDOVER requested"),
        ]));
    }

    /// <summary>
    /// MID-SENTENCE IN THE BODY IS DISCUSSION, exactly as it is for every other marker in this
    /// system: a solo explaining that it has not written a handover yet must not thereby write one.
    /// The marker counts in the SUBJECT anywhere, or at the START of a body line.
    /// </summary>
    [Fact]
    public void TalkingAboutAHandoverIsNotFilingOne()
    {
        Assert.False(HandoverEntry_Detector.Has_HandoverEntry(
        [
            Entry(1, ChannelAuthors.Solo, "still working", "I will write the HANDOVER entry before asking for a crew."),
        ]));

        Assert.True(HandoverEntry_Detector.Has_HandoverEntry(
        [
            Entry(1, ChannelAuthors.Solo, "still working", "HANDOVER — the parser is the hard part."),
        ]));
    }

    /// <summary>
    /// ONE MATCHER, not a second one written for this marker. It reads through the same
    /// `MemberState_Resolver` matcher the six existing markers use, so the position rule, the
    /// whole-token rule and the quotation rule are the ones already in force — and a marker written
    /// in bold or after a bullet still counts, because members write markdown.
    /// </summary>
    [Fact]
    public void ItUsesTheSameMatcherAsEveryOtherMarker()
    {
        Assert.True(HandoverEntry_Detector.Has_HandoverEntry(
        [
            Entry(1, ChannelAuthors.Solo, "**HANDOVER** — what the next crew needs"),
        ]));

        // A longer word containing the marker is not the marker.
        Assert.False(HandoverEntry_Detector.Has_HandoverEntry(
        [
            Entry(1, ChannelAuthors.Solo, "HANDOVERS are what I am reading about"),
        ]));
    }

    /// <summary>An empty channel cannot satisfy it — the case a brand-new orchestration is in.</summary>
    [Fact]
    public void AnEmptyChannelIsRefused()
    {
        Assert.False(HandoverEntry_Detector.Has_HandoverEntry([]));
    }

    static IChannelEntry Entry(int index, ChannelAuthors author, string subject, string body = "body")
    {
        return ChannelEntry_Factory.Create(
            index, author, "2026-08-13 13:00", subject, body,
            $"## [{index}] FROM {author} — 2026-08-13 13:00 — {subject}\n{body}");
    }
}
