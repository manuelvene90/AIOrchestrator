using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Watchdog.SessionWatchdog;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Watchdog;

/// <summary>
/// THE PAIR THE WATCHDOG'S OWN COMMENT WARNS ABOUT, and it decides whether promotion can exist at
/// all.
///
/// A basic orchestration never had a supervisor, so checking for one would respawn a supervisor into
/// it forever, on top of the solo session that IS the orchestration. That is why the check was there.
/// But it read the MEMBER IDS — basic meant "some member id starts with solo-" — and member ids are
/// kept forever as an audit trail, closed ones included. So promoting an orchestration by closing its
/// solo and spawning a supervisor left it reading as basic FOR EVER: the promoted supervisor would
/// never be respawned when it died, silently, with everything else working.
///
/// Filtering closed members does not fix it either. An empty roster reads as NOT basic, so a basic
/// orchestration whose solo had been closed would flip to full and the watchdog would spawn a
/// supervisor into it — the exact failure the comment exists to prevent, arrived at from the other
/// side. The derivation had no way to express "promoted", which is what forced the change.
///
/// `SupervisorSpawnedUtc` answers the question the watchdog is actually asking — is there a
/// supervisor slot to protect here — rather than answering a question about members and hoping it
/// correlates. It is already persisted and it is stamped BEFORE the spawn is attempted, on purpose.
///
/// THIS HARNESS DRIVES THE REAL WATCHDOG. The launcher, the store and the paths are real; only the
/// process spawner is faked, exactly as OrchestrationLauncherTests does. So these are not assertions
/// about a helper — they are assertions about what the watchdog does.
/// </summary>
public class BasicOrchestrationWatchdogTests : IDisposable
{
    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly RecordingSpawner_Fake _spawner;
    readonly IOrchestrationLauncher _launcher;
    readonly ISessionWatchdog _watchdog;

    public BasicOrchestrationWatchdogTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-watchdog-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _spawner = new RecordingSpawner_Fake();

        var log = OrchestrationLog_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(
            _paths, OrchestratorConfigProvider_Factory.Create(_paths), _store, _spawner, log);

        _watchdog = SessionWatchdog_Factory.Create(_paths, _store, _launcher, log);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// A GENUINELY BASIC ORCHESTRATION IS LEFT ALONE. No supervisor was ever spawned into it, so
    /// there is no supervisor slot to protect — and spawning one would put a second session on top of
    /// the solo that IS the orchestration, every tick, for ever.
    /// </summary>
    [Fact]
    public void AGenuinelyBasicOrchestrationNeverGetsASupervisorSpawnedIntoIt()
    {
        var session = _launcher.Start_BasicOrchestration("repo", _tempRepo);

        _spawner.SpawnedCommands.Clear();
        _watchdog.Check_AndRestart_DeadSessions();

        Assert.DoesNotContain(_spawner.SpawnedCommands, command => Is_SupervisorSpawn(command, session.OrchId));
    }

    /// <summary>
    /// A PROMOTED ONE HAS ITS SUPERVISOR RESPAWNED, and this is the case the old derivation could not
    /// express. The roster still carries the solo — member folders are audit trail and never leave —
    /// so anything reading member ids still calls this basic.
    ///
    /// The stamp is AGED deliberately. `Check_OrchestrationSupervisor` returns early inside the
    /// 90-second spawn grace, so a fresh stamp would make this pass for the wrong reason and keep
    /// passing if the derivation were reverted — item 20, a state with two routes to it.
    /// </summary>
    [Fact]
    public void APromotedOrchestrationHasItsSupervisorRespawned()
    {
        var session = _launcher.Start_BasicOrchestration("repo", _tempRepo);

        Stamp_SupervisorSpawned_LongAgo(session.OrchId);

        _spawner.SpawnedCommands.Clear();
        _watchdog.Check_AndRestart_DeadSessions();

        Assert.Contains(_spawner.SpawnedCommands, command => Is_SupervisorSpawn(command, session.OrchId));
    }

    /// <summary>
    /// And an ordinary FULL orchestration keeps being protected — the case that would break if the
    /// derivation were inverted, and the one live orchestrations depend on today.
    /// </summary>
    [Fact]
    public void AFullOrchestrationStillHasItsSupervisorRespawned()
    {
        var session = _launcher.Start_Orchestration("repo", _tempRepo);

        Stamp_SupervisorSpawned_LongAgo(session.OrchId);

        _spawner.SpawnedCommands.Clear();
        _watchdog.Check_AndRestart_DeadSessions();

        Assert.Contains(_spawner.SpawnedCommands, command => Is_SupervisorSpawn(command, session.OrchId));
    }

    /// <summary>
    /// THE LEGACY SHAPE, and it is real rather than hypothetical: three `session.json` files on this
    /// machine carry no `supervisorSpawnedUtc` at all, written before the field existed. All three
    /// are CLOSED, and the watchdog skips closed orchestrations before it reaches this check, so the
    /// gap is inert there — but an OPEN one would read as basic and lose its supervisor protection
    /// silently.
    ///
    /// Pinned as the known boundary of the derivation rather than left for someone to discover: a
    /// pre-field orchestration that is still open is indistinguishable from a basic one, because the
    /// only evidence either way was never written down.
    /// </summary>
    [Fact]
    public void AnOpenOrchestrationWithNoStampReadsAsBasic_TheKnownLimitOfTheDerivation()
    {
        var session = _launcher.Start_Orchestration("repo", _tempRepo);

        Clear_SupervisorSpawnedStamp(session.OrchId);

        _spawner.SpawnedCommands.Clear();
        _watchdog.Check_AndRestart_DeadSessions();

        Assert.DoesNotContain(_spawner.SpawnedCommands, command => Is_SupervisorSpawn(command, session.OrchId));
    }

    /// <summary>
    /// Asserted on the ROLE COMMAND the session will actually run, which means decoding the script:
    /// it travels as base64 through `-EncodedCommand`, because wt.exe treats `;` in its command line
    /// as a tab separator and would explode a raw PowerShell script into one broken tab per
    /// statement. So `/supervisor <id>` appears nowhere in the plain arguments — my first version of
    /// this searched them and reported that a FULL orchestration's supervisor was not respawned,
    /// which was the test lying rather than the watchdog failing.
    ///
    /// The tab title would have been easier and is what the app uses to find windows, but it is
    /// cosmetic: a session is what its role command makes it, and that is the property under test.
    /// The orchestration id is what separates this from the general supervisor, which the watchdog
    /// checks first on every tick.
    /// </summary>
    static bool Is_SupervisorSpawn(AIOrchestratorCoreLib.Spawning.SpawnCommand.ISpawnCommand command, string orchId)
    {
        return Read_EncodedScript_OrEmpty(command).Contains($"/supervisor {orchId}", StringComparison.Ordinal);
    }

    static string Read_EncodedScript_OrEmpty(AIOrchestratorCoreLib.Spawning.SpawnCommand.ISpawnCommand command)
    {
        var encodedIndex = command.Arguments.ToList().IndexOf("-EncodedCommand");

        if (encodedIndex < 0 || encodedIndex + 1 >= command.Arguments.Count)
            return "";

        return System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(command.Arguments[encodedIndex + 1]));
    }

    void Stamp_SupervisorSpawned_LongAgo(string orchId)
    {
        Edit_SessionJson(orchId, root => root["supervisorSpawnedUtc"] = DateTime.UtcNow.AddHours(-2).ToString("O"));
    }

    void Clear_SupervisorSpawnedStamp(string orchId)
    {
        Edit_SessionJson(orchId, root => root["supervisorSpawnedUtc"] = null);
    }

    /// <summary>
    /// Edits session.json on disk rather than through the store, because the store has no API for
    /// either state and both are things production really produces — an aged stamp is what every
    /// session looks like after a restart, and a missing one is what pre-field files look like.
    /// </summary>
    void Edit_SessionJson(string orchId, Action<JsonObject> edit)
    {
        var file = _paths.Get_SessionFile(orchId);
        var root = JsonNode.Parse(File.ReadAllText(file))!.AsObject();

        edit(root);

        File.WriteAllText(file, root.ToJsonString());
    }
}
