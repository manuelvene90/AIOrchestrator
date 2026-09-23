using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.Tests.Bridge;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.TestSupport;

/// <summary>
/// THE FIXTURE IS PROVEN BEFORE ANY SIBLING TEST LEANS ON IT (decision 20: a harness that cannot do
/// what it claims must fail loudly, not certify an absence). Each test pins one thing the later engine
/// tests would otherwise take on trust: that a solo's first entry is mirrored rather than absorbed as
/// history, that two solos land in two topics, that an outbox index comes back right, and that a tap
/// on a close-family prompt reaches the engine.
/// </summary>
public class SiblingEngineHarnessTests
{
    /// <summary>
    /// THE BASELINE WAIT WORKS: an entry appended the moment <c>Start_Solo_Async</c> returns reaches
    /// the phone — and only in that solo's own topic.
    ///
    /// <para>
    /// The entries carry <see cref="OwnerPush_Policy.BLOCKED_MARKER"/> because the fixture runs the
    /// shipped preset, whose <c>phone.push</c> is <c>filtered</c>: a plain status line from a solo is held
    /// for the turn-end digest, and a test using one would be asserting about the filter, not the fixture.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TwoSolos_GetTwoTopics_AndEachEntryLandsInItsOwn()
    {
        using var harness = new SiblingEngine_Harness();

        var settings = await harness.Start_Solo_Async("AI-Orch · settings work");
        var limits = await harness.Start_Solo_Async("AI-Orch · limits rework");

        Assert.NotEqual(harness.TopicOf(settings), harness.TopicOf(limits));

        harness.Append_Solo(settings, "status", $"{OwnerPush_Policy.BLOCKED_MARKER}: settings-entry-marker");
        harness.Append_Solo(limits, "status", $"{OwnerPush_Policy.BLOCKED_MARKER}: limits-entry-marker");

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(
                () => harness.Telegram.Count_Sent_Containing("settings-entry-marker") == 1
                    && harness.Telegram.Count_Sent_Containing("limits-entry-marker") == 1,
                20_000),
            $"an entry appended after Start_Solo_Async was not mirrored.{Environment.NewLine}{harness.Telegram.Dump_Sent()}{Environment.NewLine}{harness.Log.Dump()}");

        Assert.Contains(harness.Telegram.Sent_InTopic(harness.TopicOf(settings)), text => text.Contains("settings-entry-marker", StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Telegram.Sent_InTopic(harness.TopicOf(settings)), text => text.Contains("limits-entry-marker", StringComparison.Ordinal));
        Assert.Contains(harness.Telegram.Sent_InTopic(harness.TopicOf(limits)), text => text.Contains("limits-entry-marker", StringComparison.Ordinal));
    }

    /// <summary>
    /// A TOPIC THE ENGINE CREATES IS DISTINCT ONLY WHEN ASKED FOR: 1 for every topic by default (so no
    /// existing test moves), counting up from <see cref="ScriptedInbound_Fake.FIRST_DISTINCT_TOPIC_ID"/>
    /// after <see cref="ScriptedInbound_Fake.Use_DistinctTopicIds"/> — which is how a sibling's topic,
    /// created by the engine, is told apart from its parent's.
    /// </summary>
    [Fact]
    public async Task TheFake_GivesDistinctTopicIds_OnlyAfterTheOptIn()
    {
        var telegram = new ScriptedInbound_Fake();

        Assert.Equal(1L, await telegram.Create_ForumTopic_Async("a", null, CancellationToken.None));
        Assert.Equal(1L, await telegram.Create_ForumTopic_Async("b", null, CancellationToken.None));

        telegram.Use_DistinctTopicIds();

        Assert.Equal(ScriptedInbound_Fake.FIRST_DISTINCT_TOPIC_ID, await telegram.Create_ForumTopic_Async("c", null, CancellationToken.None));
        Assert.Equal(ScriptedInbound_Fake.FIRST_DISTINCT_TOPIC_ID + 1, await telegram.Create_ForumTopic_Async("d", null, CancellationToken.None));
    }

    /// <summary>The index a request cites as <c>handover</c> is the one the entry actually carries, and the entry is a HANDOVER.</summary>
    [Fact]
    public void AppendOutbox_ReturnsTheIndexTheEntryGot()
    {
        using var harness = new SiblingEngine_Harness();
        var orchId = harness.Launcher.Start_BasicOrchestration(SiblingEngine_Harness.REPO_NAME, harness.RepoPath).OrchId;

        Assert.Equal(1, harness.Append_Outbox(orchId, "a note"));
        var index = harness.Append_Outbox(orchId, $"{HandoverEntry_Detector.HANDOVER_MARKER} limits");

        Assert.Equal(2, index);

        var entry = Assert.Single(ChannelEntry_Parser.Parse_All(File.ReadAllText(harness.Paths.Get_SiblingOutboxFile(orchId))), e => e.Index == index);
        Assert.Equal(ChannelAuthors.Solo, entry.Author);
        Assert.True(HandoverEntry_Detector.Has_HandoverEntry([entry]));
    }

    /// <summary>
    /// A TAP ON A CLOSE-FAMILY PROMPT REACHES THE ENGINE — the prompt a sibling request uses (§4.3), whose
    /// buttons are not single-use <c>opt-</c> tokens and whose message id the fake used to lose. A close
    /// of the solo, declined, is the one such prompt this tree already draws.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TapAsync_OnAClosePrompt_IsHandled()
    {
        using var harness = new SiblingEngine_Harness();
        var orchId = await harness.Start_Solo_Async("AI-Orch · settings work");

        harness.Drop_Request(
            $"{{\"action\":\"close-orchestration\",\"orchId\":\"{orchId}\",\"requester\":\"solo\",\"reason\":\"the harness self-test\"}}");

        await harness.Tap_Async("Keep it open");

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => harness.Archived_Names().Any(name => name.StartsWith("declined-", StringComparison.Ordinal)), 20_000),
            $"the tap was never acted on. Archived: {string.Join(", ", harness.Archived_Names())}{Environment.NewLine}{harness.Log.Dump()}");

        Assert.Null(harness.Store.Get_Session(orchId).ClosedUtc);
    }
}
