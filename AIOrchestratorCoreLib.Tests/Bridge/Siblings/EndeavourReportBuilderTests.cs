using AIOrchestratorCoreLib.Bridge.Siblings;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Tests.Telegram;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.Siblings;

/// <summary>
/// `/endeavour` (spec 2026-09-23 §2.2, §2.4; owner decision O3): sibling traffic is never pushed, so this is
/// the owner's one way to see it. The block at the top must be ruling B's single spelling of the group —
/// asserted as EQUAL to <see cref="ProgressReport_Builder.Build_EndeavourBlock_OrNull"/>, not re-typed, so a
/// second copy of the group line cannot creep in here.
/// </summary>
public class EndeavourReportBuilderTests : IDisposable
{
    readonly EndeavourTree _tree = new();

    public void Dispose()
    {
        _tree.Dispose();
    }

    [Fact]
    public void InALinkedTopic_ShowsTheGroupAndEachOutboxsLastThreeSubjects()
    {
        var settings = _tree.Add_Solo("a-settings", "AI-Orch · settings work", "e-1");
        _tree.Add_Solo("b-limits", "AI-Orch · limits work", "e-1");
        Write_Plan("a-settings", ProgressReportBuilderTests.Ledger(done: 1, running: 0, open: 1));

        for (var i = 1; i <= 4; i++)
            Append_Outbox("a-settings", $"settings subject {i}");

        var text = EndeavourReport_Builder.Build(_tree.Paths, Sessions(), settings);

        Assert.Equal(
            ProgressReport_Builder.Build_EndeavourBlock_OrNull(_tree.Paths, Sessions(), "e-1")
            + "\n\n"
            + "AI-Orch · settings work — outbox:\n"
            + "   · [2] settings subject 2\n"
            + "   · [3] settings subject 3\n"
            + "   · [4] settings subject 4\n"
            + "AI-Orch · limits work — outbox: nothing written yet",
            text);
    }

    /// <summary>
    /// THE LAST THREE OF THE WHOLE HISTORY (decision 13): once the outbox is compacted, the live file alone
    /// would still end on the newest entries — so the case that matters is the one where the live file holds
    /// FEWER than three and the rest moved to the archive.
    /// </summary>
    [Fact]
    public void AnOutboxWhoseOlderEntriesWereArchived_StillShowsTheLastThree()
    {
        var settings = _tree.Add_Solo("a-settings", "AI-Orch · settings work", "e-1");
        var outbox = _tree.Paths.Get_SiblingOutboxFile("a-settings");

        for (var i = 1; i <= 3; i++)
            Append_Outbox("a-settings", $"settings subject {i}");

        // The archive holds [1] and [2]; the live file keeps [3] only — the split a compaction leaves.
        var whole = File.ReadAllText(outbox);
        var third = whole.IndexOf("## [3]", StringComparison.Ordinal);
        Assert.True(third > 0, $"the fixture did not write a third entry:{Environment.NewLine}{whole}");
        File.WriteAllText(Channel_Compactor.Build_ArchiveFilePath(outbox), whole[..third]);
        File.WriteAllText(outbox, whole[third..]);

        var text = EndeavourReport_Builder.Build(_tree.Paths, Sessions(), settings);

        Assert.Contains("   · [1] settings subject 1\n   · [2] settings subject 2\n   · [3] settings subject 3", text);
    }

    [Fact]
    public void InAnUnlinkedTopic_SaysSo()
    {
        var alone = _tree.Add_Solo("a-alone", "AI-Orch · alone", endeavourId: null);

        Assert.Equal(
            "this topic is not part of an endeavour — /progress shows its ledger",
            EndeavourReport_Builder.Build(_tree.Paths, Sessions(), alone));
    }

    [Fact]
    public void InGeneral_PointsToTheTopics()
    {
        _tree.Add_Solo("a-settings", "AI-Orch · settings work", "e-1");

        Assert.Equal(
            "/endeavour works inside a sibling's topic — /progress here already groups them",
            EndeavourReport_Builder.Build(_tree.Paths, Sessions(), topicSession: null));
    }

    [Fact]
    public void AnotherEndeavour_IsNotShown()
    {
        var settings = _tree.Add_Solo("a-settings", "AI-Orch · settings work", "e-1");
        _tree.Add_Solo("b-limits", "AI-Orch · limits work", "e-1");
        _tree.Add_Solo("c-parser", "SL · parser rework", "e-2");
        _tree.Add_Solo("d-export", "SL · export rework", "e-2");
        Append_Outbox("c-parser", "the other endeavour's subject");

        var text = EndeavourReport_Builder.Build(_tree.Paths, Sessions(), settings);

        Assert.Contains("AI-Orch · settings work", text);
        Assert.Contains("AI-Orch · limits work", text);
        Assert.DoesNotContain("SL ·", text);
        Assert.DoesNotContain("the other endeavour's subject", text);
    }

    /// <summary>A closed sibling stays in the SUM (the block's business) but gets no outbox section: the owner asked what the jobs in flight say.</summary>
    [Fact]
    public void AClosedMember_HasNoOutboxSection()
    {
        var settings = _tree.Add_Solo("a-settings", "AI-Orch · settings work", "e-1");
        _tree.Add_Solo("b-limits", "AI-Orch · limits work", "e-1");
        Append_Outbox("b-limits", "limits last word");
        _tree.Store.Close_Orchestration("b-limits");

        var text = EndeavourReport_Builder.Build(_tree.Paths, Sessions(), settings);

        Assert.DoesNotContain("limits work — outbox", text);
        Assert.DoesNotContain("limits last word", text);
    }

    [Fact]
    public void ForTopic_ResolvesTheTopic_AndTellsAnUnboundTopicFromGeneral()
    {
        _tree.Add_Solo("a-settings", "AI-Orch · settings work", "e-1");
        _tree.Store.Set_TelegramTopicId("a-settings", 7373);

        Assert.StartsWith("🔗 ", EndeavourReport_Builder.Build_ForTopic(_tree.Paths, Sessions(), 7373));
        Assert.Equal("no orchestration is bound to this topic", EndeavourReport_Builder.Build_ForTopic(_tree.Paths, Sessions(), 9999));
        Assert.Equal(EndeavourReport_Builder.IN_GENERAL, EndeavourReport_Builder.Build_ForTopic(_tree.Paths, Sessions(), null));
    }

    IReadOnlyList<IOrchestrationSession> Sessions()
    {
        return [.. _tree.Sessions().OrderBy(session => session.OrchId, StringComparer.Ordinal)];
    }

    void Append_Outbox(string orchId, string subject)
    {
        _tree.Append_Entry(_tree.Paths.Get_SiblingOutboxFile(orchId), ChannelAuthors.Solo, subject, "body");
    }

    void Write_Plan(string orchId, string text)
    {
        File.WriteAllText(_tree.Paths.Get_PlanFile(orchId), text);
    }
}
