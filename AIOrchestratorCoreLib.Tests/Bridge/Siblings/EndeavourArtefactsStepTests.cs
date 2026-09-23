using AIOrchestratorCoreLib.Bridge.Siblings;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.Siblings;

/// <summary>
/// THE TICK'S RECONCILE OF THE DERIVED FILES (spec 2026-09-23 §3.3 compaction, §3.4, §7.4): every session,
/// every tick, so a file left behind by a crash or a close is gone on the first pass — and a failure is a
/// line for the log, never an exception out of the mirror tick.
/// </summary>
public sealed class EndeavourArtefactsStepTests : IDisposable
{
    const string FIRST = "aiorchestrator-1";
    const string SECOND = "aiorchestrator-2";

    readonly EndeavourTree _tree = new();

    public void Dispose()
    {
        _tree.Dispose();
    }

    [Fact]
    public void TwoLinkedOpenSolos_EachGetBothFiles_NamingTheOther()
    {
        _tree.Add_Solo(FIRST, "AI-Orch · settings work", FIRST);
        _tree.Add_Solo(SECOND, "AI-Orch · limits rework", FIRST);

        Assert.Empty(EndeavourArtefacts_Step.Reconcile(_tree.Paths, _tree.Sessions()));

        Assert.StartsWith($"{SECOND}\t", File.ReadAllText(_tree.Paths.Get_SiblingsListFile(FIRST)));
        Assert.StartsWith($"{FIRST}\t", File.ReadAllText(_tree.Paths.Get_SiblingsListFile(SECOND)));

        var firstDigest = File.ReadAllText(_tree.Paths.Get_EndeavourDigestFile(FIRST));
        Assert.Contains("AI-Orch · limits rework", firstDigest);
        Assert.DoesNotContain("AI-Orch · settings work", firstDigest);

        var secondDigest = File.ReadAllText(_tree.Paths.Get_EndeavourDigestFile(SECOND));
        Assert.Contains("AI-Orch · settings work", secondDigest);
        Assert.DoesNotContain("AI-Orch · limits rework", secondDigest);
    }

    [Fact]
    public void AClosedMember_LosesItsFiles_AndDropsFromTheOthersList()
    {
        _tree.Add_Solo(FIRST, "AI-Orch · settings work", FIRST);
        _tree.Add_Solo(SECOND, "AI-Orch · limits rework", FIRST);
        _tree.Add_Solo("aiorchestrator-3", "AI-Orch · docs pass", FIRST);
        EndeavourArtefacts_Step.Reconcile(_tree.Paths, _tree.Sessions());

        _tree.Store.Close_Orchestration(SECOND);
        Assert.Empty(EndeavourArtefacts_Step.Reconcile(_tree.Paths, _tree.Sessions()));

        Assert.False(File.Exists(_tree.Paths.Get_SiblingsListFile(SECOND)));
        Assert.False(File.Exists(_tree.Paths.Get_EndeavourDigestFile(SECOND)));

        var list = File.ReadAllText(_tree.Paths.Get_SiblingsListFile(FIRST));
        Assert.DoesNotContain(SECOND, list);
        Assert.Contains("aiorchestrator-3", list);
        Assert.DoesNotContain("AI-Orch · limits rework", File.ReadAllText(_tree.Paths.Get_EndeavourDigestFile(FIRST)));
    }

    [Fact]
    public void TheLastSiblingClosing_RemovesTheSurvivorsFiles()
    {
        _tree.Add_Solo(FIRST, "AI-Orch · settings work", FIRST);
        _tree.Add_Solo(SECOND, "AI-Orch · limits rework", FIRST);
        EndeavourArtefacts_Step.Reconcile(_tree.Paths, _tree.Sessions());

        _tree.Store.Close_Orchestration(SECOND);
        EndeavourArtefacts_Step.Reconcile(_tree.Paths, _tree.Sessions());

        Assert.False(File.Exists(_tree.Paths.Get_SiblingsListFile(FIRST)));
        Assert.False(File.Exists(_tree.Paths.Get_EndeavourDigestFile(FIRST)));
    }

    /// <summary>§7.4: the first tick after a restart is the same code as every other, so a crash's leftovers go on it.</summary>
    [Fact]
    public void AStaleFileFromACrash_IsRemovedOnTheFirstReconcile()
    {
        _tree.Add_Solo(FIRST, "AI-Orch · settings work", endeavourId: null);
        File.WriteAllText(_tree.Paths.Get_SiblingsListFile(FIRST), "aiorchestrator-9\t/x\tlive\tghost\n");
        File.WriteAllText(_tree.Paths.Get_EndeavourDigestFile(FIRST), "# stale\n");

        Assert.Empty(EndeavourArtefacts_Step.Reconcile(_tree.Paths, _tree.Sessions()));

        Assert.False(File.Exists(_tree.Paths.Get_SiblingsListFile(FIRST)));
        Assert.False(File.Exists(_tree.Paths.Get_EndeavourDigestFile(FIRST)));
    }

    [Fact]
    public void AnUnchangedWorld_DoesNotRewriteTheDigest()
    {
        _tree.Add_Solo(FIRST, "AI-Orch · settings work", FIRST);
        _tree.Add_Solo(SECOND, "AI-Orch · limits rework", FIRST);
        EndeavourArtefacts_Step.Reconcile(_tree.Paths, _tree.Sessions());

        var digest = _tree.Paths.Get_EndeavourDigestFile(FIRST);
        var stamp = DateTime.UtcNow.AddHours(-1);
        File.SetLastWriteTimeUtc(digest, stamp);

        EndeavourArtefacts_Step.Reconcile(_tree.Paths, _tree.Sessions());

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(digest));
    }

    /// <summary>
    /// §3.3: the outbox is not tailed, so the tailer's compaction never sees it — this sweep does, through
    /// the no-guard overload (no tailer cursor exists to re-anchor).
    /// </summary>
    [Fact]
    public void AnOutboxOver90Entries_IsCompacted()
    {
        _tree.Add_Solo(FIRST, "AI-Orch · settings work", FIRST);
        _tree.Add_Solo(SECOND, "AI-Orch · limits rework", FIRST);

        var outbox = _tree.Paths.Get_SiblingOutboxFile(FIRST);
        for (var n = 1; n <= 100; n++)
            _tree.Append_Entry(outbox, ChannelAuthors.Solo, $"note {n}", "a sibling note");

        Assert.Empty(EndeavourArtefacts_Step.Compact_Outboxes(_tree.Paths, _tree.Sessions()));

        Assert.True(File.Exists(Channel_Compactor.Build_ArchiveFilePath(outbox)), "no archive — the outbox was never compacted");
        Assert.True(ChannelEntry_Parser.Parse_All(File.ReadAllText(outbox)).Count <= Channel_Compactor.KEEP_RECENT_ENTRIES);
        Assert.Equal(100, ChannelHistory_Counter.Read_Entries(outbox).Count);
    }

    /// <summary>
    /// THE RECONCILE DOES NOT COMPACT (Task 10b). Compaction takes the channel lock, and the reconcile runs
    /// before the owner's delivery in the tick, where a stale sibling lock would spend the allowance the
    /// owner's message is owed. So the two are separate calls, and the engine places compaction after the
    /// poll (<see cref="EndeavourArtefactsTickScanTests"/> pins where).
    /// </summary>
    [Fact]
    public void TheReconcile_LeavesALongOutboxAlone()
    {
        _tree.Add_Solo(FIRST, "AI-Orch · settings work", FIRST);
        _tree.Add_Solo(SECOND, "AI-Orch · limits rework", FIRST);

        var outbox = _tree.Paths.Get_SiblingOutboxFile(FIRST);
        for (var n = 1; n <= 100; n++)
            _tree.Append_Entry(outbox, ChannelAuthors.Solo, $"note {n}", "a sibling note");

        Assert.Empty(EndeavourArtefacts_Step.Reconcile(_tree.Paths, _tree.Sessions()));

        Assert.False(File.Exists(Channel_Compactor.Build_ArchiveFilePath(outbox)), "the reconcile compacted — it would take the channel lock ahead of the owner's delivery");
        Assert.Equal(100, ChannelEntry_Parser.Parse_All(File.ReadAllText(outbox)).Count);
    }

    /// <summary>
    /// DECISION 13: the owner channel compacts too (the tailer's step does that one), and the last six
    /// owner/solo entries must still come through when every one of them now sits in the archive.
    /// </summary>
    [Fact]
    public void TheDigest_ReadsHistoryAcrossCompaction()
    {
        _tree.Add_Solo(FIRST, "AI-Orch · settings work", FIRST);
        _tree.Add_Solo(SECOND, "AI-Orch · limits rework", FIRST);

        var channel = _tree.Paths.Get_OwnerChannelFile(SECOND);
        for (var n = 1; n <= 6; n++)
            _tree.Append_Entry(channel, ChannelAuthors.Solo, $"solo said {n}", $"solo body {n}");
        for (var n = 1; n <= 90; n++)
            _tree.Append_Entry(channel, ChannelAuthors.App, $"app line {n}", "housekeeping");

        Assert.NotNull(Channel_Compactor.Compact_IfNeeded(channel));
        Assert.DoesNotContain("solo said", File.ReadAllText(channel));

        EndeavourArtefacts_Step.Reconcile(_tree.Paths, _tree.Sessions());

        var digest = File.ReadAllText(_tree.Paths.Get_EndeavourDigestFile(FIRST));
        for (var n = 1; n <= 6; n++)
            Assert.Contains($"solo said {n}", digest);
        Assert.DoesNotContain("app line", digest);
    }

    /// <summary>
    /// A failure is a LINE, never a throw: the reconcile runs on the mirror tick, and an exception there
    /// would stop every other thing the tick does over one derived file.
    /// </summary>
    [RequiresExclusiveOpenEnforcementFact]
    public void ALockedFile_IsReportedAsAFailure_NotThrown()
    {
        _tree.Add_Solo(FIRST, "AI-Orch · settings work", FIRST);
        _tree.Add_Solo(SECOND, "AI-Orch · limits rework", FIRST);
        EndeavourArtefacts_Step.Reconcile(_tree.Paths, _tree.Sessions());

        _tree.Store.Set_DisplayName(SECOND, "AI-Orch · limits renamed");
        var digest = _tree.Paths.Get_EndeavourDigestFile(FIRST);

        IReadOnlyList<string> failures;
        using (new FileStream(digest, FileMode.Open, FileAccess.Read, FileShare.None))
            failures = EndeavourArtefacts_Step.Reconcile(_tree.Paths, _tree.Sessions());

        var failure = Assert.Single(failures);
        Assert.Contains(digest, failure);

        // The other files of the same tick were still reconciled.
        Assert.Contains("AI-Orch · limits renamed", File.ReadAllText(_tree.Paths.Get_SiblingsListFile(FIRST)));
    }
}
