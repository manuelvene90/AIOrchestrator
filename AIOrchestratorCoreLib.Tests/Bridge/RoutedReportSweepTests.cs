using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;

using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Reviewing;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE POSTMAN'S ROUND, DRIVEN THROUGH THE ENGINE'S REAL TICK. Tasks 5 to 7 shipped the contract, the
/// judge, the composer and the hold, and every one of them was INERT: nothing in production wrote a
/// contract in state <c>Routed</c>, so nothing was ever held and no reviewer was ever briefed. This
/// sweep is what arms them, and these cases are about the loop — that it reaches these channels at
/// all, screens them correctly, and writes exactly one entry — which is not a property any pure
/// function could be asked about.
///
/// <para>
/// EVERY SILENCE IS ASSERTED BESIDE A LIVE CONTROL IN THE SAME RUN (CLAUDE.md decision 20). "No relay
/// was written" has two routes to it — the screen held, or the sweep never ran — so a quiet
/// orchestration on its own pins neither. The control is a second orchestration, in the same ticks,
/// that DOES get its relay.
/// </para>
/// <para>
/// NO TELEGRAM CLIENT and no print sessions, for the reasons <see cref="WakeTicketSweepTests"/> gives
/// next door: the relay is a channel entry and has nothing to do with the phone, and a registered
/// bridge-driven session under a live engine can spawn a real <c>claude</c>.
/// </para>
/// </summary>
public class RoutedReportSweepTests : IDisposable
{
    const string CONFIG = """{"repos":[]}""";

    const string BASE_COMMIT = "abc1234";
    const string HEAD_COMMIT = "def5678";

    const string SUPERVISORS_WORDS = "Check F1 and F3 only. F2 was accepted as stated.";
    const string IMPLEMENTERS_WORDS = "F1: the guard now runs above the write. F3: the second formatter is deleted.";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;

    public RoutedReportSweepTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-routed-sweep-{Guid.NewGuid():N}");
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
    /// THE WHOLE ROUND IN ONE CASE: the supervisor's verdict carries a contract, the implementer
    /// declares a fix on a commit, and the reviewer finds a brief in its own channel — signed by the
    /// APP, because members never write in each other's channels, and carrying the delta between the
    /// two commits nobody had to ask a supervisor for.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ASupervisorContractPlusAFixReportPutsABriefInTheReviewersChannel()
    {
        var orchId = _launcher.Start_Orchestration("Repo", _tempRepo).OrchId;

        Declare_Contract(orchId, "imp-1", $"{ChannelGrammar.REROUTE} rev-1 from {BASE_COMMIT}\n{SUPERVISORS_WORDS}");
        File_FixReport(orchId, "imp-1", $"{IMPLEMENTERS_WORDS}\n{ChannelGrammar.FIXED} {HEAD_COMMIT}");

        var got = await Run_Until_Async(() => Find_Relay_OrNull(orchId, "rev-1") != null, BridgeTestTiming.Window_ForTicks(10));

        Assert.True(got, "the reviewer was never handed the round — the sweep wrote no relay");

        var relay = Find_Relay_OrNull(orchId, "rev-1")!;

        Assert.Equal(ChannelAuthors.App, relay.Author);
        Assert.True(AppEntryAudience_Tag.Is_AgentTagged(relay.Subject.TrimStart()), $"the relay is not agent-tagged, so it would be texted to the owner: '{relay.Subject}'");
        Assert.True(RoutedReport_Tag.Is_Routed(relay.Subject), $"the relay carries no [routed] tag, so the reviewer cannot tell it from the app's bookkeeping: '{relay.Subject}'");
        Assert.Contains($"git diff {BASE_COMMIT}..{HEAD_COMMIT}", relay.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// AND IT CARRIES BOTH SIDES' OWN WORDS. The app composes no judgement: what the reviewer reads
    /// is the supervisor's brief and the implementer's report, verbatim, and nothing about the work
    /// that the app made up.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheReviewersBriefCarriesTheSupervisorsWordsAndTheImplementersWords()
    {
        var orchId = _launcher.Start_Orchestration("Repo", _tempRepo).OrchId;

        Declare_Contract(orchId, "imp-1", $"{ChannelGrammar.REROUTE} rev-1 from {BASE_COMMIT}\n{SUPERVISORS_WORDS}");
        File_FixReport(orchId, "imp-1", $"{IMPLEMENTERS_WORDS}\n{ChannelGrammar.FIXED} {HEAD_COMMIT}");

        var got = await Run_Until_Async(() => Find_Relay_OrNull(orchId, "rev-1") != null, BridgeTestTiming.Window_ForTicks(10));

        Assert.True(got, "no relay was written, so there is nothing to read the words out of");

        var body = Find_Relay_OrNull(orchId, "rev-1")!.Body;

        Assert.Contains(SUPERVISORS_WORDS, body, StringComparison.Ordinal);
        Assert.Contains(IMPLEMENTERS_WORDS, body, StringComparison.Ordinal);
    }

    /// <summary>
    /// NO <c>FIXED:</c> LINE, NOTHING ROUTED — the designed fail-open. The app never reads prose for
    /// meaning: the implementer DECLARES the fix or nothing happens, and the fix report stays
    /// ordinary traffic that wakes the supervisor exactly as it does today.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AFixReportWithNoFixedLineRoutesNothing_WhileTheOneBesideItDoes()
    {
        var quiet = _launcher.Start_Orchestration("Quiet", _tempRepo).OrchId;
        var live = _launcher.Start_Orchestration("Live", _tempRepo).OrchId;

        Declare_Contract(quiet, "imp-1", $"{ChannelGrammar.REROUTE} rev-1 from {BASE_COMMIT}\n{SUPERVISORS_WORDS}");
        File_FixReport(quiet, "imp-1", $"{IMPLEMENTERS_WORDS} It is all on the branch.");

        Declare_Contract(live, "imp-1", $"{ChannelGrammar.REROUTE} rev-1 from {BASE_COMMIT}\n{SUPERVISORS_WORDS}");
        File_FixReport(live, "imp-1", $"{IMPLEMENTERS_WORDS}\n{ChannelGrammar.FIXED} {HEAD_COMMIT}");

        var control = await Run_Until_Async(() => Find_Relay_OrNull(live, "rev-1") != null, BridgeTestTiming.Window_ForTicks(10));

        Assert.True(control, "the control orchestration never got its relay, so the silence beside it means nothing");
        Assert.Null(Find_Relay_OrNull(quiet, "rev-1"));
    }

    /// <summary>
    /// A FIX REPORT FILED BEFORE THE CONTRACT ROUTES NOTHING — and this is the case that pins the
    /// ORDER the judge is handed. <c>FixReport_Matcher.Find</c> trusts the name of its parameter: it
    /// reads "after the declaration" as "later in this list", so a sweep that sorted, reversed or
    /// merged the entries would make this report look like an answer to a contract that did not exist
    /// when it was written. Under file order it is refused, and under any other order it routes.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AFixReportFiledBeforeTheContractRoutesNothing_WhileTheOneBesideItDoes()
    {
        var quiet = _launcher.Start_Orchestration("Quiet", _tempRepo).OrchId;
        var live = _launcher.Start_Orchestration("Live", _tempRepo).OrchId;

        File_FixReport(quiet, "imp-1", $"{IMPLEMENTERS_WORDS}\n{ChannelGrammar.FIXED} {HEAD_COMMIT}");
        Declare_Contract(quiet, "imp-1", $"{ChannelGrammar.REROUTE} rev-1 from {BASE_COMMIT}\n{SUPERVISORS_WORDS}");

        Declare_Contract(live, "imp-1", $"{ChannelGrammar.REROUTE} rev-1 from {BASE_COMMIT}\n{SUPERVISORS_WORDS}");
        File_FixReport(live, "imp-1", $"{IMPLEMENTERS_WORDS}\n{ChannelGrammar.FIXED} {HEAD_COMMIT}");

        var control = await Run_Until_Async(() => Find_Relay_OrNull(live, "rev-1") != null, BridgeTestTiming.Window_ForTicks(10));

        Assert.True(control, "the control orchestration never got its relay, so the silence beside it means nothing");
        Assert.Null(Find_Relay_OrNull(quiet, "rev-1"));
    }

    /// <summary>
    /// AND WHEN TWO REPORTS FOLLOW THE CONTRACT, THE LAST ONE IN FILE ORDER IS THE DELTA. The second
    /// half of the ordering claim, from the other side: the head commit the reviewer is asked to diff
    /// to is the one the implementer named LAST, which is only true while the entries arrive as the
    /// file holds them.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheDeltaIsTheLastFixReportInFileOrder()
    {
        var orchId = _launcher.Start_Orchestration("Repo", _tempRepo).OrchId;

        Declare_Contract(orchId, "imp-1", $"{ChannelGrammar.REROUTE} rev-1 from {BASE_COMMIT}\n{SUPERVISORS_WORDS}");
        File_FixReport(orchId, "imp-1", $"first attempt\n{ChannelGrammar.FIXED} 1111111");
        File_FixReport(orchId, "imp-1", $"{IMPLEMENTERS_WORDS}\n{ChannelGrammar.FIXED} {HEAD_COMMIT}");

        var got = await Run_Until_Async(() => Find_Relay_OrNull(orchId, "rev-1") != null, BridgeTestTiming.Window_ForTicks(10));

        Assert.True(got, "no relay was written at all");
        Assert.Contains($"git diff {BASE_COMMIT}..{HEAD_COMMIT}", Find_Relay_OrNull(orchId, "rev-1")!.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// A CONTRACT MUST NAME A REVIEWER. <c>REROUTE: imp-2 from abc1234</c> would have the app hand one
    /// implementer's fix to another implementer as a review — which is not a review, and is also a
    /// member reading a round it was never given. Nothing opens, nothing routes.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ANonReviewerNamedInAContractRoutesNothing_WhileTheOneBesideItDoes()
    {
        var quiet = _launcher.Start_Orchestration("Quiet", _tempRepo).OrchId;
        var live = _launcher.Start_Orchestration("Live", _tempRepo).OrchId;

        _launcher.Add_Implementer(quiet);

        Declare_Contract(quiet, "imp-1", $"{ChannelGrammar.REROUTE} imp-2 from {BASE_COMMIT}\n{SUPERVISORS_WORDS}");
        File_FixReport(quiet, "imp-1", $"{IMPLEMENTERS_WORDS}\n{ChannelGrammar.FIXED} {HEAD_COMMIT}");

        Declare_Contract(live, "imp-1", $"{ChannelGrammar.REROUTE} rev-1 from {BASE_COMMIT}\n{SUPERVISORS_WORDS}");
        File_FixReport(live, "imp-1", $"{IMPLEMENTERS_WORDS}\n{ChannelGrammar.FIXED} {HEAD_COMMIT}");

        var control = await Run_Until_Async(() => Find_Relay_OrNull(live, "rev-1") != null, BridgeTestTiming.Window_ForTicks(10));

        Assert.True(control, "the control orchestration never got its relay, so the silence beside it means nothing");
        Assert.Null(Find_Relay_OrNull(quiet, "imp-2"));
        Assert.Empty(RerouteContract_Store.Read_Open(_paths, quiet));
    }

    /// <summary>
    /// A PAUSED ORCHESTRATION HANDS OUT NO ROUNDS. The relay is the entry that STARTS a reviewer's
    /// turn, so an orchestration the owner put to sleep that still briefed reviewers would be dormant
    /// in name only — CLAUDE.md's PAUSE decision, and the reason this sweep has its own row in
    /// <see cref="PauseGatesEveryWakerScanTests"/>.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task APausedOrchestrationRoutesNothing_WhileTheOneBesideItDoes()
    {
        var asleep = _launcher.Start_Orchestration("Asleep", _tempRepo).OrchId;
        var live = _launcher.Start_Orchestration("Live", _tempRepo).OrchId;

        _store.Set_Paused(asleep, true);

        Declare_Contract(asleep, "imp-1", $"{ChannelGrammar.REROUTE} rev-1 from {BASE_COMMIT}\n{SUPERVISORS_WORDS}");
        File_FixReport(asleep, "imp-1", $"{IMPLEMENTERS_WORDS}\n{ChannelGrammar.FIXED} {HEAD_COMMIT}");

        Declare_Contract(live, "imp-1", $"{ChannelGrammar.REROUTE} rev-1 from {BASE_COMMIT}\n{SUPERVISORS_WORDS}");
        File_FixReport(live, "imp-1", $"{IMPLEMENTERS_WORDS}\n{ChannelGrammar.FIXED} {HEAD_COMMIT}");

        var control = await Run_Until_Async(() => Find_Relay_OrNull(live, "rev-1") != null, BridgeTestTiming.Window_ForTicks(10));

        Assert.True(control, "the live orchestration never got its relay, so the paused one's silence means nothing");
        Assert.Null(Find_Relay_OrNull(asleep, "rev-1"));
    }

    /// <summary>
    /// ONE FIX REPORT IS ONE RELAY, however many ticks run over it. The contract moves to
    /// <c>Routed</c> and the declaration is remembered, so neither the report nor the
    /// <c>REROUTE:</c> line — both of which stay in the channel for ever — can buy a second round.
    /// Without that memory this is a loop at the tick's rate, with the reviewer re-reviewing one
    /// delta until somebody notices.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheSameFixReportIsRoutedOnceAcrossManyTicks()
    {
        var orchId = _launcher.Start_Orchestration("Repo", _tempRepo).OrchId;

        Declare_Contract(orchId, "imp-1", $"{ChannelGrammar.REROUTE} rev-1 from {BASE_COMMIT}\n{SUPERVISORS_WORDS}");
        File_FixReport(orchId, "imp-1", $"{IMPLEMENTERS_WORDS}\n{ChannelGrammar.FIXED} {HEAD_COMMIT}");

        var got = await Run_Until_Async(() => Find_Relay_OrNull(orchId, "rev-1") != null, BridgeTestTiming.Window_ForTicks(10));
        Assert.True(got, "the reviewer was never handed the round");

        await Run_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(12));

        Assert.Equal(1, Count_Relays(orchId, "rev-1"));
    }

    /// <summary>
    /// THE OWNER IS NEVER SHOWN A RELAY. It is one session briefing another through the app, which is
    /// not something the owner can act on (CLAUDE.md decision 15) — so the owner channel is untouched
    /// and the entry carries the agent tag that keeps the mirror off it.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheRelayIsNeverWrittenToTheOwnerChannelAndNeverTexted()
    {
        var orchId = _launcher.Start_Orchestration("Repo", _tempRepo).OrchId;

        Declare_Contract(orchId, "imp-1", $"{ChannelGrammar.REROUTE} rev-1 from {BASE_COMMIT}\n{SUPERVISORS_WORDS}");
        File_FixReport(orchId, "imp-1", $"{IMPLEMENTERS_WORDS}\n{ChannelGrammar.FIXED} {HEAD_COMMIT}");

        var ownerChannelBefore = Read_Text(_paths.Get_OwnerChannelFile(orchId));

        var got = await Run_Until_Async(() => Find_Relay_OrNull(orchId, "rev-1") != null, BridgeTestTiming.Window_ForTicks(10));

        Assert.True(got, "no relay was written, so an untouched owner channel proves nothing");
        Assert.Equal(ownerChannelBefore, Read_Text(_paths.Get_OwnerChannelFile(orchId)));
        Assert.True(
            AppEntryAudience_Tag.Is_AgentTagged(Find_Relay_OrNull(orchId, "rev-1")!.Subject.TrimStart()),
            "the relay is not agent-tagged, so the mirror would put one session's brief to another on the owner's phone");
    }

    /// <summary>
    /// A RELAY THAT WAS NOT WRITTEN MUST NOT ARM A HOLD. The contract moves to <c>Routed</c> only on a
    /// true return from the append, and this is the case that pins it: with the reviewer's channel
    /// held by another writer the entry never lands, and a contract that moved anyway would hold the
    /// supervisor out of a round NOBODY HAD BEEN TOLD ABOUT — the one failure shape this feature can
    /// produce, and the only one that is silent on every surface.
    ///
    /// <para>
    /// THE CONTROL IS THE SAME CONTRACT, AFTERWARDS. "Still Declared" has two routes to it — refused,
    /// or never reached — so the lock is released and the same round routes, which is what makes the
    /// refusal a decision rather than an absence.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ARelayThatCouldNotBeWrittenLeavesTheContractDeclared_AndItRoutesOnceTheChannelIsFree()
    {
        var orchId = _launcher.Start_Orchestration("Repo", _tempRepo).OrchId;

        var lockDirectory = ChannelFile_Lock.Build_LockDirectoryPath(_paths.Get_ImplementerChannelFile(orchId, "rev-1"));
        Directory.CreateDirectory(lockDirectory);
        File.WriteAllText(
            Path.Combine(lockDirectory, ChannelFile_Lock.OWNER_FILE_NAME),
            ChannelFile_Lock.Build_OwnerFileContent(4242, DateTime.UtcNow, "session", "another-writer"));

        Declare_Contract(orchId, "imp-1", $"{ChannelGrammar.REROUTE} rev-1 from {BASE_COMMIT}\n{SUPERVISORS_WORDS}");
        File_FixReport(orchId, "imp-1", $"{IMPLEMENTERS_WORDS}\n{ChannelGrammar.FIXED} {HEAD_COMMIT}");

        var opened = await Run_Until_Async(
            () => RerouteContract_Store.Read_Open(_paths, orchId).Count > 0,
            BridgeTestTiming.Window_ForBlockedTicks(8));

        Assert.True(opened, "the contract was never opened, so its state below proves nothing about the refused append");
        Assert.Equal(RerouteStates.Declared, RerouteContract_Store.Read_Open(_paths, orchId)[0].State);
        Assert.Null(Find_Relay_OrNull(orchId, "rev-1"));

        Directory.Delete(lockDirectory, recursive: true);

        var routed = await Run_Until_Async(() => Find_Relay_OrNull(orchId, "rev-1") != null, BridgeTestTiming.Window_ForTicks(10));

        Assert.True(routed, "the round never routed once the channel was free, so the refusal above was not a refusal");
        Assert.Equal(RerouteStates.Routed, RerouteContract_Store.Read_Open(_paths, orchId)[0].State);
    }

    void Declare_Contract(string orchId, string implementerId, string body)
    {
        Assert.True(
            ChannelAppender.Append_SessionEntry(
                _paths.Get_ImplementerChannelFile(orchId, implementerId), ChannelAuthors.Supervisor, "FIX BRIEF — round 1", body, DateTime.Now),
            $"the fix brief could not be appended to '{orchId}/{implementerId}', so this case would prove nothing");
    }

    void File_FixReport(string orchId, string implementerId, string body)
    {
        Assert.True(
            ChannelAppender.Append_SessionEntry(
                _paths.Get_ImplementerChannelFile(orchId, implementerId), ChannelAuthors.Implementer, "FIX REPORT", body, DateTime.Now),
            $"the fix report could not be appended to '{orchId}/{implementerId}', so this case would prove nothing");
    }

    IChannelEntry? Find_Relay_OrNull(string orchId, string reviewerId)
    {
        return Read_Relays(orchId, reviewerId).LastOrDefault();
    }

    int Count_Relays(string orchId, string reviewerId)
    {
        return Read_Relays(orchId, reviewerId).Count;
    }

    List<IChannelEntry> Read_Relays(string orchId, string reviewerId)
    {
        return
        [
            .. ChannelHistory_Cache
                .Read_Entries(_paths.Get_ImplementerChannelFile(orchId, reviewerId))
                .Where(entry => entry.Author == ChannelAuthors.App && RoutedReport_Tag.Is_Routed(entry.Subject)),
        ];
    }

    static string Read_Text(string path)
    {
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
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
