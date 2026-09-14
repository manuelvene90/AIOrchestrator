using AIOrchestratorCoreLib.Running.PrintTurnRunner;
using AIOrchestratorCoreLib.Running.TurnLiveness;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE BRAKE AT THE PROCESS SEAM, against a real process — the fake CLI told to sleep, which is a
/// hang staged on purpose. The spec's second "done when" is the reason this file exists: "a brake
/// that has never been shown to catch the thing it exists for is the situation this spec is written
/// about". The signals are staged through the readers so each case pins ONE route to its outcome.
/// </summary>
public class PrintTurnRunnerSilenceBrakeTests
{
    static readonly IReadOnlyDictionary<string, string> NO_ENVIRONMENT = new Dictionary<string, string>();
    static readonly TimeSpan SHORT_LIMIT = TimeSpan.FromSeconds(1);
    static readonly TimeSpan FAST_POLL = TimeSpan.FromMilliseconds(200);

    static (IPrintTurnRunner Runner, string WorkDir) Build(int delayMilliseconds)
    {
        var workDir = Path.Combine(Path.GetTempPath(), $"aiorch-turn-brake-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        File.WriteAllText(Path.Combine(workDir, "fake-claude-scenario.json"), "{\"default\":{\"delay_ms\":" + delayMilliseconds + "}}");

        return (PrintTurnRunner_Factory.Create(PrintRunnerTestHarness.Fake_Invocation()), workDir);
    }

    [Fact]
    public async Task AHungTurn_IsKilledByTheBrake_LongBeforeTheDeadline_AndSaysWhy()
    {
        var (runner, workDir) = Build(20000);

        try
        {
            var brake = TurnSilenceBrake_Factory.Create_WithReaders(SHORT_LIMIT, FAST_POLL, () => null, _ => 0);

            var result = await runner.Run_Async(["-p", "--output-format", "json"], "prompt", workDir, NO_ENVIRONMENT, TimeSpan.FromMinutes(5), brake, CancellationToken.None);

            Assert.True(result.TimedOut);
            Assert.False(result.NothingToClose);
            Assert.NotNull(result.BrakeKill);
            Assert.Contains("no sign of life", result.BrakeKill);
            Assert.Contains(TurnSilenceBrake_Factory.CONFIG_KEY, result.BrakeKill);
            Assert.True(result.Elapsed < TimeSpan.FromSeconds(15), $"the hung turn was not killed promptly: {result.Elapsed}");
        }
        finally
        {
            TempTree.Delete_BestEffort(workDir);
        }
    }

    [Fact]
    public async Task ATurnWithACommandRunning_IsNotKilled_AndFinishesNormally()
    {
        var (runner, workDir) = Build(3000);

        try
        {
            var brake = TurnSilenceBrake_Factory.Create_WithReaders(SHORT_LIMIT, FAST_POLL, () => null, _ => 1);

            var result = await runner.Run_Async(["-p", "--output-format", "json"], "prompt", workDir, NO_ENVIRONMENT, TimeSpan.FromMinutes(5), brake, CancellationToken.None);

            Assert.False(result.TimedOut);
            Assert.Null(result.BrakeKill);
            Assert.Equal(0, result.ExitCode);
        }
        finally
        {
            TempTree.Delete_BestEffort(workDir);
        }
    }

    [Fact]
    public async Task ATurnWhoseTranscriptKeepsGrowing_IsNotKilled_AndFinishesNormally()
    {
        var (runner, workDir) = Build(3000);

        try
        {
            var brake = TurnSilenceBrake_Factory.Create_WithReaders(SHORT_LIMIT, FAST_POLL, () => DateTime.UtcNow, _ => 0);

            var result = await runner.Run_Async(["-p", "--output-format", "json"], "prompt", workDir, NO_ENVIRONMENT, TimeSpan.FromMinutes(5), brake, CancellationToken.None);

            Assert.False(result.TimedOut);
            Assert.Null(result.BrakeKill);
        }
        finally
        {
            TempTree.Delete_BestEffort(workDir);
        }
    }

    /// <summary>The deadline is still the backstop: a turn that shows life for ever is still stopped.</summary>
    [Fact]
    public async Task ATurnThatShowsLifeForEver_IsStillStoppedByTheDeadline_AsADeadline()
    {
        var (runner, workDir) = Build(20000);

        try
        {
            var brake = TurnSilenceBrake_Factory.Create_WithReaders(SHORT_LIMIT, FAST_POLL, () => DateTime.UtcNow, _ => 1);

            var result = await runner.Run_Async(["-p", "--output-format", "json"], "prompt", workDir, NO_ENVIRONMENT, TimeSpan.FromSeconds(2), brake, CancellationToken.None);

            Assert.True(result.TimedOut);
            Assert.Null(result.BrakeKill);
        }
        finally
        {
            TempTree.Delete_BestEffort(workDir);
        }
    }

    /// <summary>Shutdown under the brake is still shutdown, not a result — the rule PrintTurnRunnerTests pins without it.</summary>
    [Fact]
    public async Task ACancelledTurnUnderTheBrake_StillThrows()
    {
        var (runner, workDir) = Build(20000);

        try
        {
            var brake = TurnSilenceBrake_Factory.Create_WithReaders(TimeSpan.FromMinutes(5), FAST_POLL, () => null, _ => 0);
            using var cancellation = new CancellationTokenSource();

            var turn = runner.Run_Async(["-p", "--output-format", "json"], "prompt", workDir, NO_ENVIRONMENT, TimeSpan.FromMinutes(5), brake, cancellation.Token);

            await Task.Delay(1500);
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => turn);
        }
        finally
        {
            TempTree.Delete_BestEffort(workDir);
        }
    }
}
