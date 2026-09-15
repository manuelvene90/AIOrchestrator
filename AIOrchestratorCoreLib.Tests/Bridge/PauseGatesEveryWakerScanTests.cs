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
    /// </summary>
    public static TheoryData<string, string> TheWakers => new()
    {
        { "async Task Nudge_IdleImplementers_Async", "Nudge_IdleSupervisor" },
        { "async Task Check_LedgerHealth_Async", "_ledgerDebtSinceUtc" },
        { "void Flag_IdleMembers", "IdleMember" },
        { "async Task Resume_AllSessions_Async", "GO AHEAD — resume" },
        { "async Task Push_AwayDigests_Async", "AwayDigest_Decider.Should_Send" },

        // THE WAKE-TICKET SWEEP (one-wake-model, 2026-09-15). It writes no channel entry at all — it
        // writes the file a terminal session's monitor polls — which makes it the LOUDEST waker in the
        // list rather than an exception to it: in ticket mode this is the whole of what starts that
        // session's turn, so a paused orchestration whose supervisor still gets tickets is not asleep
        // in any sense the owner would recognise.
        { "async Task Sweep_WakeTickets_Async", "Write_WakeTicket(registered.StateFile" },
    };

    [Theory]
    [MemberData(nameof(TheWakers))]
    public void EveryWakerThatWritesToAChannel_SkipsAPausedOrchestration(string signatureMark, string anchor)
    {
        var body = Extract_Method(signatureMark);

        // The harness proves it found the right method before judging what is inside it — decision
        // 20: a check that cannot evaluate its predicate must not certify the absence of a defect.
        Assert.Contains(anchor, body, StringComparison.Ordinal);

        Assert.Contains(
            "session.Paused",
            body,
            StringComparison.Ordinal);
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
