using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.Siblings;

/// <summary>
/// `/progress` IN GENERAL, ON THE REAL ENGINE — the wiring half of Task 11 (plan 2026-09-23, spec §2.4).
/// <c>ProgressReportBuilderTests</c> pins the text; this pins that the engine sends THAT text and no copy of
/// its own. The first case is the golden the builder's literal was derived from: it ran green against the
/// engine BEFORE the General branch moved out, so the move is proven byte-identical for unlinked sessions.
/// </summary>
public class ProgressInGeneralTests : IDisposable
{
    readonly SiblingEngine_Harness _harness = new();

    public void Dispose()
    {
        _harness.Dispose();
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ProgressInGeneral_TwoUnlinkedOrchestrations_IsTodaysText()
    {
        var one = await _harness.Start_Solo_Async("one");
        var two = await _harness.Start_Solo_Async("two");
        File.WriteAllText(_harness.Paths.Get_PlanFile(one), "- [x] a\n- [>] b\n- [ ] c\n");

        // A basic orchestration is born with a seeded PLAN.md; "no ledger" needs it gone.
        File.Delete(_harness.Paths.Get_PlanFile(two));

        var reply = await Send_ProgressInGeneral_Async("two: ");

        // As a SET of lines: the store enumerates folders, alphabetical on NTFS only, and the order is the
        // store's, not this command's. ProgressReportBuilderTests pins the order the builder is handed.
        Assert.Equal(
            ["one: 1/3 done (33%) · 1 running", "two: no task ledger yet"],
            reply.Split('\n').Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ProgressInGeneral_TwoLinkedOrchestrations_IsOneSummedGroup()
    {
        var settings = await _harness.Start_Solo_Async("AI-Orch · settings work");
        var limits = await _harness.Start_Solo_Async("AI-Orch · limits work");
        _harness.Store.Set_SiblingLink(settings, "e-1", settings, $"{settings}#1", _harness.WorktreePath);
        _harness.Store.Set_SiblingLink(limits, "e-1", settings, $"{settings}#1", _harness.WorktreePath);
        File.WriteAllText(_harness.Paths.Get_PlanFile(settings), "- [x] a\n- [ ] b\n");
        File.WriteAllText(_harness.Paths.Get_PlanFile(limits), "- [x] c\n- [>] d\n");

        var reply = await Send_ProgressInGeneral_Async("🔗");

        // The group's member order is the store's (see above), so the header is pinned by its sum and the
        // member lines as a set; the whole text must be exactly what the builder makes of the same store.
        var lines = reply.Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("🔗 AI-Orch · ", lines[0]);
        Assert.EndsWith(" — 2/4 done (50%) · 1 running", lines[0]);
        Assert.Equal(
            ["   · AI-Orch · limits work: 1/2 done (50%) · 1 running", "   · AI-Orch · settings work: 1/2 done (50%)"],
            lines.Skip(1).Order(StringComparer.Ordinal));
        Assert.Equal(
            AIOrchestratorCoreLib.Telegram.ProgressReport_Builder.Build_OpenOrchestrationsText(_harness.Paths, _harness.Store.Load_All()),
            reply);
    }

    /// <summary>
    /// Sends `/progress` in General (no <c>message_thread_id</c>) and returns the General reply that contains
    /// <paramref name="marker"/>. General's dashboard carries the same body under a heading, so the reply is
    /// picked as the General message that STARTS with the body rather than any that merely contains it.
    /// </summary>
    async Task<string> Send_ProgressInGeneral_Async(string marker)
    {
        _harness.Telegram.Queue_Updates(
            "{\"ok\":true,\"result\":[{\"update_id\":9101,\"message\":{\"message_id\":91,"
            + $"\"from\":{{\"id\":{SiblingEngine_Harness.OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SiblingEngine_Harness.SUPERGROUP_CHAT_ID}}},\"text\":\"/progress\"}}}}]}}");

        string? Find_Reply() => _harness.Telegram.Sent_InTopic(null)
            .LastOrDefault(text => text.Contains(marker, StringComparison.Ordinal)
                                   && !text.StartsWith(AIOrchestratorCoreLib.Telegram.GeneralDashboard_Composer.HEADING, StringComparison.Ordinal));

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => Find_Reply() != null, 20_000),
            $"/progress in General got no reply.{Environment.NewLine}{_harness.Telegram.Dump_Sent()}{Environment.NewLine}{_harness.Log.Dump()}");

        return Find_Reply()!;
    }
}
