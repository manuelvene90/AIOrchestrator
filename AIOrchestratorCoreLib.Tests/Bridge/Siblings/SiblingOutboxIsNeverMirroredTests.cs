using System.Text;
using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Bridge.Siblings;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.Siblings;

/// <summary>
/// O3: SIBLING TRAFFIC IS NEVER PUSHED. The outbox sits in the orchestration folder's root,
/// where ChannelDiscovery does not look (spec §3.3), so no code enforces this. The test is here
/// so that a future "discover every *.md" change turns red instead of ringing the owner's phone
/// with two sessions' coordination.
///
/// <para>
/// ON <see cref="SiblingEngine_Harness"/>, NOT <c>FailableTelegram_Fake</c> (pre-flight ruling C): that fake
/// answers every topic with one id, so "it reached neither sibling's topic" could not be told from "it
/// reached the one topic both share". The print path's half of O3 — a reply to siblings alone is filed
/// <c>[agent]</c> and never texted — is Task 13's (<c>SiblingOnlyReplyIsNotTextedTests</c>); this is the
/// terminal/mirror half: the outbox file itself.
/// </para>
/// <para>
/// TWO WAYS THIS COULD PASS FOR THE WRONG REASON, both closed. (1) The tailer takes a channel it has never
/// seen at its CURRENT length, so a discovered outbox whose ASK was already written when it was first seen
/// would be absorbed as history and never sent — a "discover every *.md" regression would stay green. So the
/// outbox gets a first entry, the engine runs several ticks (a tailer would take it as history then), and
/// only THEN is the ASK appended. (2) "Nothing was sent" after an idle wait proves nothing if the mirror
/// never ran: a sendable entry appended to the SAME solo's owner channel afterwards must reach the phone
/// first, so the tick that would have carried the ASK demonstrably ran.
/// </para>
/// </summary>
public class SiblingOutboxIsNeverMirroredTests : IDisposable
{
    const string ASK_FRAGMENT = "which DTO";
    const string CONTROL_MARKER = "owner-channel-control-marker";

    readonly SiblingEngine_Harness _harness = new();

    public void Dispose()
    {
        _harness.Dispose();
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AnOutboxAppend_NeverReachesTelegram()
    {
        var (settings, limits) = await Start_TwoLinkedSolos_Async();

        _harness.Append_Outbox(settings, "STATUS — starting on the settings rows");
        await Task.Delay(BridgeTestTiming.Window_ForTicks(5));

        _harness.Append_Outbox(settings, $"ASK {ASK_FRAGMENT}? — the settings page or the limits one", $"Body: {ASK_FRAGMENT} do you want for the reset window?");
        _harness.Append_Outbox(limits, $"ANSWER {ASK_FRAGMENT}: the limits one", "Mine. Keep yours.");
        await Task.Delay(BridgeTestTiming.Window_ForTicks(5));

        // The positive control: the mirror ran over this solo's folder after the ASK was written.
        _harness.Append_Solo(settings, "status", $"{OwnerPush_Policy.BLOCKED_MARKER}: {CONTROL_MARKER}");

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => _harness.Telegram.Count_Sent_Containing(CONTROL_MARKER) == 1, 20_000),
            $"the control entry never reached the phone, so the ASK's absence would prove nothing.{Environment.NewLine}{_harness.Telegram.Dump_Sent()}{Environment.NewLine}{_harness.Log.Dump()}");

        await Task.Delay(BridgeTestTiming.Window_ForTicks(2));

        Assert.Equal(0, _harness.Telegram.Count_Sent_Containing(ASK_FRAGMENT));
        Assert.False(_harness.Telegram.Has_Edited_Containing(ASK_FRAGMENT), "an edit carried the siblings' traffic to the phone");
        Assert.DoesNotContain(_harness.Telegram.ButtonEdits, edit => edit.Text.Contains(ASK_FRAGMENT, StringComparison.Ordinal));
        Assert.DoesNotContain(_harness.Telegram.Documents.ToList(), document => Encoding.UTF8.GetString(document.Content).Contains(ASK_FRAGMENT, StringComparison.Ordinal)
                                                                              || (document.Caption ?? string.Empty).Contains(ASK_FRAGMENT, StringComparison.Ordinal));
    }

    /// <summary>
    /// THE OTHER HALF OF O3 — never pushed, but SHOWN WHEN ASKED: `/endeavour` typed in a sibling's topic
    /// answers in that topic with exactly what <see cref="EndeavourReport_Builder"/> makes of the store, the
    /// outbox subjects included. Asserted equal to the builder's own text rather than re-typed: the wording is
    /// <c>EndeavourReportBuilderTests</c>' business, and the engine must add no copy of its own.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task EndeavourTypedInASiblingsTopic_ShowsTheOutboxThere()
    {
        var (settings, limits) = await Start_TwoLinkedSolos_Async();
        _harness.Append_Outbox(limits, $"ASK {ASK_FRAGMENT}? — the limits side");

        var topic = _harness.TopicOf(settings);

        _harness.Telegram.Queue_Updates(
            "{\"ok\":true,\"result\":[{\"update_id\":9201,\"message\":{\"message_id\":92,"
            + $"\"message_thread_id\":{topic},\"from\":{{\"id\":{SiblingEngine_Harness.OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SiblingEngine_Harness.SUPERGROUP_CHAT_ID}}},\"text\":\"/endeavour\"}}}}]}}");

        string? Find_Reply() => _harness.Telegram.Sent_InTopic(topic)
            .LastOrDefault(text => text.Contains(ASK_FRAGMENT, StringComparison.Ordinal));

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => Find_Reply() != null, 20_000),
            $"/endeavour in a sibling's topic did not show the outbox there.{Environment.NewLine}{_harness.Telegram.Dump_Sent()}{Environment.NewLine}{_harness.Log.Dump()}");

        Assert.Equal(EndeavourReport_Builder.Build_ForTopic(_harness.Paths, _harness.Read_Sessions(), topic), Find_Reply());
        Assert.DoesNotContain(_harness.Telegram.Sent_InTopic(_harness.TopicOf(limits)), text => text.Contains(ASK_FRAGMENT, StringComparison.Ordinal));
    }

    async Task<(string Settings, string Limits)> Start_TwoLinkedSolos_Async()
    {
        var settings = await _harness.Start_Solo_Async("AI-Orch · settings work");
        var limits = await _harness.Start_Solo_Async("AI-Orch · limits work");

        _harness.Store.Set_SiblingLink(settings, settings, settings, $"{settings}#1", _harness.WorktreePath);
        _harness.Store.Set_SiblingLink(limits, settings, settings, $"{settings}#1", _harness.WorktreePath);

        Assert.NotEqual(_harness.TopicOf(settings), _harness.TopicOf(limits));

        return (settings, limits);
    }
}
