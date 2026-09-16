using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Watchdog.SessionWatchdog;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>A bridge-driven session has no pid file by design; the watchdog must not read that as death.</summary>
public class WatchdogPrintSessionTests
{
    sealed class RecordingLauncher : IOrchestrationLauncher
    {
        public List<string> Calls { get; } = [];

        public IOrchestrationSession Start_Orchestration(string repoName, string repoPath) => throw new NotSupportedException();
        public IOrchestrationSession Start_BasicOrchestration(string repoName, string repoPath) => throw new NotSupportedException();
        public IOrchestrationSession Add_Implementer(string orchId) => throw new NotSupportedException();
        public IOrchestrationSession Promote_ToFullCrew(string orchId) => throw new NotSupportedException();
        public IOrchestrationSession Demote_ToBasic(string orchId) => throw new NotSupportedException();
        public IOrchestrationSession Add_Member(string orchId, MemberKinds kind) => throw new NotSupportedException();
        public IOrchestrationSession Add_Member(string orchId, MemberKinds kind, string? model) => throw new NotSupportedException();
        public bool Respawn_Supervisor(string orchId) { Calls.Add($"sup:{orchId}"); return true; }
        public void Respawn_Communicator(string orchId) => Calls.Add($"com:{orchId}");
        public bool Respawn_Implementer(string orchId, string memberId) { Calls.Add($"imp:{orchId}/{memberId}"); return true; }
        public bool Spawn_GeneralSupervisor() { Calls.Add("general"); return true; }
    }

    [Fact]
    public void APrintMember_WithNoPidFile_IsNotRespawned_ButATerminalOneIs()
    {
        // BOTH roles configured print, because the registration file alone no longer means
        // print-run: the config is asked too (a stale file must not silence the watchdog for ever).
        using var harness = new PrintRunnerTestHarness("implementer,general");
        var (orchId, printMember) = harness.Register_Member(MemberKinds.Implementer);
        var terminalMember = harness.Store.Add_Member(orchId, MemberKinds.Reviewer).Members[^1].MemberId;
        harness.Register_General();

        var launcher = new RecordingLauncher();
        var watchdog = SessionWatchdog_Factory.Create(harness.Paths, harness.ConfigProvider, harness.Store, launcher, harness.Log);

        // Past the spawn grace, so a missing pid file counts.
        Thread.Sleep(10);
        watchdog.Check_AndRestart_DeadSessions();

        Assert.DoesNotContain($"imp:{orchId}/{printMember}", launcher.Calls);
        Assert.DoesNotContain("general", launcher.Calls);
        Assert.Contains(launcher.Calls, call => call.StartsWith("sup:") || call == $"imp:{orchId}/{terminalMember}");
    }

    [Fact]
    public void AStreamSupervisor_HasARealProcessButStillNoPidFILE_SoItIsExemptToo()
    {
        // The exemption is asked of the RUNNER, not of the word "print": a stream session's process
        // belongs to the bridge and writes no pid file, so a watchdog reading the file would respawn
        // a session that is running perfectly.
        using var harness = new PrintRunnerTestHarness("supervisor:stream");
        var orchId = "repo-1";
        harness.Register_Supervisor(orchId);

        var launcher = new RecordingLauncher();
        var watchdog = SessionWatchdog_Factory.Create(harness.Paths, harness.ConfigProvider, harness.Store, launcher, harness.Log);

        harness.Age_SupervisorSpawn(orchId, TimeSpan.FromMinutes(10));
        watchdog.Check_AndRestart_DeadSessions();

        Assert.DoesNotContain($"sup:{orchId}", launcher.Calls);

        // THE CONTROL, because "no respawn" has two routes to it and only one of them is the rule
        // under test: flip the role back to terminal and the same missing pid file must produce the
        // respawn. Without this the assertion above would pass just as well if the watchdog never
        // looked at supervisors at all.
        //
        // NAMED EXPLICITLY, because "supervisor" alone stopped meaning terminal. Print supports the
        // supervisor since the trigger went multi-channel, so the bare word left the role still
        // bridge-driven and still exempt — a control that could no longer fail for its own reason,
        // which is the same shape as a guard with its check deleted.
        harness.Write_Config("supervisor:terminal");
        watchdog.Check_AndRestart_DeadSessions();

        Assert.Contains($"sup:{orchId}", launcher.Calls);
    }

    [Fact]
    public void ADemotedMember_WhoseRoleIsStillConfiguredPrint_IsRespawned_BecauseItNoLongerDrivesItsTurns()
    {
        // THE WATCHDOG HALF OF THE ONE-WAKE-MODEL INTERLOCK. Since Task 5 a terminal spawn WRITES the
        // state file with DrivesTurns=false instead of deleting it, so the file's existence stopped
        // answering "is the dispatcher running this session's turns". A watchdog still asking Exists
        // would read a demoted member as bridge-driven, skip it for ever, and leave a dead window
        // with nothing to respawn it — the exact failure the file-existence question used to cause
        // for a role flipped back to terminal in config.json.
        //
        // The BridgeEngine half of the same question is pinned by EffortDialOnABridgeDrivenSupervisorTests;
        // this is the watchdog half, which had no end-to-end case.
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, driving) = harness.Register_Member(MemberKinds.Implementer);
        var (_, demoted) = harness.Register_Member(MemberKinds.Implementer, drivesTurns: false);

        var launcher = new RecordingLauncher();
        var watchdog = SessionWatchdog_Factory.Create(harness.Paths, harness.ConfigProvider, harness.Store, launcher, harness.Log);

        watchdog.Check_AndRestart_DeadSessions();

        // ONE VARIABLE SEPARATES THE TWO, and that is the whole design of this case. Both members are
        // in the same orchestration, both have their role configured print, both have a registration
        // file, neither has a pid file or a spawn stamp — so neither the config, the file's presence,
        // the 90 s grace nor the app-start pass can account for a difference. Only DrivesTurns can.
        Assert.Contains($"imp:{orchId}/{demoted}", launcher.Calls);
        Assert.DoesNotContain($"imp:{orchId}/{driving}", launcher.Calls);
    }
}
