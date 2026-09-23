using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Limits.ClaudeAccount;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using AIOrchestratorCoreLib.Usage;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE TWO WAYS SESSIONS STAYED DOWN AFTER A USAGE LIMIT, driven through the real engine (owner,
/// 2026-09-23).
///
/// <para>
/// ONE — AN ACCOUNT SWITCH DID NOT COUNT. The owner runs three Claude accounts and logs in to another
/// when one runs out. At 05:01 the weekly window of the first reached 100% and dispatch paused until
/// Tue 29 Sep; at 05:03 they logged in to a second account; every restart after that restored the
/// six-day pause and brought no session back.
/// </para>
/// <para>
/// TWO — A SESSION STUCK ON THE LIMIT WAS NEVER RESTARTED. The interactive CLI can sit at its limit
/// menu, or at "press enter to continue" after a long sleep, well past the reset. The app knew the
/// session was blocked and did nothing with it when the allowance came back.
/// </para>
/// </summary>
[Collection(AIOrchestratorCoreLib.Tests.Channels.CHANNEL_LOCK_COLLECTION.NAME)]
public class LimitAccountSwitchAndRescueTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const string WEEKLY_REASON = "the rate_limits.seven_day.used_percentage window was at 100%";
    const string ACCOUNT_A = "aaaaaaaa-0000-0000-0000-000000000001";
    const string ACCOUNT_B = "bbbbbbbb-0000-0000-0000-000000000002";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly string _claudeGlobalConfig;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IOrchestratorConfigProvider _configProvider;
    readonly IEngineStateStore _engineState = EngineStateStore_Factory.Create_InMemory();
    readonly RecordingLog_Fake _log = new();
    readonly CapturingTelegram_Fake _telegram = new();
    readonly RecordingSpawner_Fake _spawner = new();

    /// <summary>Every seeded slot reads as running, and a stop is recorded rather than performed.</summary>
    readonly SessionProcesses_Fake _processes = new() { EverythingAlive = true };

    /// <summary>At the wall clock: the usage-probe reader filters windows on <c>DateTime.Now</c>.</summary>
    readonly FixedClock_Fake _clock = new(DateTime.UtcNow);

    public LimitAccountSwitchAndRescueTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-limitaccount-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID}}}");

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _claudeGlobalConfig = Path.Combine(_tempRoot, ".claude.json");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        _launcher = OrchestrationLauncher_Factory.Create(_paths, _configProvider, _store, _spawner, _log);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that will not delete is not a test result.
        }
    }

    /// <summary>
    /// THE REGRESSION: the pause belongs to account A, B is logged in now. The pause lifts on the first
    /// tick, the owner is told why, sessions spawn — and A's 100% probe, written before the switch,
    /// does not pause dispatch again once the probe throttle lets it be read.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AnotherAccountLoggedIn_LiftsThePreviousAccountsPause_AndItsOldProbesDoNotRepause()
    {
        var resetsAtUtc = _clock.UtcNow.AddDays(6);
        Seed_Pause(resetsAtUtc, ACCOUNT_A);
        Write_UsageProbe("seven_day", 100, resetsAtUtc, writtenUtc: _clock.UtcNow.AddHours(-1));
        Log_In(ACCOUNT_B);
        var engine = Create_Engine();

        Assert.True(
            await Run_Until_Async(engine, () => _spawner.SpawnedCommands.Count > 0, 20_000),
            "THE DEFECT: the owner logged in to another account and the previous account's pause still "
            + "kept every session down."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}"
            + $"{Environment.NewLine}Sent:{Environment.NewLine}{_telegram.Dump_Sent()}");

        Assert.True(_telegram.Has_Sent_Containing("different Claude account"), _telegram.Dump_Sent());
        Assert.False(_telegram.Has_Sent_Containing("still PAUSED"), _telegram.Dump_Sent());

        var state = _engineState.Load_OrEmpty();
        Assert.Null(state.DispatchPausedUntilUtc);
        Assert.Equal(ACCOUNT_B, state.LimitAccountId);
        Assert.NotNull(state.LimitAccountSinceUtc);

        // Past the probe-reading throttle, with A's reading still at 100% on disk.
        _clock.Advance(TimeSpan.FromMinutes(2));
        await Run_Until_Async(engine, () => false, BridgeTestTiming.Window_ForTicks(10));

        Assert.Null(_engineState.Load_OrEmpty().DispatchPausedUntilUtc);
        Assert.False(_log.Has_Line_Containing("Dispatch PAUSED"), _log.Dump());
    }

    /// <summary>
    /// The positive control for the floor above: a probe written AFTER the switch is the new account's
    /// own reading, and at 100% it pauses like any other.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheNewAccountsOwnFullReading_StillPauses()
    {
        Seed_Pause(_clock.UtcNow.AddDays(6), ACCOUNT_A);
        Log_In(ACCOUNT_B);
        var engine = Create_Engine();

        Assert.True(
            await Run_Until_Async(engine, () => _telegram.Has_Sent_Containing("different Claude account"), 20_000),
            $"setup: the switch was not seen.{Environment.NewLine}{_log.Dump()}");

        var newResetUtc = _clock.UtcNow.AddHours(3);
        Write_UsageProbe("five_hour", 100, newResetUtc, writtenUtc: _clock.UtcNow.AddMinutes(1));
        _clock.Advance(TimeSpan.FromMinutes(2));

        Assert.True(
            await Run_Until_Async(engine, () => _log.Has_Line_Containing("Dispatch PAUSED"), 20_000),
            "the new account's own 100% reading was ignored along with the old account's."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");
    }

    /// <summary>The same account still logged in: the pause holds, exactly as before this change.</summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheSameAccountStillLoggedIn_ThePauseHolds()
    {
        Seed_Pause(_clock.UtcNow.AddDays(6), ACCOUNT_A);
        Log_In(ACCOUNT_A);
        var engine = Create_Engine();

        Assert.True(
            await Run_Until_Async(engine, () => _telegram.Has_Sent_Containing("still PAUSED"), 20_000),
            $"the restored pause was not announced.{Environment.NewLine}{_log.Dump()}");

        Assert.Empty(_spawner.SpawnedCommands);
        Assert.False(_telegram.Has_Sent_Containing("different Claude account"), _telegram.Dump_Sent());
    }

    /// <summary>
    /// A state file from before accounts were recorded: the logged-in account is ADOPTED as the pause's
    /// own, because nothing says the pause was anyone else's — so it holds.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task APauseWithNoAccountOnRecord_AdoptsTheCurrentOne_AndHolds()
    {
        Seed_Pause(_clock.UtcNow.AddDays(6), accountId: null);
        Log_In(ACCOUNT_B);
        var engine = Create_Engine();

        Assert.True(
            await Run_Until_Async(engine, () => _telegram.Has_Sent_Containing("still PAUSED"), 20_000),
            $"the restored pause was not announced.{Environment.NewLine}{_log.Dump()}");

        Assert.Empty(_spawner.SpawnedCommands);
        Assert.Equal(ACCOUNT_B, _engineState.Load_OrEmpty().LimitAccountId);
        Assert.Null(_engineState.Load_OrEmpty().LimitAccountSinceUtc);
    }

    /// <summary>
    /// THE OVERNIGHT CASE: a member whose last reply is the CLI's limit refusal, from an hour ago, with
    /// the dispatcher open past the grace. It is stopped so the watchdog restarts it — once, not every
    /// tick.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AMemberStillStuckOnTheLimit_AfterTheGrace_IsStoppedForTheWatchdog_Once()
    {
        var (_, memberId) = Start_WithAMemberWhoseLastReplyIs(Limit_Refusal(_clock.UtcNow.AddHours(-1)));
        var engine = Create_Engine();
        _clock.Advance(TimeSpan.FromMinutes(AIOrchestratorCoreLib.Limits.LimitRescue_Decider.GRACE_MINUTES + 1));

        Assert.True(
            await Run_Until_Async(engine, () => _log.Has_Line_Containing("still stuck on a usage limit"), 20_000),
            "THE DEFECT: a session stuck on the limit was left there after the allowance came back."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}");

        Assert.True(_log.Has_Line_Containing($"{memberId} was still stuck on a usage limit"), _log.Dump());
        Assert.Single(_processes.Killed);

        // Past the check throttle, inside the minimum interval: not stopped a second time.
        _clock.Advance(TimeSpan.FromMinutes(2));
        await Run_Until_Async(engine, () => false, BridgeTestTiming.Window_ForTicks(10));

        Assert.Equal(1, Count_Lines_Containing("still stuck on a usage limit"));
        Assert.Single(_processes.Killed);
    }

    /// <summary>Inside the grace the CLI's own auto-continue gets its turn, and a working member is never touched.</summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task InsideTheGrace_OrWhenNotBlocked_NothingIsStopped()
    {
        Start_WithAMemberWhoseLastReplyIs(Limit_Refusal(_clock.UtcNow.AddHours(-1)));
        var engine = Create_Engine();

        await Run_Until_Async(engine, () => false, BridgeTestTiming.Window_ForTicks(10));
        Assert.False(_log.Has_Line_Containing("still stuck on a usage limit"), _log.Dump());
        Assert.Empty(_processes.Killed);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AMemberWhoseLastReplyIsOrdinary_IsNeverStopped()
    {
        Start_WithAMemberWhoseLastReplyIs(Ordinary_Reply(_clock.UtcNow.AddHours(-1)));
        var engine = Create_Engine();
        _clock.Advance(TimeSpan.FromMinutes(AIOrchestratorCoreLib.Limits.LimitRescue_Decider.GRACE_MINUTES + 1));

        await Run_Until_Async(engine, () => false, BridgeTestTiming.Window_ForTicks(10));
        Assert.False(_log.Has_Line_Containing("still stuck on a usage limit"), _log.Dump());
        Assert.Empty(_processes.Killed);
    }

    /// <summary>
    /// /RESUME WAKES EVERYONE (owner, 2026-09-23 10:00Z: <i>"I sent a /resume command to the gen sup,
    /// and not all got awakened"</i>). It used to wake only by appending GO AHEAD to channels, heard
    /// only by an armed watcher — and at a usage limit every watcher had expired and could not re-arm.
    /// Now the member blocked on the limit is stopped so the watchdog respawns it into the GO AHEAD,
    /// with no grace (the refusal is a minute old), and the member that is working is not touched.
    /// The reply counts restarts against sessions already awake, never "woke N" for appends nobody heard.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task Resume_RestartsTheBlockedMember_AndLeavesTheAwakeOnesAlone()
    {
        var (orchId, memberId) = Start_WithAMemberWhoseLastReplyIs(Limit_Refusal(_clock.UtcNow.AddMinutes(-1)));
        var engine = Create_Engine();

        _telegram.Queue_Updates(Build_GeneralOwnerMessageJson("/resume"));

        Assert.True(
            await Run_Until_Async(engine, () => _telegram.Has_Sent_Containing("restarted"), 20_000),
            "THE DEFECT: /resume did not restart the session blocked on the usage limit."
            + $"{Environment.NewLine}Engine log:{Environment.NewLine}{_log.Dump()}"
            + $"{Environment.NewLine}Sent:{Environment.NewLine}{_telegram.Dump_Sent()}");

        Assert.Equal([_paths.Get_ImplementerPidFile(orchId, memberId)], _processes.Killed);
        Assert.True(_telegram.Has_Sent_Containing("restarted 1"), _telegram.Dump_Sent());
        Assert.True(_telegram.Has_Sent_Containing("3 already awake"), _telegram.Dump_Sent());
        Assert.False(_telegram.Has_Sent_Containing("go ahead sent to"), _telegram.Dump_Sent());
        Assert.True(_log.Has_Line_Containing($"{orchId}/{memberId}"), _log.Dump());
        Assert.True(_log.Has_Line_Containing($"{orchId}/supervisor (awake"), _log.Dump());
    }

    /// <summary>
    /// ONE GO AHEAD PER BASIC ORCHESTRATION. A solo's channel IS the owner channel, so the member loop
    /// used to write the same entry into the file the owner-channel append had just written — "woke 7"
    /// on a morning with four sessions.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task Resume_WritesOneGoAhead_IntoABasicOrchestration()
    {
        var session = _launcher.Start_BasicOrchestration("Repo", _tempRepo);
        var engine = Create_Engine();

        _telegram.Queue_Updates(Build_GeneralOwnerMessageJson("/resume"));

        Assert.True(
            await Run_Until_Async(engine, () => _telegram.Has_Sent_Containing("already awake"), 20_000),
            $"setup: /resume did not reply.{Environment.NewLine}{_log.Dump()}{Environment.NewLine}{_telegram.Dump_Sent()}");

        var ownerChannel = File.ReadAllText(_paths.Get_OwnerChannelFile(session.OrchId));
        var goAheads = ownerChannel.Split('\n').Count(line => line.StartsWith("## [", StringComparison.Ordinal) && line.Contains("GO AHEAD — resume", StringComparison.Ordinal));

        Assert.True(goAheads == 1, $"the basic orchestration's owner channel carries {goAheads} GO AHEAD entries:{Environment.NewLine}{ownerChannel}");
    }

    IBridgeEngine Create_Engine()
    {
        return BridgeEngine_Factory.Create_WithDecisionState(
            _paths, _configProvider, _store, _launcher, _log, _telegram,
            _engineState, _clock,
            BridgeTestTiming.Fast(),
            accountReader: ClaudeAccountReader_Factory.Create_FromFile(_claudeGlobalConfig),
            sessionProcesses: _processes);
    }

    void Seed_Pause(DateTime pausedUntilUtc, string? accountId)
    {
        _engineState.Save(new EngineStateSnapshot
        {
            DispatchPausedUntilUtc = pausedUntilUtc,
            DispatchPauseReason = WEEKLY_REASON,
            LimitAccountId = accountId,
        });
    }

    void Log_In(string accountId)
    {
        File.WriteAllText(_claudeGlobalConfig, $"{{\"oauthAccount\":{{\"accountUuid\":\"{accountId}\"}}}}");
    }

    void Write_UsageProbe(string windowKey, double percent, DateTime resetsAtUtc, DateTime writtenUtc)
    {
        var unixSeconds = new DateTimeOffset(DateTime.SpecifyKind(resetsAtUtc, DateTimeKind.Utc), TimeSpan.Zero).ToUnixTimeSeconds();
        var file = Path.Combine(_paths.Root, $"{Guid.NewGuid():N}.usage.json");

        File.WriteAllText(
            file,
            $"{{\"rate_limits\":{{\"{windowKey}\":{{\"used_percentage\":{percent},\"resets_at\":{unixSeconds}}}}}}}");

        File.SetLastWriteTimeUtc(file, writtenUtc);
    }

    (string OrchId, string MemberId) Start_WithAMemberWhoseLastReplyIs(string transcriptLine)
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var memberId = session.Members[0].MemberId;

        var memberFolder = _paths.Get_ImplementerFolder(session.OrchId, memberId);
        Directory.CreateDirectory(memberFolder);

        var transcriptPath = Path.Combine(_tempRoot, "transcripts", $"{memberId}-session.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(transcriptPath)!);
        File.WriteAllText(transcriptPath, transcriptLine + "\n");

        File.WriteAllText(
            Path.Combine(memberFolder, UsageTotals_Reader.SESSION_USAGE_FILE),
            "{\"transcript_path\":\"" + transcriptPath.Replace("\\", "\\\\") + "\"}");

        return (session.OrchId, memberId);
    }

    int Count_Lines_Containing(string fragment)
    {
        return _log.Dump()
            .Split(Environment.NewLine)
            .Count(line => line.Contains(fragment, StringComparison.Ordinal));
    }

    /// <summary>A message in the General topic — no thread id, which is where /resume is offered.</summary>
    static string Build_GeneralOwnerMessageJson(string text)
    {
        return $"{{\"ok\":true,\"result\":[{{\"update_id\":4001,\"message\":{{\"message_id\":91,"
            + $"\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"text\":\"{text}\"}}}}]}}";
    }

    static string Stamp(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

    static string Ordinary_Reply(DateTime utc)
        => "{\"type\":\"assistant\",\"timestamp\":\"" + Stamp(utc)
         + "\",\"uuid\":\"u\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"done\"}]}}";

    static string Limit_Refusal(DateTime utc)
        => "{\"type\":\"assistant\",\"timestamp\":\"" + Stamp(utc)
         + "\",\"uuid\":\"u\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"You've hit your weekly limit\"}]},"
         + "\"error\":\"rate_limit\",\"isApiErrorMessage\":true,\"apiErrorStatus\":429}";

    static async Task<bool> Run_Until_Async(IBridgeEngine engine, Func<bool> condition, int maxMilliseconds)
    {
        using var cancellation = new CancellationTokenSource();

        var loop = engine.Run_Async(cancellation.Token);
        var satisfied = false;

        for (var waited = 0; waited < maxMilliseconds; waited += 100)
        {
            if (condition())
            {
                satisfied = true;
                break;
            }

            await Task.Delay(100);
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
