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
        public void Respawn_Supervisor(string orchId) => Calls.Add($"sup:{orchId}");
        public void Respawn_Communicator(string orchId) => Calls.Add($"com:{orchId}");
        public void Respawn_Implementer(string orchId, string memberId) => Calls.Add($"imp:{orchId}/{memberId}");
        public void Spawn_GeneralSupervisor() => Calls.Add("general");
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
}
