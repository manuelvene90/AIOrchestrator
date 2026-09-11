using AIOrchestratorCoreLib.Composition;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Composition;

public class SingleInstanceGuardTests : IDisposable
{
    readonly string _root;
    readonly ISupervisionPaths _paths;

    public SingleInstanceGuardTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"aiorch-instance-{Guid.NewGuid():N}");
        _paths = SupervisionPaths_Factory.Create(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void TheFirstHost_GetsTheLock_AndTheSecondIsRefused_UntilTheFirstLetsGo()
    {
        using var first = SingleInstance_Guard.Try_Acquire(_paths);
        Assert.NotNull(first);
        Assert.True(File.Exists(_paths.InstanceLockFile));

        Assert.Null(SingleInstance_Guard.Try_Acquire(_paths));

        first.Dispose();

        using var again = SingleInstance_Guard.Try_Acquire(_paths);
        Assert.NotNull(again);
    }

    [Fact]
    public void AMissingRoot_IsCreated_RatherThanFailingTheLock()
    {
        Assert.False(Directory.Exists(_root));

        using var held = SingleInstance_Guard.Try_Acquire(_paths);

        Assert.NotNull(held);
        Assert.True(Directory.Exists(_root));
    }

    [Fact]
    public void TheLockFile_LivesUnderTheRoot_WhereNoOtherComponentComposesPathsByHand()
    {
        Assert.Equal(Path.Combine(_root, ".instance.lock"), _paths.InstanceLockFile);
    }

    /// <summary>
    /// The two failures are DIFFERENT ANSWERS and the host prints them to the owner. Folding them
    /// together reported an unusable root as "another host is already running" — which sends
    /// someone hunting for a process that does not exist.
    /// </summary>
    [Fact]
    public void ARootThatCannotBeCreated_IsReportedAsThat_NotAsContention()
    {
        var underAFile = Path.Combine(_root, "a-file", "supervision");
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "a-file"), "not a directory");

        var held = SingleInstance_Guard.Try_Acquire(SupervisionPaths_Factory.Create(underAFile), out var reason);

        Assert.Null(held);
        Assert.NotNull(reason);
        Assert.Contains("could not be created", reason);
        Assert.DoesNotContain("already running", reason);
    }

    [Fact]
    public void ContentionSaysSo_AndTheReasonNamesTheRoot()
    {
        using var first = SingleInstance_Guard.Try_Acquire(_paths, out var firstReason);
        Assert.NotNull(first);
        Assert.Null(firstReason);

        Assert.Null(SingleInstance_Guard.Try_Acquire(_paths, out var reason));
        Assert.Contains("already running", reason);
        Assert.Contains(_root, reason);
        Assert.Contains("single poller", reason);
    }
}
