using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// A CHANNEL BORN UNDER DND MUST STILL BE SEEN — rev-6 F2, driven through the real engine because the
/// defect is where a statement sits relative to the mute gate.
///
/// <para>
/// Both sweeps run BELOW the DND gate, and orchestrations can still be CREATED under DND. So a channel
/// born during a mute was first SEEN at unmute, hours later, with everything that had accumulated in it
/// meanwhile absorbed as "history" and unreportable for ever. The silent baseline pass above the gate
/// records what each channel contained the first time the app saw it, and nothing else.
/// </para>
/// <para>
/// WHY A TELEGRAM CLIENT IS INJECTED HERE, when every other engine probe in this project runs
/// file-only: the gate is `if (_telegramMuted &amp;&amp; _telegramClient != null) return;`. With no client it
/// never returns, DND has no effect on the tick at all, and this whole defect is unreachable. The fake
/// is the only way to reach it.
/// </para>
/// <para>
/// WHICH CASE PINS WHAT, and I had these backwards while designing them. "A channel first seen under
/// DND is silent about its history at unmute" is an INVARIANT, not a guard: unfixed, that channel is
/// simply seen for the first time at unmute and its whole content is absorbed — same silence, different
/// reason. The only observable difference between fixed and unfixed is an offence that ARRIVES DURING
/// THE MUTE: unfixed it is absorbed with the history and lost, fixed it is new and reported. That case
/// is the guard; this one is kept because a pass that recorded SIGHT WITHOUT READING would redden it,
/// and that variant is the remedy this fix was nearly built as.
/// </para>
/// </summary>
public class BaselineUnderDndProbeTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;

    /// <summary>Already in the file when the app first sees it: a quoted header, and a non-numeric index.</summary>
    const string HISTORY =
        "## [1] FROM supervisor — 2026-08-13 09:00 — brief\nbody\n\n"
        + "## [2] FROM implementer — 2026-08-13 09:05 — report\nbody\n\n"
        + "## [1] FROM supervisor — 2026-08-13 09:00 — quoted into the body above, already here\n\n"
        + "## [3] FROM supervisor — 2026-08-13 09:10 — accepted\n\n"
        + "## [2b] FROM supervisor — 2026-08-13 09:12 — a non-numeric index, already here\nbody\n";

    /// <summary>Clean: no crossing, no malformed header. What a channel created under DND looks like.</summary>
    const string CLEAN =
        "## [1] FROM supervisor — 2026-08-13 09:00 — brief\nbody\n\n"
        + "## [2] FROM implementer — 2026-08-13 09:05 — report\nbody\n";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;

    public BaselineUnderDndProbeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-dnd-baseline-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        // The inbound loop reads the chat and owner ids and throws without them. The Italian layer is
        // pinned OFF because it defaults ON and would reach the real translator.
        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID},\"telegramItalianLayer\":false}}");

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        var store = OrchestrationSessionStore_Factory.Create(_paths);
        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var log = OrchestrationLog_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, store, new RecordingSpawner_Fake(), log);
        _engine = BridgeEngine_Factory.Create_WithTelegramClient(
            _paths, configProvider, store, _launcher, log, new FailableTelegram_Fake());
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// GUARD — the only case that separates fixed from unfixed. The channel is clean when the app first
    /// sees it under DND; the offences arrive while the mute is on. Unfixed, the first sight happens at
    /// unmute, both are absorbed as history and neither can ever be reported. Fixed, the pass baselined
    /// the clean file, so both are new.
    /// </summary>
    [Fact]
    public async Task OffencesThatARRIVEDuringTheMute_AreReportedAtUnmute()
    {
        _engine.Set_TelegramMuted(true);

        var (orchId, channelFile) = Start_WithAChannel(CLEAN);

        await Tick_Once_Async();

        File.AppendAllText(
            channelFile,
            "\n## [1] FROM supervisor — 2026-08-13 09:00 — quoted while the owner was away\n\n"
            + "## [2b] FROM supervisor — 2026-08-13 09:20 — a non-numeric index, written while muted\nbody\n");

        _engine.Set_TelegramMuted(false);

        await Tick_Once_Async();

        Assert.Contains("quoted while the owner was away", Wait_For_LogLine(orchId, "index runs backwards"));
        Assert.Contains($"{Path.GetFileName(channelFile)}: 1 malformed", Wait_For_LogLine(orchId, "malformed entry header"));
    }

    /// <summary>
    /// INVARIANT — not a guard, and named so nobody counts it as one. It passes with the pass removed,
    /// because an unfixed engine reaches the same silence by absorbing everything at unmute.
    ///
    /// It is here for the OTHER failure: a pass that registered sight without reading the file. That
    /// variant — the remedy this fix was nearly built as — leaves the channel out of first sight with
    /// none of its content memoised, so every historical crossing and malformed header is reported as
    /// new at unmute. This case is what turns that argument into a red test.
    /// </summary>
    [Fact]
    public async Task TheHISTORYAChannelAlreadyHadWhenFirstSeenUnderDND_StaysSilent()
    {
        _engine.Set_TelegramMuted(true);

        var (orchId, channelFile) = Start_WithAChannel(HISTORY);

        await Tick_Once_Async();

        _engine.Set_TelegramMuted(false);

        await Tick_Once_Async();

        // The unmuted tick reached its end, so the silence below is a verdict rather than an absence:
        // Persist_BridgeState is the last statement of the tick body and both sweeps run above it.
        // (The MUTED tick's execution is what the guard above proves; it cannot be proved here,
        // because the state file is written BELOW the gate and a muted tick never reaches it.)
        Assert.True(
            Wait_Until(() => File.Exists(_paths.BridgeStateFile)),
            "no unmuted tick ever completed, so 'nothing was reported' proves nothing");

        Assert.DoesNotContain(Read_LogLines(orchId), line => line.Contains("index runs backwards"));
        Assert.DoesNotContain(Read_LogLines(orchId), line => line.Contains("malformed entry header"));
        Assert.DoesNotContain("INVISIBLE", File.ReadAllText(channelFile));
    }

    (string OrchId, string ChannelFile) Start_WithAChannel(string channelText)
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        var channelFile = _paths.Get_ImplementerChannelFile(session.OrchId, session.Members[0].MemberId);

        File.WriteAllText(channelFile, channelText);

        return (session.OrchId, channelFile);
    }

    string[] Read_LogLines(string orchId)
    {
        var logFile = _paths.Get_OrchestrationLogFile(orchId);

        return File.Exists(logFile) ? File.ReadAllLines(logFile) : [];
    }

    string Wait_For_LogLine(string orchId, string fragment)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var line = Read_LogLines(orchId).FirstOrDefault(l => l.Contains(fragment));

            if (line != null)
                return line;

            Thread.Sleep(50);
        }

        throw new Exception($"no log line containing '{fragment}' was ever written for {orchId}");
    }

    static bool Wait_Until(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (condition())
                return true;

            Thread.Sleep(50);
        }

        return false;
    }

    async Task Tick_Once_Async()
    {
        using var cancellation = new CancellationTokenSource();

        var loop = _engine.Run_Async(cancellation.Token);

        await cancellation.CancelAsync();

        try
        {
            await loop;
        }
        catch (OperationCanceledException)
        {
            // The only way this loop ends.
        }
    }
}
