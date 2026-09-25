using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Status;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.Siblings;

/// <summary>
/// O4: A QUESTION HELD IN ONE SIBLING'S TOPIC DOES NOT HOLD THE OTHER'S. The owner split the work
/// so that the jobs do not wait on each other (§8.5). A cross-topic hold would make 'limits' wait
/// for the owner's answer about 'settings', which is the single-thread bottleneck again.
///
/// <para>
/// A PIN, NOT A FEATURE: no production code changed for it. <see cref="QuestionHold_Policy"/> is asked
/// with the APPENDING channel's own orchestration flag, so the behaviour already holds; this exists so
/// that a later "one question at a time, across the whole endeavour" fix turns red here instead of
/// quietly re-creating the bottleneck the owner split the work to escape. The mutant that asks about ANY
/// orchestration's flag reddens both facts (Task 15 report).
/// </para>
/// <para>
/// WHY REAL QUESTIONS, NOT STATUS LINES (Task 15 carry, 2026-09-23). Under the shipped push setting
/// (<c>phone.push = filtered</c>) a plain solo status entry is not sent at all, so "B's status line
/// reached the phone" would never be true and "A's was held" would be true for the wrong reason. A
/// question is sent under every push setting, and its own send is what raises the awaiting-answer flag —
/// so the hold under test is the one the engine raised itself, not a file the test planted.
/// </para>
/// <para>
/// ON <see cref="SiblingEngine_Harness"/>, NOT <c>FailableTelegram_Fake</c> (pre-flight ruling C): that
/// fake answers every topic with one id, and O4 is a claim about one topic against another.
/// </para>
/// </summary>
public class QuestionHoldIsPerTopicTests : IDisposable
{
    /// <summary>One nonsense word per question, carried only in its `QUESTION:` line (see <see cref="Question_Body"/>).</summary>
    const string A_FIRST = "SETTINGSALPHA";
    const string A_SECOND = "SETTINGSBRAVO";
    const string B_FIRST = "LIMITSCHARLIE";
    const string B_SECOND = "LIMITSDELTA";

    readonly SiblingEngine_Harness _harness = new();

    public void Dispose()
    {
        _harness.Dispose();
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AQuestionPendingInA_DoesNotHoldBsEntries()
    {
        var (settings, limits) = await Start_TwoLinkedSolos_Async();

        await Ask_AndWaitForTheHold_Async(settings, A_FIRST);

        // The waterfall in A's topic: this one must wait for the owner's answer to A_FIRST.
        _harness.Append_Solo(settings, "decision two", Question_Body(A_SECOND));

        // B has asked nothing yet. Its question must reach B's topic while A's is still with the owner.
        _harness.Append_Solo(limits, "reset window", Question_Body(B_FIRST));

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => Was_Sent_In(limits, B_FIRST), 30_000),
            $"O4 BROKEN: B's question was held because A is awaiting an answer — one sibling's question held another's topic.{Environment.NewLine}{Dump()}");

        // The positive control that the hold was live in THIS run, so B's delivery is not simply a hold
        // that never works: A's second question is still waiting, several ticks later.
        await Task.Delay(BridgeTestTiming.Window_ForTicks(5));

        Assert.True(AwaitingAnswerFlag_Marker.Is_Raised(_harness.Paths, settings), $"A's awaiting-answer flag fell on its own.{Environment.NewLine}{Dump()}");
        Assert.Equal(0, _harness.Telegram.Count_Sent_Containing(A_SECOND));
        Assert.DoesNotContain(_harness.Telegram.Sent_InTopic(_harness.TopicOf(settings)), text => text.Contains(B_FIRST, StringComparison.Ordinal));
    }

    /// <summary>
    /// THE OTHER DIRECTION OF THE SAME RULE: an answer in A's topic releases A's held question and nothing
    /// else. B asked too, so B's second question waits for an answer in B's topic — the owner answering A
    /// is not the owner answering B.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AnAnswerInA_ReleasesOnlyAsHeldQuestion()
    {
        var (settings, limits) = await Start_TwoLinkedSolos_Async();

        await Ask_AndWaitForTheHold_Async(settings, A_FIRST);
        await Ask_AndWaitForTheHold_Async(limits, B_FIRST);

        _harness.Append_Solo(settings, "decision two", Question_Body(A_SECOND));
        _harness.Append_Solo(limits, "reset window two", Question_Body(B_SECOND));

        await Task.Delay(BridgeTestTiming.Window_ForTicks(5));

        Assert.Equal(0, _harness.Telegram.Count_Sent_Containing(A_SECOND));
        Assert.Equal(0, _harness.Telegram.Count_Sent_Containing(B_SECOND));

        Owner_Writes_In(settings, "take the shorter route", updateId: 9401, messageId: 941);

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => Was_Sent_In(settings, A_SECOND), 30_000),
            $"the owner answered in A's topic and A's held question never followed — the hold is a drop.{Environment.NewLine}{Dump()}");

        await Task.Delay(BridgeTestTiming.Window_ForTicks(5));

        Assert.True(AwaitingAnswerFlag_Marker.Is_Raised(_harness.Paths, limits), $"an answer in A's topic cleared B's flag.{Environment.NewLine}{Dump()}");
        Assert.Equal(0, _harness.Telegram.Count_Sent_Containing(B_SECOND));
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

    /// <summary>A real question, texted into the solo's own topic; its send raises the solo's flag.</summary>
    async Task Ask_AndWaitForTheHold_Async(string orchId, string sentinel)
    {
        _harness.Append_Solo(orchId, "decision one", Question_Body(sentinel));

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(
                () => Was_Sent_In(orchId, sentinel) && AwaitingAnswerFlag_Marker.Is_Raised(_harness.Paths, orchId),
                30_000),
            $"'{orchId}' never asked '{sentinel}' in its own topic with the awaiting-answer flag raised, so nothing below would test a hold.{Environment.NewLine}{Dump()}");
    }

    bool Was_Sent_In(string orchId, string sentinel)
    {
        return _harness.Telegram.Sent_InTopic(_harness.TopicOf(orchId)).Any(text => text.Contains(sentinel, StringComparison.Ordinal));
    }

    void Owner_Writes_In(string orchId, string text, long updateId, long messageId)
    {
        _harness.Telegram.Queue_Updates(
            $"{{\"ok\":true,\"result\":[{{\"update_id\":{updateId},\"message\":{{\"message_id\":{messageId},"
            + $"\"message_thread_id\":{_harness.TopicOf(orchId)},\"from\":{{\"id\":{SiblingEngine_Harness.OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SiblingEngine_Harness.SUPERGROUP_CHAT_ID}}},\"text\":\"{text}\"}}}}]}}");
    }

    /// <summary>
    /// The complete question shape <c>QuestionWaterfallProbeTests.Question_Body</c> documents: an incomplete
    /// question is refused and grows no buttons, and the question and its options come LAST so no coaching
    /// entry is generated. The sentinel rides only in the `QUESTION:` line, never in the subject, because
    /// the topic's status line re-sends the last subject.
    /// </summary>
    static string Question_Body(string sentinel)
    {
        return "RECOMMEND: Keep — it is the branch the ledger already names.\nRISK: low\nROW: none\n"
            + $"QUESTION: Which branch carries the {sentinel} change?\nOPTION: Keep\nOPTION: Replace";
    }

    string Dump()
    {
        return $"{_harness.Telegram.Dump_Sent()}{Environment.NewLine}{_harness.Log.Dump()}";
    }
}
