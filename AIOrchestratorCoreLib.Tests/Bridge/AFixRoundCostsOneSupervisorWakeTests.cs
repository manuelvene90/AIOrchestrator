using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.Running.WakeTicket;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE WHOLE FIX ROUND, COUNTED. Every other case in this plan asks whether one piece works; this one
/// drives a round from the reviewer's findings to the re-verdict through the engine's real tick and
/// COUNTS what it cost the supervisor — with the contract and, in the case below it, without.
///
/// <para>
/// THE ORACLE IS THE TICKET NUMBER, and it is a number the app already writes for its own reasons:
/// under <c>wake: ticket</c> a supervisor's turn begins with a numbered file
/// (<see cref="WakeTicket_Store"/>), so "how many times was this session woken" is readable from disk
/// rather than modelled. The two cases below differ in ONE line of one channel entry — the
/// <c>REROUTE:</c> the supervisor's own verdict carries — and in nothing else.
/// </para>
/// <para>
/// WHAT THIS DOES NOT SAY, and the plan is explicit that it must not be said. A ticket is what a
/// supervisor's turn costs UNDER <c>wake: ticket</c>. That is not the shipped default: under
/// <c>wake: watcher</c> the supervisor's bash monitor fingerprints its channel files and wakes it on
/// the fix report whatever the app decided, so on such a machine this round still buys the reviewer a
/// correctly scoped brief and the supervisor a turn it does not have to compose, and the wake-up
/// itself is NOT saved. Nothing here was observed on a machine running real orchestrations; what is
/// measured is the ticket count in this test, and the report says so in those words.
/// </para>
/// <para>
/// THE ROUND-ONE REPORT IS PART OF THE FIXTURE, NOT DECORATION. A fix round always follows a report
/// the supervisor has already been handed from that implementer's channel, and that matters
/// mechanically: <c>WakeUp_Policy</c> exempts the FIRST entry a session is ever handed from a source
/// from the digest, so an <c>imp-1</c> the supervisor had never heard from has its fix report wake it
/// AT ONCE, on the tick before the postman's round runs, and the round then costs two wake-ups with
/// the contract exactly as it does without one. MEASURED on 2026-09-17 by running the case below with
/// the round-one report taken out: 2 where it expects 1. The fixture keeps that report because it is
/// the shape of every real fix round; the case where it is missing is a gap of the feature, written
/// down in the plan's report rather than pinned here as intended behaviour.
/// </para>
/// <para>
/// NO TELEGRAM CLIENT AND NO PRINT SESSIONS, for the reasons <see cref="WakeTicketSweepTests"/> and
/// <see cref="RoutedReportSweepTests"/> give next door: tickets and relays are local files with
/// nothing to do with the phone, and a registered bridge-driven session under a live engine can spawn
/// a real <c>claude</c>. Everything here is a TERMINAL supervisor behind a recording spawner.
/// </para>
/// </summary>
public class AFixRoundCostsOneSupervisorWakeTests : IDisposable
{
    /// <summary>
    /// THE DIGEST IS ON AND SHORT, and both halves are load-bearing.
    ///
    /// <para>
    /// ON, because the app decides a supervisor's wake-ups on the tick BEFORE the postman's round runs
    /// — <c>Sweep_WakeTickets_Async</c> is above <c>Sweep_RoutedReports_Async</c> in the tick, and the
    /// engine's own comment there says the one-tick lag costs nothing "because member traffic waits out
    /// MemberDigestWindow before it can wake anyone". That is the argument this fixture depends on, and
    /// it holds only while the digest holds: MEASURED on 2026-09-17 with <c>memberDigestMinutes: 0</c>
    /// — a supported setting, "the digest is off" — the contract case costs TWO wake-ups, 2 where the
    /// assertion below expects 1. The relay is still written; the wake it is meant to spare happens
    /// first. In the plan's report, not pinned here as intended behaviour.
    /// </para>
    /// <para>
    /// SHORT, because the control case has to WAIT OUT that window twice and this is a test. Three
    /// seconds is two orders of magnitude above the 20 ms tick the routed sweep needs to get ahead of
    /// the next wake decision, and two orders below the shipped five minutes.
    /// </para>
    /// </summary>
    const string CONFIG = """{"repos":[],"runners":{"supervisor":{"runner":"terminal","wake":"ticket"}},"printRunner":{"memberDigestMinutes":0.05}}""";

    /// <summary>The same number as <see cref="CONFIG"/>'s <c>memberDigestMinutes</c>, in milliseconds — see <see cref="Window_PastTheDigest"/>.</summary>
    const int MEMBER_DIGEST_MILLISECONDS = 3_000;

    const string BASE_COMMIT = "abc1234";
    const string HEAD_COMMIT = "def5678";

    const string ROUND_ONE_REPORT = "Task 1 is on the branch at abc1234, suite green.";
    const string FINDINGS = "F1 the guard runs below the write. F2 the wording. F3 a second formatter.";
    const string SUPERVISORS_WORDS = "Check F1 and F3 only. F2 was accepted as stated.";
    const string IMPLEMENTERS_WORDS = "F1: the guard now runs above the write. F3: the second formatter is deleted.";
    const string RE_REVIEW = "F1 closed, evidence read. F3 closed, the second copy is gone. Nothing new.";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;

    public AFixRoundCostsOneSupervisorWakeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-one-wake-round-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);
        File.WriteAllText(_paths.ConfigFile, CONFIG);

        _store = OrchestrationSessionStore_Factory.Create(_paths);

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var log = OrchestrationLog_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), log);

        _engine = BridgeEngine_Factory.Create_WithTelegramClient(
            _paths, configProvider, _store, _launcher, log, telegramClient: null, BridgeTestTiming.Fast());
    }

    public void Dispose()
    {
        TempTree.Delete_BestEffort(_tempRoot);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// WITH THE CONTRACT: the fix report is relayed and HELD, and the supervisor is woken once between
    /// the findings and the re-verdict — on the re-review, carrying both.
    ///
    /// <para>
    /// THE SILENCE IS WAITED OUT PAST THE DIGEST WINDOW, which is what stops it having two routes to
    /// green (CLAUDE.md decision 20). A ticket that had merely not arrived YET would look identical,
    /// and the case below — the same round, the same window, one line different — is where that
    /// window is shown to be long enough for a wake to happen in.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AFixRoundWhoseVerdictCarriedAContract_WakesTheSupervisorOnceBetweenTheFindingsAndTheReVerdict()
    {
        var orchId = await Start_AndReachTheFindings_Async();
        var atTheFindings = Read_TicketNumber(orchId);

        // THE VERDICT TURN, written as that turn would have written it: the fix brief, and the contract
        // at the end of it. This entry is the supervisor's own, so it wakes nobody here.
        Append_Supervisor(orchId, "imp-1", "FIX BRIEF — round 1", $"{SUPERVISORS_WORDS}\n{ChannelGrammar.REROUTE} rev-1 from {BASE_COMMIT}");

        File_MemberEntry(orchId, "imp-1", ChannelAuthors.Implementer, "FIX REPORT", $"{IMPLEMENTERS_WORDS}\n{ChannelGrammar.FIXED} {HEAD_COMMIT}");

        var relayed = await Run_Until_Async(() => Find_Relay_OrNull(orchId, "rev-1") != null, BridgeTestTiming.Window_ForTicks(10));

        Assert.True(relayed, "the app never did the postman's round, so the ticket count below would be about nothing");

        // PAST THE DIGEST WINDOW. Anything the digest was going to release has been released by now —
        // the control case takes its second ticket inside exactly this wait.
        await Run_Until_Async(() => Read_TicketNumber(orchId) > atTheFindings, Window_PastTheDigest(10));

        Assert.Equal(atTheFindings, Read_TicketNumber(orchId));

        File_MemberEntry(orchId, "rev-1", ChannelAuthors.Reviewer, "RE-REVIEW — both closed", RE_REVIEW);

        var woke = await Run_Until_Async(() => Read_TicketNumber(orchId) > atTheFindings, Window_PastTheDigest(10));

        Assert.True(woke, "the re-review never woke the supervisor, so the fix report was not held — it was lost");
        Assert.Equal(atTheFindings + 1, Read_TicketNumber(orchId));

        // NOTHING WAS DROPPED BY BEING HELD. The turn that finally starts carries the fix report it was
        // held out of, beside the re-review that released it — which is what separates a saving from an
        // omission, and the reason the hold withholds an identity from the wake RULES and from nothing
        // else.
        var pack = Read_StatePack(orchId);

        Assert.Contains(IMPLEMENTERS_WORDS, pack, StringComparison.Ordinal);
        Assert.Contains(RE_REVIEW, pack, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE LIVE CONTROL, and it is the whole claim: the SAME round with the <c>REROUTE:</c> line taken
    /// out of the verdict costs the supervisor TWO wake-ups over the same stretch — one to read the fix
    /// report and post it on, one for the re-review.
    ///
    /// <para>
    /// A count asserted without this is a number with two routes to it. It also pins the direction of
    /// the feature's own failure: nothing here is different except a line the supervisor writes, so a
    /// hold that applied to every fix report would show up as this case going quiet.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheSameRoundWithNoContractInTheVerdict_WakesTheSupervisorTwice()
    {
        var orchId = await Start_AndReachTheFindings_Async();
        var atTheFindings = Read_TicketNumber(orchId);

        // THE SAME VERDICT, WITHOUT ITS LAST LINE.
        Append_Supervisor(orchId, "imp-1", "FIX BRIEF — round 1", SUPERVISORS_WORDS);

        File_MemberEntry(orchId, "imp-1", ChannelAuthors.Implementer, "FIX REPORT", $"{IMPLEMENTERS_WORDS}\n{ChannelGrammar.FIXED} {HEAD_COMMIT}");

        var first = await Run_Until_Async(() => Read_TicketNumber(orchId) > atTheFindings, Window_PastTheDigest(10));

        Assert.True(first, "the fix report did not wake the supervisor, so the held case beside it proves nothing about a hold");
        Assert.Equal(atTheFindings + 1, Read_TicketNumber(orchId));

        // AND NOBODY WAS BRIEFED. The postman's round is the contract's, not the fix report's: with no
        // REROUTE: line the reviewer's channel is untouched and the re-review brief is the supervisor's
        // to write on the turn it has just been woken for.
        Assert.Null(Find_Relay_OrNull(orchId, "rev-1"));

        File_MemberEntry(orchId, "rev-1", ChannelAuthors.Reviewer, "RE-REVIEW — both closed", RE_REVIEW);

        var second = await Run_Until_Async(() => Read_TicketNumber(orchId) > atTheFindings + 1, Window_PastTheDigest(10));

        Assert.True(second, "the re-review never woke the supervisor");
        Assert.Equal(atTheFindings + 2, Read_TicketNumber(orchId));
    }

    /// <summary>
    /// ROUND ONE, IDENTICAL IN BOTH CASES: the implementer reports, the supervisor is woken by it, the
    /// reviewer files findings, and the supervisor is woken by those. Both wake-ups are OUTSIDE what
    /// this plan touches — the findings turn is where the round's scope is decided and the contract is
    /// written — so the count both cases start from is taken here, at the findings, and never at zero.
    /// </summary>
    async Task<string> Start_AndReachTheFindings_Async()
    {
        var orchId = _launcher.Start_Orchestration("Repo", _tempRepo).OrchId;

        // A session registered with no cursors is BASELINED on the app's first sight of it, so the
        // traffic below has to arrive AFTER the sweep has seen it or it is absorbed as history.
        await Run_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(4));

        File_MemberEntry(orchId, "imp-1", ChannelAuthors.Implementer, "REPORT — task 1", ROUND_ONE_REPORT);

        var read = await Run_Until_Async(() => Read_TicketNumber(orchId) == 1, Window_PastTheDigest(10));

        Assert.True(read, "the supervisor was never woken by imp-1's round-one report, so the fixture is not a fix round");

        File_MemberEntry(orchId, "rev-1", ChannelAuthors.Reviewer, "REVIEW — 3 findings", FINDINGS);

        var findings = await Run_Until_Async(() => Read_TicketNumber(orchId) == 2, Window_PastTheDigest(10));

        Assert.True(findings, "the supervisor was never woken by the reviewer's findings, so there is no round to count");

        return orchId;
    }

    void Append_Supervisor(string orchId, string memberId, string subject, string body)
    {
        Assert.True(
            ChannelAppender.Append_SessionEntry(
                _paths.Get_ImplementerChannelFile(orchId, memberId), ChannelAuthors.Supervisor, subject, body, DateTime.Now),
            $"'{subject}' could not be appended to '{orchId}/{memberId}', so this case would prove nothing");
    }

    void File_MemberEntry(string orchId, string memberId, ChannelAuthors author, string subject, string body)
    {
        Assert.True(
            ChannelAppender.Append_SessionEntry(
                _paths.Get_ImplementerChannelFile(orchId, memberId), author, subject, body, DateTime.Now),
            $"'{subject}' could not be appended to '{orchId}/{memberId}', so this case would prove nothing");
    }

    int Read_TicketNumber(string orchId)
    {
        return Read_Ticket_OrNull(orchId)?.Number ?? 0;
    }

    IWakeTicket? Read_Ticket_OrNull(string orchId)
    {
        return WakeTicket_Store.Read_OrNull(
            WakeTicket_Store.Get_File(_paths, SessionRoles.Supervisor, orchId, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID));
    }

    string Read_StatePack(string orchId)
    {
        var ticket = Read_Ticket_OrNull(orchId);

        Assert.NotNull(ticket);
        Assert.NotNull(ticket!.StatePackFile);
        Assert.True(File.Exists(ticket.StatePackFile), $"the ticket names a pack that is not on disk: {ticket.StatePackFile}");

        return File.ReadAllText(ticket.StatePackFile!);
    }

    IChannelEntry? Find_Relay_OrNull(string orchId, string reviewerId)
    {
        return ChannelHistory_Cache
            .Read_Entries(_paths.Get_ImplementerChannelFile(orchId, reviewerId))
            .LastOrDefault(entry => entry.Author == ChannelAuthors.App && RoutedReport_Tag.Is_Routed(entry.Subject));
    }

    /// <summary>
    /// A window that certainly contains the whole member digest AND <paramref name="ticks"/> ticks
    /// after it — the wait both "it wakes" and "it does not wake" need here, because a digest that has
    /// not elapsed looks exactly like a hold.
    /// </summary>
    static int Window_PastTheDigest(int ticks)
    {
        return MEMBER_DIGEST_MILLISECONDS + BridgeTestTiming.Window_ForTicks(ticks);
    }

    async Task<bool> Run_Until_Async(Func<bool> condition, int maxMilliseconds)
    {
        using var cancellation = new CancellationTokenSource();

        var loop = _engine.Run_Async(cancellation.Token);
        var satisfied = false;

        for (var waited = 0; waited < maxMilliseconds; waited += 50)
        {
            if (condition())
            {
                satisfied = true;
                break;
            }

            await Task.Delay(50);
        }

        await cancellation.CancelAsync();

        try
        {
            await loop;
        }
        catch (OperationCanceledException)
        {
            // The only way these loops end.
        }

        return satisfied || condition();
    }
}
