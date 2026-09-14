using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Channels;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// WHICH OWNER-CHANNEL ENTRY RINGS THE PHONE, UNDER EACH OF THE TWO WAYS THIS MACHINE HAS SHIPPED
/// (plan 03 Task 2, <c>phone.push</c>).
///
/// <para>
/// <c>everything</c> is the fork's ruling of 2026-09-09 — *"if the supervisor writes to me, I must know
/// it"* — and it must answer byte-for-byte what <see cref="OwnerPush_Policy.Should_Push"/> answered on
/// the build that had no choice, so quiet's phone cannot move. <c>filtered</c> is master's: a question,
/// a <c>BLOCKED ON OWNER</c>, a file, the boot greeting and THE answer ring; everything else is held for
/// the turn-end digest. It is rebuilt from the predicates the policy already carried and nothing called.
/// </para>
/// <para>
/// THE DECISION IS A VALUE, not a boolean branch inline in the engine — that is the seam the spec asked
/// for, delivered as <see cref="OwnerPushDecisions"/> on the policy that already existed rather than as
/// an injected interface whose only implementation would be this static.
/// </para>
/// </summary>
public class OwnerPushDeciderTests
{
    const string NARRATION = "## [11] FROM supervisor — d — s\nimp-1 is pricing the matrix; rev-1 has the diff.";

    // ---- Everything (quiet): exactly what Should_Push does today, in OwnerPushDecisions clothing ----

    [Fact]
    public void UnderEverything_ProgressNarration_IsSentNow()
    {
        Assert.Equal(OwnerPushDecisions.SendNow, OwnerPush_Policy.Decide(PhonePushModes.Everything, NARRATION, ownerIsWaitingForAReply: false, subject: "s"));
    }

    [Fact]
    public void UnderEverything_AnOwnerRestatement_IsDropped()
    {
        var entry = "## [12] FROM supervisor — d — s\nOwner: \"What do you think? The old app does not ask for it.\"";

        Assert.Equal(OwnerPushDecisions.Drop, OwnerPush_Policy.Decide(PhonePushModes.Everything, entry, ownerIsWaitingForAReply: true, subject: "s"));
    }

    [Fact]
    public void UnderEverything_AnEmptyBody_IsDropped()
    {
        Assert.Equal(OwnerPushDecisions.Drop, OwnerPush_Policy.Decide(PhonePushModes.Everything, "", ownerIsWaitingForAReply: false, subject: null));
    }

    /// <summary>
    /// THE ARM <see cref="OwnerPush_Policy.Should_Push"/> STILL IS. It has callers this task has not
    /// read, so it stays — as a thin call into <c>Decide(Everything, …)</c>. If the two ever disagree,
    /// one of them has grown a second copy of the rule.
    /// </summary>
    [Theory]
    [InlineData(NARRATION, false, "s")]
    [InlineData("## [12] FROM supervisor — d — s\nOwner: \"is the rebuild done\"", true, "s")]
    [InlineData("", false, null)]
    [InlineData("## [1] FROM supervisor — d — supervisor online — Repo", false, "supervisor online — Repo")]
    public void ShouldPush_IsTheEverythingArm(string entry, bool ownerIsWaiting, string? subject)
    {
        var decidedToSend = OwnerPush_Policy.Decide(PhonePushModes.Everything, entry, ownerIsWaiting, subject) == OwnerPushDecisions.SendNow;

        Assert.Equal(decidedToSend, OwnerPush_Policy.Should_Push(entry, ownerIsWaiting, subject));
    }

    // ---- Filtered (classic): master's semantics, rebuilt from the predicates that are already here ----

    [Fact]
    public void UnderFiltered_AQuestion_IsSentNow()
    {
        var entry = "## [7] FROM supervisor — d — s\nQUESTION: merge now or hold\nOPTION: Merge\nOPTION: Hold";

        Assert.Equal(OwnerPushDecisions.SendNow, OwnerPush_Policy.Decide(PhonePushModes.Filtered, entry, ownerIsWaitingForAReply: false, subject: "s"));
    }

    [Fact]
    public void UnderFiltered_AProseQuestion_IsSentNow()
    {
        var entry = "## [4] FROM solo — d — s\nShould I merge this or hold it?\nEither is cheap from here.";

        Assert.Equal(OwnerPushDecisions.SendNow, OwnerPush_Policy.Decide(PhonePushModes.Filtered, entry, ownerIsWaitingForAReply: false, subject: "s"));
    }

    [Fact]
    public void UnderFiltered_BlockedOnOwner_IsSentNow()
    {
        var entry = $"## [3] FROM supervisor — d — s\n{OwnerPush_Policy.BLOCKED_MARKER}: need the token";

        Assert.Equal(OwnerPushDecisions.SendNow, OwnerPush_Policy.Decide(PhonePushModes.Filtered, entry, ownerIsWaitingForAReply: false, subject: "s"));
    }

    /// <summary>
    /// A FILE IS A DELIVERY, NOT NARRATION — and a held entry is filed as TEXT, so a picture held for
    /// the digest would reach the owner as its path and never as the picture (master, 2026-09-08:
    /// *"Continui a inviarmi la directory dell'immagine invece dell'immagine stessa"*). Both markers.
    /// </summary>
    [Theory]
    [InlineData("## [5] FROM supervisor — d — s\nThe comparison table now shows the cap.\nIMAGE: /repo/shots/table.png")]
    [InlineData("## [5] FROM supervisor — d — s\nThe plan-card mockup is ready.\nATTACH: /repo/mockups/plan-card.html")]
    public void UnderFiltered_AnEntryCarryingAPicture_IsSentNow(string entry)
    {
        Assert.Equal(OwnerPushDecisions.SendNow, OwnerPush_Policy.Decide(PhonePushModes.Filtered, entry, ownerIsWaitingForAReply: false, subject: "s"));
    }

    /// <summary>
    /// The greeting has an EMPTY body by role-command mandate, so nothing but its subject can rescue
    /// it — which is why the subject is a parameter at all (2026-08-25: *"I want to know that the
    /// sessions are ready"*).
    /// </summary>
    [Fact]
    public void UnderFiltered_TheBootGreeting_IsSentNow()
    {
        const string subject = "supervisor online — AIOrchestrator — repos\\AIOrchestrator";

        Assert.Equal(
            OwnerPushDecisions.SendNow,
            OwnerPush_Policy.Decide(PhonePushModes.Filtered, $"## [1] FROM supervisor — 2026-08-25 09:49 — {subject}", ownerIsWaitingForAReply: false, subject));
    }

    /// <summary>The reply to something they asked must get through — they are waiting for it.</summary>
    [Fact]
    public void UnderFiltered_TheAnswerWhileTheCreditIsOpen_IsSentNow()
    {
        var entry = "## [9] FROM supervisor — d — the answer\nYes, the daily DD is feasible.";

        Assert.Equal(OwnerPushDecisions.SendNow, OwnerPush_Policy.Decide(PhonePushModes.Filtered, entry, ownerIsWaitingForAReply: true, subject: "the answer"));
    }

    /// <summary>
    /// The waterfall. Not lost — the entry is in the channel and the app — and not dropped either: it
    /// is HELD, so the turn-end digest can hand it over as one message rather than one ring each.
    /// </summary>
    [Theory]
    [InlineData(NARRATION)]
    [InlineData("## [12] FROM supervisor — d — s\nConfirmed: the provider list is complete.")]
    [InlineData("## [3] FROM solo — d — s\nSTANDING BY.\nLedger line sits at [?] until they answer.")]
    public void UnderFiltered_ProgressNarration_IsHeldForTheDigest(string entry)
    {
        Assert.Equal(OwnerPushDecisions.HoldForDigest, OwnerPush_Policy.Decide(PhonePushModes.Filtered, entry, ownerIsWaitingForAReply: false, subject: "s"));
    }

    /// <summary>
    /// AN EMPTY BODY IS DROPPED, NEVER HELD, IN BOTH MODES — and this is the sharpest line in the file.
    /// The engine's own comment records why the old filing had to go rather than merely stop mattering:
    /// an empty entry "was filed and then released five minutes later, RINGING, wearing 'nothing has
    /// moved for 5 min — sending you the last thing it said'. A blank message, with a notification,
    /// about nothing."
    /// </summary>
    [Theory]
    [InlineData(PhonePushModes.Everything)]
    [InlineData(PhonePushModes.Filtered)]
    public void AnEmptyBody_IsDroppedInEitherMode(PhonePushModes mode)
    {
        Assert.Equal(OwnerPushDecisions.Drop, OwnerPush_Policy.Decide(mode, "", ownerIsWaitingForAReply: false, subject: null));
        Assert.Equal(OwnerPushDecisions.Drop, OwnerPush_Policy.Decide(mode, "   \n  ", ownerIsWaitingForAReply: true, subject: "s"));
    }

    /// <summary>
    /// Their own words quoted back say nothing they did not just type, and holding them would put them
    /// in the digest — so a restatement is dropped in both modes, even while they wait (2026-09-07).
    /// </summary>
    [Theory]
    [InlineData(PhonePushModes.Everything)]
    [InlineData(PhonePushModes.Filtered)]
    public void AnOwnerRestatement_IsDroppedInEitherMode_EvenWhileTheOwnerWaits(PhonePushModes mode)
    {
        var entry = "## [12] FROM supervisor — d — s\nThe owner said: “Che ne pensi? La vecchia app non lo richiede.”";

        Assert.Equal(OwnerPushDecisions.Drop, OwnerPush_Policy.Decide(mode, entry, ownerIsWaitingForAReply: true, subject: "s"));
    }

    /// <summary>
    /// A TURN-END DECLARATION IS NOT AN ANSWER AND MUST NOT BE ONE HERE EITHER. Is_TurnEndDeclaration
    /// already stops the credit being CONSUMED in the engine (decision 25); under `filtered` it must
    /// also stop a "WAITING ON …" subject being SENT as if it were the thing the owner is waiting for.
    /// </summary>
    [Fact]
    public void UnderFiltered_AWaitingOnSubject_IsHeld_EvenWhileTheOwnerWaits()
    {
        const string subject = "WAITING ON the task 6 re-review - fix landed 7af0aafe";
        var entry = $"## [2] FROM supervisor — d — {subject}\nTask 6 fix round landed: 7af0aafe, 116 runtime tests, review running.";

        Assert.Equal(OwnerPushDecisions.HoldForDigest, OwnerPush_Policy.Decide(PhonePushModes.Filtered, entry, ownerIsWaitingForAReply: true, subject));
    }

    // ---- D8: which mode a channel is under ----

    /// <summary>
    /// D8 (coordinator, 2026-09-14): GENERAL IS EXEMPT from <c>phone.push</c>. The general supervisor's
    /// channel is an owner channel too, and filtering it would stop the concierge's narration reaching
    /// the phone — which is most of what General is for.
    /// </summary>
    [Theory]
    [InlineData(PhonePushModes.Filtered)]
    [InlineData(PhonePushModes.Everything)]
    public void TheGeneralChannel_IsUnderEverything_WhateverTheConfiguredMode(PhonePushModes configured)
    {
        Assert.Equal(PhonePushModes.Everything, OwnerPush_Policy.Resolve_ModeForChannel(configured, ChannelDiscovery.GENERAL_ORCH_ID));
    }

    [Theory]
    [InlineData(PhonePushModes.Filtered)]
    [InlineData(PhonePushModes.Everything)]
    public void AnOrchestrationChannel_IsUnderTheConfiguredMode(PhonePushModes configured)
    {
        Assert.Equal(configured, OwnerPush_Policy.Resolve_ModeForChannel(configured, "strategy-lab-6"));
    }
}
