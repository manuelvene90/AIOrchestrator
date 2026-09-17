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
/// THE WAYS OUT. A routed contract HOLDS the fix report off the supervisor's turn, so a contract that
/// cannot end is a supervisor kept out of a round with nothing anywhere saying so — the one silent
/// failure this feature can produce. Every exit here releases that hold, and what differs is only
/// whether anybody is told.
///
/// <para>
/// THE HOLD IS <c>RerouteContract_Store.Read_Open</c>. <see cref="RoutedHold_Policy.Resolve_RidingOnly"/>
/// derives the held identities from exactly that list and nothing else, so "the contract is gone from
/// the file" IS "the hold is released" — and every case below reads the contract while it is still
/// there before asserting that it went, because an empty list on its own has two routes to it
/// (CLAUDE.md decision 20).
/// </para>
/// <para>
/// BACKDATED THROUGH THE CONTRACT FILE, never by sleeping: a test that waits ninety minutes is not a
/// test, and the bridge harness offers no clock seam fine enough to fake one.
/// </para>
/// </summary>
public class RoutedReportCapTests : IDisposable
{
    const string CONFIG = """{"repos":[]}""";

    const string BASE_COMMIT = "abc1234";
    const string HEAD_COMMIT = "def5678";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;

    public RoutedReportCapTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-routed-cap-{Guid.NewGuid():N}");
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
    /// THE ORDINARY EXIT, and the whole round driven end to end: the contract is declared, the fix
    /// report routes, the reviewer reports, and the contract closes. The supervisor then takes ONE
    /// turn carrying both the findings and the fix report it was never woken for.
    ///
    /// <para>
    /// AND NOTHING IS APPENDED FOR IT. The re-review is itself the entry that wakes the supervisor, so
    /// an alarm here would be a second notification about a round that is going exactly to plan.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AReReviewClosesTheContractAndReleasesTheHold()
    {
        var orchId = _launcher.Start_Orchestration("Repo", _tempRepo).OrchId;

        Append(_paths.Get_ImplementerChannelFile(orchId, "imp-1"), ChannelAuthors.Supervisor, "FIX BRIEF", $"{ChannelGrammar.REROUTE} rev-1 from {BASE_COMMIT}\nF1 and F3 only.");
        Append(_paths.Get_ImplementerChannelFile(orchId, "imp-1"), ChannelAuthors.Implementer, "FIX REPORT", $"both fixed\n{ChannelGrammar.FIXED} {HEAD_COMMIT}");

        var routed = await Run_Until_Async(() => Read_Routed_OrNull(orchId) != null, BridgeTestTiming.Window_ForTicks(10));

        Assert.True(routed, "the contract never reached Routed, so there is no hold here to release");

        var ownerChannelWhileHeld = Read_Text(_paths.Get_OwnerChannelFile(orchId));

        Append(_paths.Get_ImplementerChannelFile(orchId, "rev-1"), ChannelAuthors.Reviewer, "RE-REVIEW", "F1 verified, F3 verified. One LOW remains.");

        var closed = await Run_Until_Async(() => RerouteContract_Store.Read_Open(_paths, orchId).Count == 0, BridgeTestTiming.Window_ForTicks(10));

        Assert.True(closed, "the reviewer reported and the contract stayed open — the fix report is held off the supervisor's turn for ever");
        Assert.Equal(ownerChannelWhileHeld, Read_Text(_paths.Get_OwnerChannelFile(orchId)));
    }

    /// <summary>
    /// AND THE REVIEWER'S OWN EARLIER ENTRIES DO NOT COUNT. The question is "has it filed anything
    /// since the RELAY", anchored on that entry's identity in file order — a rule that read the
    /// channel as a whole, or counted its entries, would answer "yes" the moment the round was routed
    /// to a reviewer that had ever spoken, which every reviewer has: its own boot announcement is an
    /// entry.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AnEntryTheReviewerFiledBeforeTheRelayDoesNotCloseTheContract()
    {
        var orchId = _launcher.Start_Orchestration("Repo", _tempRepo).OrchId;

        Append(_paths.Get_ImplementerChannelFile(orchId, "rev-1"), ChannelAuthors.Reviewer, "rev-1 online", "Round one findings: F1, F2, F3.");

        Append(_paths.Get_ImplementerChannelFile(orchId, "imp-1"), ChannelAuthors.Supervisor, "FIX BRIEF", $"{ChannelGrammar.REROUTE} rev-1 from {BASE_COMMIT}\nF1 and F3 only.");
        Append(_paths.Get_ImplementerChannelFile(orchId, "imp-1"), ChannelAuthors.Implementer, "FIX REPORT", $"both fixed\n{ChannelGrammar.FIXED} {HEAD_COMMIT}");

        var routed = await Run_Until_Async(() => Read_Routed_OrNull(orchId) != null, BridgeTestTiming.Window_ForTicks(10));
        Assert.True(routed, "the contract never reached Routed, so this case would prove nothing");

        await Run_Until_Async(() => RerouteContract_Store.Read_Open(_paths, orchId).Count == 0, BridgeTestTiming.Window_ForTicks(10));

        Assert.NotNull(Read_Routed_OrNull(orchId));

        // THE LIVE CONTROL, in the same run and on the same contract: the reviewer files NOW, and it
        // closes. Without it the silence above could equally mean the closing step never ran.
        Append(_paths.Get_ImplementerChannelFile(orchId, "rev-1"), ChannelAuthors.Reviewer, "RE-REVIEW", "F1 verified, F3 verified.");

        var closed = await Run_Until_Async(() => RerouteContract_Store.Read_Open(_paths, orchId).Count == 0, BridgeTestTiming.Window_ForTicks(10));

        Assert.True(closed, "the closing step never reached this contract at all, so the case above proves nothing");
    }

    /// <summary>
    /// THE CAP, AND IT SAYS SO ONCE. Past <see cref="RerouteContract_Policy.REVIEW_CAP"/> the hold is
    /// released — the fix report reaches the supervisor on the ordinary digest, exactly as it would
    /// have without this feature — and ONE entry names the reviewer that never reported. A second one
    /// would be decision 14's waterfall pointed at a supervisor.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ACapExpiryReleasesTheHoldAndSaysSoOnce()
    {
        var orchId = _launcher.Start_Orchestration("Repo", _tempRepo).OrchId;

        Store_RoutedContract(orchId, "imp-1", "rev-1", DateTime.UtcNow - RerouteContract_Policy.REVIEW_CAP - TimeSpan.FromMinutes(30));

        Assert.Single(RerouteContract_Store.Read_Open(_paths, orchId));

        var released = await Run_Until_Async(() => RerouteContract_Store.Read_Open(_paths, orchId).Count == 0, BridgeTestTiming.Window_ForTicks(12));

        Assert.True(released, "the cap never expired the contract — the supervisor is held out of this round for ever");
        Assert.Equal(1, Count_OwnerChannelEntriesNaming(orchId, "rev-1"));

        await Run_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(12));

        Assert.Equal(1, Count_OwnerChannelEntriesNaming(orchId, "rev-1"));
    }

    /// <summary>
    /// A CONTRACT STILL INSIDE THE CAP IS NOT TOUCHED — the control for the case above, which on its
    /// own would pass with the clock ignored and every routed contract expired on sight.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AContractStillInsideTheCapIsLeftAlone_WhileTheExpiredOneBesideItGoes()
    {
        var young = _launcher.Start_Orchestration("Young", _tempRepo).OrchId;
        var old = _launcher.Start_Orchestration("Old", _tempRepo).OrchId;

        Store_RoutedContract(young, "imp-1", "rev-1", DateTime.UtcNow - TimeSpan.FromMinutes(5));
        Store_RoutedContract(old, "imp-1", "rev-1", DateTime.UtcNow - RerouteContract_Policy.REVIEW_CAP - TimeSpan.FromMinutes(30));

        var control = await Run_Until_Async(() => RerouteContract_Store.Read_Open(_paths, old).Count == 0, BridgeTestTiming.Window_ForTicks(12));

        Assert.True(control, "the expired contract was not released, so the young one surviving beside it means nothing");
        Assert.Single(RerouteContract_Store.Read_Open(_paths, young));
        Assert.Equal(0, Count_OwnerChannelEntriesNaming(young, "rev-1"));
    }

    /// <summary>
    /// A REVIEWER THAT IS GONE ENDS THE ROUND WITHOUT AN ALARM. Nobody is coming, so the hold is
    /// released at once rather than ninety minutes later — and nothing is appended, because the
    /// supervisor learns it from the fix report it is now handed, and it is the one who closed the
    /// member.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AClosedReviewerClosesTheContractWithoutAnAlarm()
    {
        var orchId = _launcher.Start_Orchestration("Repo", _tempRepo).OrchId;

        Store_RoutedContract(orchId, "imp-1", "rev-1", DateTime.UtcNow - TimeSpan.FromMinutes(5));
        _store.Close_Member(orchId, "rev-1");

        Assert.Single(RerouteContract_Store.Read_Open(_paths, orchId));

        var released = await Run_Until_Async(() => RerouteContract_Store.Read_Open(_paths, orchId).Count == 0, BridgeTestTiming.Window_ForTicks(12));

        Assert.True(released, "the reviewer is gone and the hold stayed armed — nothing will ever satisfy it");
        Assert.Equal(0, Count_OwnerChannelEntriesNaming(orchId, "rev-1"));
    }

    /// <summary>
    /// AND AN EXPIRED CONTRACT IS NOT RE-OPENED FROM THE DECLARATION THAT IS STILL IN THE CHANNEL.
    /// The <c>REROUTE:</c> line lives in the implementer's channel for ever and this sweep reads that
    /// channel every tick, so a round that is merely REMOVED comes back on the next pass, re-matches
    /// the same fix report and re-briefs the reviewer — a loop at the tick's rate. The handled list is
    /// what makes "this round is over" outlive the row it closed.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AClosedRoundIsNeverReopenedByTheDeclarationStillInTheChannel()
    {
        var orchId = _launcher.Start_Orchestration("Repo", _tempRepo).OrchId;

        Append(_paths.Get_ImplementerChannelFile(orchId, "imp-1"), ChannelAuthors.Supervisor, "FIX BRIEF", $"{ChannelGrammar.REROUTE} rev-1 from {BASE_COMMIT}\nF1 and F3 only.");
        Append(_paths.Get_ImplementerChannelFile(orchId, "imp-1"), ChannelAuthors.Implementer, "FIX REPORT", $"both fixed\n{ChannelGrammar.FIXED} {HEAD_COMMIT}");

        var routed = await Run_Until_Async(() => Read_Routed_OrNull(orchId) != null, BridgeTestTiming.Window_ForTicks(10));
        Assert.True(routed, "the contract never reached Routed, so there is no closed round to re-open");

        Append(_paths.Get_ImplementerChannelFile(orchId, "rev-1"), ChannelAuthors.Reviewer, "RE-REVIEW", "F1 verified, F3 verified.");

        var closed = await Run_Until_Async(() => RerouteContract_Store.Read_Open(_paths, orchId).Count == 0, BridgeTestTiming.Window_ForTicks(10));
        Assert.True(closed, "the contract never closed, so nothing here could be re-opened");

        await Run_Until_Async(() => RerouteContract_Store.Read_Open(_paths, orchId).Count > 0, BridgeTestTiming.Window_ForTicks(12));

        Assert.Empty(RerouteContract_Store.Read_Open(_paths, orchId));
        Assert.Equal(1, Count_Relays(orchId, "rev-1"));
    }

    void Store_RoutedContract(string orchId, string implementerId, string reviewerId, DateTime routedUtc)
    {
        var declared = RerouteContract_Factory.Create_Declared(
            id: $"contract-{implementerId}-{reviewerId}",
            orchId: orchId,
            implementerId: implementerId,
            reviewerId: reviewerId,
            baseCommit: BASE_COMMIT,
            brief: "F1 and F3 only.",
            declaredUtc: routedUtc);

        RerouteContract_Store.Write_Open(
            _paths,
            orchId,
            [RerouteContract_Factory.CreateFrom_Routed(declared, reportIdentity: "9f2a1c0000000000", headCommit: HEAD_COMMIT, routedUtc: routedUtc)]);
    }

    IRerouteContract? Read_Routed_OrNull(string orchId)
    {
        return RerouteContract_Store.Read_Open(_paths, orchId).FirstOrDefault(contract => contract.State == RerouteStates.Routed);
    }

    void Append(string channelFile, ChannelAuthors author, string subject, string body)
    {
        Assert.True(
            ChannelAppender.Append_SessionEntry(channelFile, author, subject, body, DateTime.Now),
            $"'{subject}' could not be appended to {Path.GetFileName(channelFile)}, so this case would prove nothing");
    }

    int Count_OwnerChannelEntriesNaming(string orchId, string memberId)
    {
        return ChannelHistory_Cache
            .Read_Entries(_paths.Get_OwnerChannelFile(orchId))
            .Count(entry => entry.Author == ChannelAuthors.App && entry.Subject.Contains(memberId, StringComparison.Ordinal));
    }

    int Count_Relays(string orchId, string reviewerId)
    {
        return ChannelHistory_Cache
            .Read_Entries(_paths.Get_ImplementerChannelFile(orchId, reviewerId))
            .Count(entry => entry.Author == ChannelAuthors.App && RoutedReport_Tag.Is_Routed(entry.Subject));
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
