using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Sessions;

public class OrchIdAllocatorTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public OrchIdAllocatorTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-allocator-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void Allocate_FreshRepo_StartsAtOne()
    {
        Assert.Equal("arb-studio-1", OrchId_Allocator.Allocate_NextOrchId(_paths, "Arb Studio"));
    }

    [Fact]
    public void Allocate_ExistingFolders_Increments()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "arb-studio-1"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "arb-studio-3"));

        Assert.Equal("arb-studio-4", OrchId_Allocator.Allocate_NextOrchId(_paths, "Arb Studio"));
    }

    [Fact]
    public void Allocate_OtherReposFolders_DoNotInterfere()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "skeleton-7"));

        Assert.Equal("arb-studio-1", OrchId_Allocator.Allocate_NextOrchId(_paths, "Arb Studio"));
    }

    [Fact]
    public void Allocate_SlugCollapsesSpecialCharacters()
    {
        var orchId = OrchId_Allocator.Allocate_NextOrchId(_paths, "Da-Vinci  Fintech__Suite!");

        Assert.Equal("da-vinci-fintech-suite-1", orchId);
    }
}
