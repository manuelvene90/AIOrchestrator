using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.Siblings;

/// <summary>
/// A SIBLING-ONLY TURN'S REPLY IS WRITTEN, NOT TEXTED (ruling S4, Task 13 review I1; owner decision O3:
/// "sibling-to-sibling traffic is never texted", spec §2.2).
///
/// <para>
/// A bridge-driven solo files its final message in its own owner channel — the one channel it may write,
/// and the thing that stops two print solos acknowledging each other for ever. That channel is mirrored. So
/// when only siblings woke the turn, the dispatcher files the reply with the <c>[agent]</c> tag, the same
/// "for the agent, never texted" mark the app's own agent-facing entries carry, and the mirror honours it
/// for a solo's entry too. The dispatcher half is pinned in <c>SiblingPrintTurnTests</c>; this is the phone
/// half, on the real engine: the tagged entry reaches no Telegram topic, AND it does not spend the owner's
/// answer credit.
/// </para>
/// <para>
/// WHY THE SECOND ASSERTION IS NOT OPTIONAL. Under the fixture's shipped <c>filtered</c> push, a plain solo
/// status line is held anyway — so "nothing was sent" alone would be two routes to one state (decision 20).
/// The owner's credit is raised first, which makes an untagged reply a SendNow; and the ordinary answer
/// written afterwards reaches the phone only through that credit, which proves the tagged entry left it open.
/// </para>
/// </summary>
public class SiblingOnlyReplyIsNotTextedTests : IDisposable
{
    const string ACK_BODY = "I will not touch Foo.csproj while you have it.";
    const string ANSWER_BODY = "Yes. The parser is merged and the branch is clean.";

    readonly SiblingEngine_Harness _harness = new();

    public void Dispose()
    {
        _harness.Dispose();
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AnAgentTaggedSoloReply_ReachesNoTopic_AndLeavesTheOwnersAnswerCreditOpen()
    {
        var orchId = await _harness.Start_Solo_Async("AI-Orch · parser work");
        var topic = _harness.TopicOf(orchId);

        // 1 — the owner asks: this raises the answer credit when the message lands in the channel.
        _harness.Telegram.Queue_Updates(
            "{\"ok\":true,\"result\":[{\"update_id\":8101,\"message\":{\"message_id\":81,"
            + $"\"message_thread_id\":{topic},\"from\":{{\"id\":{SiblingEngine_Harness.OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SiblingEngine_Harness.SUPERGROUP_CHAT_ID}}},\"text\":\"is the parser merged\"}}}}]}}");

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => _harness.Log.Has_Info_Containing("Owner message delivered"), BridgeTestTiming.Window_ForAggregation(30)),
            $"the owner's message was never delivered, so no credit was raised.{Environment.NewLine}{_harness.Log.Dump()}");

        // 2 — a turn only a sibling woke files its acknowledgement, tagged, exactly as the dispatcher writes it.
        _harness.Append_Solo(orchId, AppEntryAudience_Tag.Apply("ACK — Foo.csproj", AppEntryAudiences.Agent), ACK_BODY);

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => _harness.Log.Has_Info_Containing("FROM Solo: [agent] ACK"), 20_000),
            $"the acknowledgement was never tailed, so its absence from the phone would prove nothing.{Environment.NewLine}{_harness.Log.Dump()}");

        await Task.Delay(BridgeTestTiming.Window_ForTicks(10));

        Assert.Equal(0, _harness.Telegram.Count_Sent_Containing(ACK_BODY));

        // AND THE REPLY TRACKER did not read it as the owner being answered — observed RED on 2026-09-24
        // before the fix: "Turn ended after the owner was answered" in the tick after the tagged entry.
        Assert.False(
            _harness.Log.Has_Info_Containing("Turn ended after the owner was answered"),
            $"the tagged acknowledgement cleared the owner's pending reply.{Environment.NewLine}{_harness.Log.Dump()}");

        // 3 — the real answer, which under `filtered` reaches the phone only on the credit.
        _harness.Append_Solo(orchId, "the answer", ANSWER_BODY);

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => _harness.Telegram.Count_Sent_Containing(ANSWER_BODY) == 1, 20_000),
            "the owner's answer was not pushed — the tagged acknowledgement spent the credit it must leave open."
            + $"{Environment.NewLine}{_harness.Telegram.Dump_Sent()}{Environment.NewLine}{_harness.Log.Dump()}");

        Assert.Equal(0, _harness.Telegram.Count_Sent_Containing(ACK_BODY));
    }
}
