using AIOrchestratorCoreLib.Status;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

public class AwaitingAnswerFlagMarkerTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public AwaitingAnswerFlagMarkerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-awaiting-flag-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.Get_OrchestrationFolder("arb-fix"));
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// The feature's central case: the owner walks over BECAUSE the session is already frozen. A `/pc`
    /// that only stops the next block leaves this one standing in front of them.
    /// </summary>
    [Fact]
    public void EnteringTerminal_ClearsABlockThatWasALREADYRaised()
    {
        AwaitingAnswerFlag_Marker.Raise(_paths, "arb-fix", out _);

        var cleared = AwaitingAnswerFlag_Marker.Apply_Presence(_paths, "arb-fix", OwnerPresenceModes.Terminal, out var failure);

        Assert.True(cleared);
        Assert.Null(failure);
        Assert.False(AwaitingAnswerFlag_Marker.Is_Raised(_paths, "arb-fix"));
    }

    /// <summary>
    /// The hook reads a FILE, so the name and the location are the contract rather than an
    /// implementation detail this test may paraphrase.
    /// </summary>
    [Fact]
    public void TheFlagIsTheFileTheHookTestsFor()
    {
        AwaitingAnswerFlag_Marker.Raise(_paths, "arb-fix", out _);

        Assert.True(File.Exists(Path.Combine(_paths.Get_OrchestrationFolder("arb-fix"), ".awaiting-answer")));
    }

    /// <summary>
    /// Returning to Remote must not RE-raise a block: the question it stood for was answered in the
    /// terminal, and there is no moment afterwards at which restoring it would be right.
    /// </summary>
    [Fact]
    public void ReturningToRemote_DoesNotRaiseAnything()
    {
        AwaitingAnswerFlag_Marker.Apply_Presence(_paths, "arb-fix", OwnerPresenceModes.Remote, out var failure);

        Assert.Null(failure);
        Assert.False(AwaitingAnswerFlag_Marker.Is_Raised(_paths, "arb-fix"));
    }

    /// <summary>
    /// And Remote leaves an existing block ALONE — a supervisor waiting on a Telegram answer is still
    /// waiting on it when the owner walks away from the terminal.
    /// </summary>
    [Fact]
    public void ReturningToRemote_LeavesAnExistingBlockStanding()
    {
        AwaitingAnswerFlag_Marker.Raise(_paths, "arb-fix", out _);

        var cleared = AwaitingAnswerFlag_Marker.Apply_Presence(_paths, "arb-fix", OwnerPresenceModes.Remote, out _);

        Assert.False(cleared);
        Assert.True(AwaitingAnswerFlag_Marker.Is_Raised(_paths, "arb-fix"));
    }

    /// <summary>
    /// Nothing to clear is not a failure, and it must not read as a transition — a `/pc` in a session
    /// that was never blocked has simply nothing to do.
    /// </summary>
    [Fact]
    public void EnteringTerminal_WithNoBlockRaised_ReportsNoTransitionAndNoFailure()
    {
        var cleared = AwaitingAnswerFlag_Marker.Apply_Presence(_paths, "arb-fix", OwnerPresenceModes.Terminal, out var failure);

        Assert.False(cleared);
        Assert.Null(failure);
    }
}
