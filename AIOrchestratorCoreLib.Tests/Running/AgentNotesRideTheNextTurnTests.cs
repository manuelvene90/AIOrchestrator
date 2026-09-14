using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Sessions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE APP'S NOTES REACH A BRIDGE-DRIVEN SESSION. Until 2026-09-11 they were written into its channel
/// and never shown to it: its turn's prompt carried inbound entries only, so "the owner is still
/// waiting for your reply" was addressed to a session that could not read it (ai-orch-1, measured on
/// itself). A note rides the next turn, once, and never starts one.
/// </summary>
public class AgentNotesRideTheNextTurnTests
{
    [Fact]
    public async Task AnAppNote_RidesTheNextTurnOnce_AndNeverStartsOne()
    {
        using var harness = new PrintRunnerTestHarness("solo");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Solo);
        harness.Write_Scenario("""{"default":{"result":"ack\n\nnoted"}}""");
        var dispatcher = harness.Create_Dispatcher();
        var ownerChannel = harness.Paths.Get_OwnerChannelFile(orchId);

        int Turns() => harness.Read_State(SessionRoles.Solo, orchId, memberId).ExecutedTurns.Count;

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Turns() == 1, PrintRunnerTestHarness.GENEROUS));

        Assert.True(ChannelAppender.Append_AppEntry(ownerChannel, AppEntryAudiences.Agent, "the owner is still waiting for your reply", "Reply now, even one line.", DateTime.Now));

        // A note alone starts nothing.
        Assert.False(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Turns() > 1, TimeSpan.FromSeconds(3)));

        Assert.True(ChannelAppender.Append_OwnerEntry(ownerChannel, "second", DateTime.Now));
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Turns() == 2, PrintRunnerTestHarness.GENEROUS));

        Assert.True(ChannelAppender.Append_OwnerEntry(ownerChannel, "third", DateTime.Now));
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Turns() == 3, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var prompts = harness.Read_Invocations().Select(invocation => invocation["prompt"]?.GetValue<string>() ?? string.Empty).ToList();
        var second = Assert.Single(prompts, prompt => prompt.Contains($"[bridge turn {orchId}/{memberId}/2]", StringComparison.Ordinal));
        var third = Assert.Single(prompts, prompt => prompt.Contains($"[bridge turn {orchId}/{memberId}/3]", StringComparison.Ordinal));

        Assert.Contains("the owner is still waiting for your reply", second, StringComparison.Ordinal);
        Assert.Contains(PrintTurnPrompt_Builder.AGENT_NOTES_LINE.Trim(), second, StringComparison.Ordinal);

        // The dispatcher's own turn records are not notes: the session knows what it did.
        Assert.DoesNotContain(PrintTurn_Words.TURN_ENDED_SUBJECT, second, StringComparison.Ordinal);

        // Once.
        Assert.DoesNotContain("the owner is still waiting for your reply", third, StringComparison.Ordinal);
        Assert.DoesNotContain(PrintTurnPrompt_Builder.AGENT_NOTES_LINE.Trim(), third, StringComparison.Ordinal);
    }
}
