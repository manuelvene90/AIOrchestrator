using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Mirroring;
using AIOrchestratorCoreLib.Bridge.PeriodicStatus;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationMember;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using AIOrchestratorCoreLib.Usage;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE ENGINE'S HALF OF THE NO-CHANGE GUARD (plan 03 Task 8, ruling R6) — the comparison form of the
/// periodic status leaves out exactly the readings a status wake moves by itself.
///
/// <para>
/// WHY THIS NEEDS THE REAL ENGINE. <c>PeriodicStatusSweepTests</c> proves the sweep compares the stable
/// form and posts the full one, against a stub. What goes INTO the stable form is the engine's roster
/// builder, and it is where the loop would come back: the status is appended to the owner channel, a
/// terminal supervisor's (or solo's) watcher wakes on it, the wake grows that session's context
/// figure — and if the figure were in the comparison, the next status would read as changed, post,
/// wake it again, and lock into the 30-minute limit cycle <c>AwayDigest_Decider</c> records from
/// 2026-08-18/19. The engine is reached through the host interface it implements, the same call the
/// sweep makes; the harness is <c>OwnerAnswerSurvivesFailedSendTests</c>' constructor.
/// </para>
/// </summary>
public class AStatusWakeDoesNotChangeTheStatusTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;

    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;
    readonly IPeriodicStatusHost _host;

    public AStatusWakeDoesNotChangeTheStatusTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-status-wake-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tempRoot, "repo"));

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID}}}");

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        var store = OrchestrationSessionStore_Factory.Create(_paths);
        var log = new RecordingLog_Fake();
        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, store, new RecordingSpawner_Fake(), log);

        var engine = BridgeEngine_Factory.Create_WithTelegramClient(_paths, configProvider, store, launcher, log, new FailableTelegram_Fake(), BridgeTestTiming.Fast());

        _host = Assert.IsAssignableFrom<IPeriodicStatusHost>(engine);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A SUPERVISED ORCHESTRATION. The supervisor reads the owner channel, so its figure is left out
    /// of the comparison; an implementer's watcher reads its own channel, so a status never wakes it
    /// and its figure (over the digest's 80% threshold here) stays in — it only moves when it worked.
    /// "last wrote N min ago" is out for every member: it moves by the clock alone.
    /// </summary>
    [Fact]
    public void Build_MemberStatus_LeavesTheSupervisorsFigureAndTheElapsedTimesOutOfTheComparison()
    {
        var session = Create_Session("orch-sup", supervised: true, "imp-1");
        Write_Usage(_paths.Get_OrchestrationFolder("orch-sup"), 41);
        Write_Usage(_paths.Get_ImplementerFolder("orch-sup", "imp-1"), 85);
        Write_MemberChannel("orch-sup", "imp-1");

        var posted = _host.Build_MemberStatus(session, previous: null, withVolatileReadings: true);
        var key = _host.Build_MemberStatus(session, previous: null, withVolatileReadings: false);

        // The owner reads all of it.
        Assert.Contains("ctx 41%", posted, StringComparison.Ordinal);
        Assert.Contains("ctx 85%", posted, StringComparison.Ordinal);
        Assert.Contains("last wrote", posted, StringComparison.Ordinal);

        Assert.DoesNotContain("ctx 41%", key, StringComparison.Ordinal);
        Assert.DoesNotContain("last wrote", key, StringComparison.Ordinal);
        Assert.Contains("ctx 85%", key, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE WAKE ITSELF, as the claim: the supervisor woke on the status and its context grew from 41%
    /// to 47%. The owner's next status would say 47%; the comparison form must not have moved, or the
    /// status that caused the wake is what makes the next one post.
    /// </summary>
    [Fact]
    public void AWakeThatOnlyGrowsTheSupervisorsContext_LeavesTheComparisonFormAsItWas()
    {
        var session = Create_Session("orch-wake", supervised: true, "imp-1");
        var supervisorFolder = _paths.Get_OrchestrationFolder("orch-wake");
        Write_Usage(supervisorFolder, 41);

        var postedBefore = _host.Build_MemberStatus(session, previous: null, withVolatileReadings: true);
        var keyBefore = _host.Build_MemberStatus(session, previous: null, withVolatileReadings: false);

        Write_Usage(supervisorFolder, 47);

        Assert.NotEqual(postedBefore, _host.Build_MemberStatus(session, previous: null, withVolatileReadings: true));
        Assert.Equal(keyBefore, _host.Build_MemberStatus(session, previous: null, withVolatileReadings: false));
    }

    /// <summary>
    /// A BASIC ORCHESTRATION'S SOLO IS THE SUPERVISOR'S CASE, not a member's: its channel IS the owner
    /// channel (<c>MemberChannel_Locator</c>), so the status wakes a terminal solo exactly as it wakes a
    /// supervisor. Its figure — always shown, the supervisor's rule — is in the posted text and out of
    /// the comparison.
    /// </summary>
    [Fact]
    public void Build_MemberStatus_LeavesTheSolosFigureOutOfTheComparison()
    {
        var session = Create_Session("orch-solo", supervised: false, "solo-1");
        Write_Usage(_paths.Get_ImplementerFolder("orch-solo", "solo-1"), 33);

        var posted = _host.Build_MemberStatus(session, previous: null, withVolatileReadings: true);
        var key = _host.Build_MemberStatus(session, previous: null, withVolatileReadings: false);

        Assert.Contains("- solo-1:", posted, StringComparison.Ordinal);
        Assert.Contains("ctx 33%", posted, StringComparison.Ordinal);

        Assert.Contains("- solo-1:", key, StringComparison.Ordinal);
        Assert.DoesNotContain("ctx 33%", key, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE WORKING STATE IS THE WAKE'S TOO (review of 8d548f0, 2026-09-23). A status wakes a terminal
    /// supervisor or solo, and a wake whose turn is still running at the next boundary reads "working
    /// now — editing X" where the last status read "idle — waiting". Easy at the 5-minute minimum
    /// interval. With that state in the comparison form, the next status posts and wakes it again.
    /// Working here is what the terminal probe reads: a status-line file written in the last two minutes.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnOwnerFacingSessionFlippingBetweenIdleAndWorking_LeavesTheComparisonFormAsItWas(bool supervised)
    {
        var (session, usageFolder) = Create_OwnerFacing(supervised ? "orch-flip-key-sup" : "orch-flip-key-solo", supervised);

        Write_Usage(usageFolder, 40);
        Set_Idle(usageFolder);

        var postedIdle = _host.Build_MemberStatus(session, previous: null, withVolatileReadings: true);
        var keyIdle = _host.Build_MemberStatus(session, previous: null, withVolatileReadings: false);

        Write_Usage(usageFolder, 40);

        var postedWorking = _host.Build_MemberStatus(session, previous: null, withVolatileReadings: true);

        // The owner is told: the flip is real in the posted form, or this test proves nothing.
        Assert.Contains("working now", postedWorking, StringComparison.Ordinal);
        Assert.DoesNotContain("working now", postedIdle, StringComparison.Ordinal);

        Assert.Equal(keyIdle, _host.Build_MemberStatus(session, previous: null, withVolatileReadings: false));
    }

    /// <summary>
    /// THE SAME CLAIM THROUGH THE SWEEP, with the REAL engine as its host, so the posts are real appends
    /// to a real owner channel. The owner-facing session flips idle, working, idle, working across four
    /// boundaries, and its context grows on every wake: nothing is posted. Then a ledger line is
    /// ticked, which is real news, and exactly one status goes out.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnOwnerFacingSessionFlippingBetweenIdleAndWorking_PostsNothingNew(bool supervised)
    {
        var orchId = supervised ? "orch-flip-sweep-sup" : "orch-flip-sweep-solo";
        var (session, usageFolder) = Create_OwnerFacing(orchId, supervised);
        var noon = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Local);

        File.WriteAllText(_paths.Get_PlanFile(orchId), "# PLAN\n\n- [x] 1. map\n- [>] 2. build\n- [ ] 3. ship\n");
        Write_Usage(usageFolder, 40);
        Set_Idle(usageFolder);

        var phone = OrchestratorConfig_Loader.Load_OrEmpty(_paths).Phone;
        Assert.True(phone.PeriodicStatus);

        var sweep = PeriodicStatusSweep_Factory.Create();

        // First sight: sends nothing, remembers what it would have said.
        await sweep.Push_Async(noon.AddMinutes(7), false, phone, [session], _host, CancellationToken.None);

        var context = 40;

        for (var boundary = 1; boundary <= 4; boundary++)
        {
            context += 3;
            Write_Usage(usageFolder, context);

            if (boundary % 2 == 0)
                Set_Idle(usageFolder);

            await sweep.Push_Async(noon.AddMinutes(30 * boundary), false, phone, [session], _host, CancellationToken.None);
        }

        Assert.Equal(0, Count_StatusEntries(orchId));

        File.WriteAllText(_paths.Get_PlanFile(orchId), "# PLAN\n\n- [x] 1. map\n- [x] 2. build\n- [>] 3. ship\n");

        await sweep.Push_Async(noon.AddMinutes(150), false, phone, [session], _host, CancellationToken.None);

        Assert.Equal(1, Count_StatusEntries(orchId));
    }

    (IOrchestrationSession Session, string UsageFolder) Create_OwnerFacing(string orchId, bool supervised)
    {
        var memberId = supervised ? "imp-1" : "solo-1";
        var session = Create_Session(orchId, supervised, memberId);

        var usageFolder = supervised
            ? _paths.Get_OrchestrationFolder(orchId)
            : _paths.Get_ImplementerFolder(orchId, memberId);

        return (session, usageFolder);
    }

    /// <summary>Older than the probe's two-minute mid-turn window, well inside the status interval.</summary>
    static void Set_Idle(string usageFolder)
    {
        File.SetLastWriteTimeUtc(Path.Combine(usageFolder, UsageTotals_Reader.SESSION_USAGE_FILE), DateTime.UtcNow.AddMinutes(-10));
    }

    int Count_StatusEntries(string orchId)
    {
        var ownerChannel = _paths.Get_OwnerChannelFile(orchId);

        if (!File.Exists(ownerChannel))
            return 0;

        return ChannelHistory_Counter.Read_AllEntries(ownerChannel)
            .Count(entry => entry.Author == ChannelAuthors.App && MirrorText_Formatter.Is_StatusEntry(entry));
    }

    IOrchestrationSession Create_Session(string orchId, bool supervised, string memberId)
    {
        Directory.CreateDirectory(_paths.Get_OrchestrationFolder(orchId));
        Directory.CreateDirectory(_paths.Get_ImplementerFolder(orchId, memberId));

        return OrchestrationSession_Factory.Create(
            orchId, "repo", Path.Combine(_tempRoot, "repo"), DateTime.UtcNow.AddHours(-2),
            4242, null,
            supervised ? DateTime.UtcNow.AddHours(-2) : null,
            null, orchId, null, null,
            [OrchestrationMember_Factory.Create(memberId, null, DateTime.UtcNow.AddHours(-1))],
            TelegramDeliveryModes.Normal, null);
    }

    void Write_MemberChannel(string orchId, string memberId)
    {
        File.WriteAllText(
            _paths.Get_ImplementerChannelFile(orchId, memberId),
            $"## [1] FROM {memberId} — 2026-09-23 12:00 — {memberId} online\n\nready\n");
    }

    /// <summary>The live status-line probe's shape, trimmed to the field the context reader looks at.</summary>
    static void Write_Usage(string folder, int usedPercent)
    {
        File.WriteAllText(
            Path.Combine(folder, UsageTotals_Reader.SESSION_USAGE_FILE),
            "{\"session_id\":\"277a9896\",\"model\":{\"id\":\"claude-opus-5\",\"display_name\":\"Opus 5\"},"
            + "\"context_window\":{\"context_window_size\":1000000,"
            + $"\"used_percentage\":{usedPercent},\"remaining_percentage\":{100 - usedPercent}}}}}");
    }
}
