using AIOrchestratorCoreLib.Bridge.Siblings;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.Siblings;

/// <summary>
/// <c>.siblings</c>, THE WATCHER'S LIST (spec 2026-09-23 §3.4, §5.3): one line per OPEN sibling, read by a
/// bash loop that splits on tabs — so the shape is the contract, and a tab or newline smuggled in through
/// a display name would split one sibling into two (Review Focus 2).
/// </summary>
public sealed class EndeavourMarkersSyncTests : IDisposable
{
    readonly EndeavourTree _tree = new();

    public void Dispose()
    {
        _tree.Dispose();
    }

    [Fact]
    public void TheLine_HasFourTabSeparatedFields_AndTheOwnOrchestrationIsNeverListed()
    {
        var self = _tree.Add_Solo("aiorchestrator-1", "AI-Orch · settings work", "aiorchestrator-1");
        var other = _tree.Add_Solo("aiorchestrator-2", "AI-Orch · limits rework", "aiorchestrator-1");

        Assert.True(EndeavourMarkers_Sync.Sync(_tree.Paths, self, [self, other]));

        var text = File.ReadAllText(_tree.Paths.Get_SiblingsListFile("aiorchestrator-1"));
        Assert.EndsWith("\n", text);

        var line = Assert.Single(text.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        var fields = line.Split('\t');

        Assert.Equal(
            ["aiorchestrator-2", _tree.Paths.Get_SiblingOutboxFile("aiorchestrator-2"), "live", "AI-Orch · limits rework"],
            fields);
    }

    [Theory]
    [InlineData("AI-Orch ·\tlimits")]
    [InlineData("AI-Orch ·\nlimits")]
    [InlineData("AI-Orch ·\r\nlimits")]
    public void ATabOrNewlineInAName_BecomesASpace(string name)
    {
        var text = EndeavourMarkers_Sync.Build_Text([("aiorchestrator-2", "/x/sibling-outbox.md", false, name)]);

        var line = Assert.Single(text.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        var fields = line.Split('\t');

        Assert.Equal(4, fields.Length);
        Assert.DoesNotContain('\r', fields[3]);
        Assert.StartsWith("AI-Orch · ", fields[3]);
        Assert.EndsWith("limits", fields[3]);
    }

    [Fact]
    public void APausedSibling_IsMarkedPaused()
    {
        var text = EndeavourMarkers_Sync.Build_Text(
        [
            ("aiorchestrator-2", "/x/2/sibling-outbox.md", true, "AI-Orch · limits rework"),
            ("aiorchestrator-3", "/x/3/sibling-outbox.md", false, "AI-Orch · docs pass"),
        ]);

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("paused", lines[0].Split('\t')[2]);
        Assert.Equal("live", lines[1].Split('\t')[2]);
    }

    /// <summary>
    /// DERIVED FILES ARE REWRITTEN ONLY WHEN THEIR TEXT CHANGES: the tick runs every two seconds, and a
    /// rewrite of an unchanged world would be disk churn for nothing.
    /// </summary>
    [Fact]
    public void AnUnchangedWorld_DoesNotRewriteTheFile()
    {
        var self = _tree.Add_Solo("aiorchestrator-1", "AI-Orch · settings work", "aiorchestrator-1");
        var other = _tree.Add_Solo("aiorchestrator-2", "AI-Orch · limits rework", "aiorchestrator-1");
        var file = _tree.Paths.Get_SiblingsListFile("aiorchestrator-1");

        Assert.True(EndeavourMarkers_Sync.Sync(_tree.Paths, self, [other]));

        var stamp = DateTime.UtcNow.AddHours(-1);
        File.SetLastWriteTimeUtc(file, stamp);

        Assert.False(EndeavourMarkers_Sync.Sync(_tree.Paths, self, [other]));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(file));
    }

    /// <summary>"IT MUST NOT OUTLIVE THE MODE" (MeetingFlag_Marker): the last open sibling gone, the file goes too.</summary>
    [Fact]
    public void NoOpenSiblings_DeletesTheFile()
    {
        var self = _tree.Add_Solo("aiorchestrator-1", "AI-Orch · settings work", "aiorchestrator-1");
        var other = _tree.Add_Solo("aiorchestrator-2", "AI-Orch · limits rework", "aiorchestrator-1");
        var file = _tree.Paths.Get_SiblingsListFile("aiorchestrator-1");

        EndeavourMarkers_Sync.Sync(_tree.Paths, self, [other]);
        Assert.True(File.Exists(file));

        Assert.True(EndeavourMarkers_Sync.Sync(_tree.Paths, self, []));
        Assert.False(File.Exists(file));

        Assert.False(EndeavourMarkers_Sync.Sync(_tree.Paths, self, []));
    }

    [Fact]
    public void AClosedOrchestration_LosesItsFile_WhateverItsSiblingsAre()
    {
        var self = _tree.Add_Solo("aiorchestrator-1", "AI-Orch · settings work", "aiorchestrator-1");
        var other = _tree.Add_Solo("aiorchestrator-2", "AI-Orch · limits rework", "aiorchestrator-1");
        var file = _tree.Paths.Get_SiblingsListFile("aiorchestrator-1");

        EndeavourMarkers_Sync.Sync(_tree.Paths, self, [other]);
        _tree.Store.Close_Orchestration("aiorchestrator-1");

        Assert.True(EndeavourMarkers_Sync.Sync(_tree.Paths, _tree.Store.Get_Session("aiorchestrator-1"), [other]));
        Assert.False(File.Exists(file));
    }
}
