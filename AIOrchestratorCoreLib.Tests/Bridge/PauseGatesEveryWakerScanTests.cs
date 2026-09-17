using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// EVERY WAKER IS GATED ON THE PAUSE — the half of dormancy that does NOT ride the delivery funnel.
///
/// <para>
/// CLAUDE.md's PAUSE decision is explicit about why this needs its own guard per site: outbound
/// traffic all passes through <c>EffectiveMode_Resolver</c>, so one answer there reaches every send
/// gate at once — but *"pushing does not, because nothing a delivery mode says has ever governed
/// what the app WRITES INTO A CHANNEL"*. Each sweep that appends to a channel therefore asks the
/// question for itself, and the decision ends: **"Miss one and dormancy is a word."**
/// </para>
/// <para>
/// A SCAN, AND WRITTEN DOWN AS THE WEAKER CLAIM — the same shape and the same honesty as
/// <see cref="AwaySuppressesAppAlertsScanTests"/> next door. Driving these sweeps into firing from a
/// test needs minutes of measured idleness, a wall-clock half-hour boundary, or a dead pid file, and
/// the engine offers a seam for none of them; so an integration test that merely watches a paused
/// orchestration write nothing is green whether the gates are there or not, because in a short
/// window none of these would have fired anyway. That is exactly the "two routes to the same state"
/// that pins neither. This proves the gate is PRESENT AND PLACED, not that it fires.
/// <see cref="APausedOrchestrationIsDormantTests"/> carries the behavioural half of pause.
/// </para>
/// <para>
/// IT IS ALSO THE LIST ITSELF. The decision names the wakers, and a name in a document is not a
/// check; this is the one artifact that fails when someone adds a seventh sweep and does not gate
/// it, or deletes a gate while the suite stays green.
/// </para>
/// </summary>
public class PauseGatesEveryWakerScanTests
{
    const string ENGINE_FILE = "BridgeEngineModel.cs";

    /// <summary>A body shorter than this is an extraction that went wrong, not a method.</summary>
    const int PLAUSIBLE_BODY_FLOOR = 200;

    /// <summary>
    /// THE SWEEPS THE CHOKE POINT DOES NOT COVER, each with a word that proves the extraction found
    /// the method it means rather than some other one. `Append_SupervisorAttention_UnlessMeeting`
    /// stops the supervisor-facing traffic in one place; these write to members, to flag files or to
    /// every channel at once, which is why they sit outside it and need a guard each.
    ///
    /// <para>
    /// THE THIRD COLUMN IS THE SPELLING OF THE SCREEN, and it is per row rather than one literal for
    /// all of them (plan 02 task 12, 2026-09-15). A sweep that already holds a session in its hand
    /// reads <c>session.Paused</c>; a writer that holds only an orchestration id asks
    /// <c>Is_Paused</c>, which is the ONE implementation of the question and the same call the choke
    /// point below makes. Forcing the first spelling everywhere would have meant copying that
    /// one-line accessor into a method that has no session — a second copy of a rule, which is the
    /// thing CLAUDE.md decision 12 is about, written to satisfy a string match in a test. Each row
    /// still pins exactly one literal, so no case can pass for two reasons.
    /// </para>
    /// </summary>
    public static TheoryData<string, string, string> TheWakers => new()
    {
        { "async Task Nudge_IdleImplementers_Async", "Nudge_IdleSupervisor", "session.Paused" },
        { "async Task Check_LedgerHealth_Async", "_ledgerDebtSinceUtc", "session.Paused" },
        { "void Flag_IdleMembers", "IdleMember", "session.Paused" },
        { "async Task Resume_AllSessions_Async", "GO AHEAD — resume", "session.Paused" },
        { "async Task Push_AwayDigests_Async", "AwayDigest_Decider.Should_Send", "session.Paused" },

        // THE NOTE ROUTER'S SECOND ADAPTER (plan 02, 2026-09-15). AppNote_Writer itself is a
        // pass-through — it writes what its caller decided to write — so the row belongs to the
        // engine method that decides, and there are two. `Route_SupervisorNote` is NOT here: it is
        // only reachable through `Append_SupervisorAttention_UnlessMeeting`, whose own ordering Fact
        // sits below. `Route_ChannelNote` has no choke point above it and writes to a MEMBER's spoke
        // (and to the general channel), so it asks for itself.
        //
        // IT WAS NOT UNGATED IN PRACTICE, AND THAT IS WHY IT NEEDED A ROW. Its five callers are
        // reached from the mirror tick, and a paused orchestration resolves to Deferred, which
        // freezes its offsets and drops its channels out of `Find_ActiveChannels` — a delivery-mode
        // screen, in another method, that happens to cover a pause. A guard that holds for a reason
        // nobody wrote down is a guard the next caller does not inherit.
        { "bool Route_ChannelNote", "AppNote_Writer.Write", "Is_Paused(channel.OrchId)" },

        // THE WAKE-TICKET SWEEP (one-wake-model, 2026-09-15). It writes no channel entry at all — it
        // writes the file a terminal session's monitor polls — which makes it the LOUDEST waker in the
        // list rather than an exception to it: in ticket mode this is the whole of what starts that
        // session's turn, so a paused orchestration whose supervisor still gets tickets is not asleep
        // in any sense the owner would recognise.
        { "async Task Sweep_WakeTickets_Async", "Write_WakeTicket(registered.StateFile", "session.Paused" },

        // THE ROUTED-REPORT SWEEP (one-wake-model step 4). It writes a BRIEF into a reviewer's
        // channel — a waker in the fullest sense, since that entry is what starts the reviewer's
        // turn. A paused orchestration that still hands out re-reviews is not dormant in any sense
        // the owner would recognise. Its cap alert goes through the choke point below, which screens
        // the pause a second time; this row is the stronger half, because a paused orchestration is
        // not examined at all.
        { "async Task Sweep_RoutedReports_Async", "RoutedReport_Composer.Compose", "session.Paused" },
    };

    [Theory]
    [MemberData(nameof(TheWakers))]
    public void EveryWakerThatWritesToAChannel_SkipsAPausedOrchestration(string signatureMark, string anchor, string pauseMark)
    {
        var body = Extract_Method(signatureMark);

        // The harness proves it found the right method before judging what is inside it — decision
        // 20: a check that cannot evaluate its predicate must not certify the absence of a defect.
        Assert.Contains(anchor, body, StringComparison.Ordinal);

        Assert.Contains(
            pauseMark,
            body,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// AND THE ROUTER'S SCREEN IS ABOVE ITS WRITE, which is the half the row above cannot see. The
    /// same claim the choke point makes and for the same reason: below the write the entry is on
    /// disk and the session it was told not to poke is awake, whatever the method returns.
    /// </summary>
    [Fact]
    public void TheChannelNoteRouter_AsksAboutThePause_BeforeItWritesAnything()
    {
        var body = Extract_Method("bool Route_ChannelNote");

        var gate = body.IndexOf("Is_Paused(channel.OrchId)", StringComparison.Ordinal);
        var write = body.IndexOf("AppNote_Writer.Write(", StringComparison.Ordinal);

        Assert.True(gate >= 0, "the note router no longer asks whether the orchestration is paused, so the five coaching sites can poke a sleeping session");
        Assert.True(write >= 0, "the note router no longer calls the writer — this scan is reading a method it does not understand");

        Assert.True(
            gate < write,
            "the pause is checked AFTER the note is written: the coaching is already on disk and the session the owner put to sleep has been woken by it");
    }

    /// <summary>
    /// AND THE LIVENESS ALARM INSIDE THAT SWEEP IS BELOW THE PAUSE SCREEN. It is the one thing in the
    /// sweep that writes to a channel, and it is precisely the sort of alarm a pause must not produce:
    /// a paused orchestration's session is asleep BECAUSE THE OWNER SAID SO, and every ticket-mode
    /// session in one would otherwise be reported as having stopped waking, one window after the
    /// pause. The choke point below would refuse the append anyway, which is the belt; this is the
    /// braces, and it is the stronger half — a paused session is not examined at all.
    /// </summary>
    [Fact]
    public void TheWakeTicketStallAlarm_SitsBelowThePauseScreenOfItsSweep()
    {
        var body = Extract_Method("async Task Sweep_WakeTickets_Async");

        var paused = body.IndexOf("session.Paused", StringComparison.Ordinal);
        var alarm = body.IndexOf("Raise_WakeStall_IfTicketWentUnanswered", StringComparison.Ordinal);

        Assert.True(alarm >= 0, "the sweep no longer asserts that a ticket was acted on — the one failure this series introduces is silent again");
        Assert.True(paused >= 0, "the sweep no longer asks whether the orchestration is paused — this scan is reading a method it does not understand");

        Assert.True(
            paused < alarm,
            "the liveness alarm is raised above the pause screen: an orchestration the owner put to sleep would be reported as having stopped waking");
    }

    /// <summary>
    /// AND THE SAME FOR THE ROUTED-REPORT SWEEP: both of the things it writes sit BELOW its pause
    /// screen. The relay is the entry that starts a reviewer's turn, and the cap alert is precisely
    /// the sort of alarm a pause must not produce — a paused orchestration's reviewer has not
    /// answered BECAUSE THE OWNER SAID SO, so every held round in one would be reported as abandoned
    /// ninety minutes after the pause. The twin of
    /// <see cref="TheWakeTicketStallAlarm_SitsBelowThePauseScreenOfItsSweep"/>, and for the same
    /// reason: the choke point would refuse the append anyway, and this is the braces.
    /// </summary>
    [Fact]
    public void TheRoutedRelayAndItsCapAlarm_SitBelowThePauseScreenOfTheirSweep()
    {
        var body = Extract_Method("async Task Sweep_RoutedReports_Async");

        var paused = body.IndexOf("session.Paused", StringComparison.Ordinal);
        var relay = body.IndexOf("ChannelAppender.Append_AppEntry", StringComparison.Ordinal);
        var expiry = body.IndexOf("Close_RerouteContracts", StringComparison.Ordinal);

        Assert.True(relay >= 0, "the sweep no longer appends the relay — this scan is reading a method it does not understand");
        Assert.True(expiry >= 0, "the sweep no longer closes contracts, so a hold on the supervisor has no way out at all");
        Assert.True(paused >= 0, "the sweep no longer asks whether the orchestration is paused — every reviewer in a sleeping orchestration can still be handed a round");

        Assert.True(
            paused < relay,
            "the pause is checked AFTER the relay is appended: the brief is on disk and the reviewer the owner put to sleep is at work");

        Assert.True(
            paused < expiry,
            "the cap alert is raised above the pause screen: an orchestration the owner put to sleep would be told its reviewer has abandoned the round");
    }

    /// <summary>
    /// AND THE WAY OUT IS BELOW THE PAUSE INSIDE THE CLOSER TOO — not a second pause screen, but the
    /// proof that the closer is only ever reached through one. It is called from the sweep, which
    /// screens the pause first, and it is the ONLY caller: a second one would be an ungated route to
    /// the same append.
    /// </summary>
    [Fact]
    public void TheRerouteCloser_HasExactlyOneCaller_AndItIsThePausedSweep()
    {
        var source = Read_Source(ENGINE_FILE);

        var calls = source.Split("Close_RerouteContracts(").Length - 1;

        // One declaration, one call.
        Assert.Equal(2, calls);
        Assert.Contains("changed |= Close_RerouteContracts(session, open, handled);", Extract_Method("async Task Sweep_RoutedReports_Async"), StringComparison.Ordinal);
    }

    /// <summary>
    /// THE CHOKE POINT, and the ordering is the claim. Every piece of supervisor-facing attention
    /// traffic passes through here, so one check covers nudges, ledger complaints, idle flags and
    /// the periodic status — but only if it sits ABOVE the append. Below it, the entry is on disk
    /// and the session is awake whatever this returns.
    /// </summary>
    [Fact]
    public void TheSupervisorAttentionChokePoint_RefusesAPausedOrchestration_BeforeItAppends()
    {
        var body = Extract_Method("bool Append_SupervisorAttention_UnlessMeeting");

        var gate = body.IndexOf("Is_Paused(orchId)", StringComparison.Ordinal);
        var append = body.IndexOf("Append_AppEntry_Safe", StringComparison.Ordinal);

        Assert.True(gate >= 0, "the choke point no longer asks whether the orchestration is paused, so every waker above it is ungated");
        Assert.True(append >= 0, "the choke point no longer appends — this scan is reading a method it does not understand");

        Assert.True(
            gate < append,
            "the pause is checked AFTER the append: the entry is already on disk and the session it was told not to poke is awake");
    }

    /// <summary>
    /// THE MARKER IS RECONCILED ON THE TICK, and before anything in that tick can write. It is
    /// DERIVED, NEVER AUTHORED — the rule the meeting flag states — so a flag left behind by a crash
    /// cannot quietly exempt a session from the turn-end hook for ever, and a pause that survived a
    /// restart gets its file back before the first append of the new process.
    /// </summary>
    [Fact]
    public void ThePausedMarkerIsReconciledOnEveryTick_BesideTheMeetingFlag_AndBeforeAnythingWrites()
    {
        var body = Extract_Method("async Task Execute_MirrorTick_Inside_Snapshot_Async");

        var meeting = body.IndexOf("Sync_MeetingFlags()", StringComparison.Ordinal);
        var paused = body.IndexOf("Sync_PausedFlags()", StringComparison.Ordinal);
        var deliver = body.IndexOf("Flush_OwnerDeliveries_Async", StringComparison.Ordinal);

        Assert.True(meeting >= 0, "the meeting reconcile is gone — this scan is reading a tick it does not understand");
        Assert.True(paused >= 0, "nothing reconciles .paused on the tick, so the marker can outlive the state it stands for");

        Assert.True(
            paused > meeting && paused < deliver,
            "the paused reconcile does not sit between the meeting flags and the first write of the tick");
    }

    /// <summary>
    /// AND THE WATCHDOG, which is the one waker that does not write to a channel at all: it respawns
    /// a dead terminal. A session booted back up reads its role command, arms a watcher and starts
    /// working — the pause silently over, with nothing on screen saying so.
    /// </summary>
    [Fact]
    public void TheWatchdog_DoesNotRespawnAPausedOrchestration()
    {
        var source = Read_Source("SessionWatchdogModel.cs");

        Assert.Contains("if (session.Paused)", source, StringComparison.Ordinal);
    }

    static string Extract_Method(string signatureMark)
    {
        var source = Read_Source(ENGINE_FILE);

        var at = source.IndexOf(signatureMark, StringComparison.Ordinal);

        Assert.True(at >= 0, $"'{signatureMark}' is not in {ENGINE_FILE} — this scan cannot prove anything about a method it cannot find");

        var open = source.IndexOf('{', at);

        Assert.True(open >= 0, $"no body found for '{signatureMark}'");

        var depth = 0;

        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}' && --depth == 0)
            {
                var body = source[open..(i + 1)];

                Assert.True(
                    body.Length >= PLAUSIBLE_BODY_FLOOR,
                    $"the body extracted for '{signatureMark}' is {body.Length} characters — that is a brace-matching failure, not a method");

                return body;
            }
        }

        throw new Exception($"unbalanced braces walking the body of '{signatureMark}'");
    }

    /// <summary>
    /// SAY WHICH COPY YOU READ (CLAUDE.md decision 18): this reads the BRANCH SOURCE, walking up
    /// from the test binary, and refuses rather than certifying anything if it cannot find it.
    /// </summary>
    static string Read_Source(string fileName)
    {
        var folder = AppContext.BaseDirectory;

        for (var depth = 0; depth < 8; depth++)
        {
            var matches = Directory.GetFiles(folder, fileName, SearchOption.AllDirectories);

            var source = matches.FirstOrDefault(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

            if (source != null)
                return File.ReadAllText(source);

            var parent = Directory.GetParent(folder);

            if (parent == null)
                break;

            folder = parent.FullName;
        }

        throw new Exception($"{fileName} not found walking up from {AppContext.BaseDirectory} — a scan that cannot read its subject must refuse to run, not pass");
    }
}
