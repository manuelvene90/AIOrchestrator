using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Sessions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE ONE TURN NOBODY TRIGGERED, and why it has to exist.
///
/// <para>
/// A bridge-driven session has no process until a turn runs, and no turn runs until an entry arrives.
/// For a member that is exactly right. For the session that owns an orchestration's OWNER channel it is
/// a deadlock, because the owner cannot write an entry until the orchestration has a Telegram topic, and
/// the topic is created from that session's own boot greeting
/// (<see cref="AIOrchestratorCoreLib.Bridge.OwnerPush_Policy.Is_OnlineGreeting"/>). Nothing moves.
/// </para>
/// <para>
/// It was found live, not here: on 2026-09-06 a full crew was started from the phone at 22:01:08 and had
/// no topic to type into until 22:07:30, and only because the task was appended to owner-channel.md by
/// hand. Under the terminal runner the supervisor was spawned WITH its role command and greeted at once,
/// so this is a regression the bridge-driven runners introduced — and one no test could have caught,
/// because every test in <see cref="StreamTurnDispatcherTests"/> and its neighbours starts by appending
/// the entry whose absence is the whole defect.
/// </para>
/// </summary>
public class BootTurnTests
{
    const string STREAM_START = "stream-start";
    const string STREAM_MESSAGE = "stream-message";

    static IReadOnlyList<JsonObject> Lines(PrintRunnerTestHarness harness, string kind)
    {
        return [.. harness.Read_Invocations().Where(line => line["line_kind"]?.GetValue<string>() == kind)];
    }

    static IReadOnlyList<IChannelEntry> Owner_Entries(PrintRunnerTestHarness harness, string orchId)
    {
        return ChannelEntry_Parser.Parse_All(File.ReadAllText(harness.Paths.Get_OwnerChannelFile(orchId)));
    }

    /// <summary>
    /// The acceptance criterion itself: registered, nothing said to it, and the greeting is in the owner
    /// channel — which is what makes the topic and therefore what makes the orchestration reachable.
    /// </summary>
    [Fact]
    public async Task AStreamSupervisor_BOOTSWITHNOTHINGSAIDTOIT_AndItsGreetingReachesTheOwnerChannel()
    {
        using var harness = new PrintRunnerTestHarness("supervisor:stream");
        var orchId = "repo-1";
        var memberId = harness.Register_Supervisor(orchId);
        harness.Write_Scenario("""{"turns":[{"result":"supervisor online — Repo\n\nReady. Text me what you need."}]}""");
        var dispatcher = harness.Create_Dispatcher();

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Supervisor, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var greeting = Assert.Single(Owner_Entries(harness, orchId), entry => entry.Author == ChannelAuthors.Supervisor);
        Assert.Equal("supervisor online — Repo", greeting.Subject);

        // The subject is what the push policy reads to decide the owner is told a session came up, and
        // that push is what creates the topic. Asserted here rather than assumed: a greeting the policy
        // does not recognise would file an entry and still leave the orchestration unreachable.
        Assert.True(AIOrchestratorCoreLib.Bridge.OwnerPush_Policy.Is_OnlineGreeting(greeting.Subject));

        // ONE message on stdin, and it is the role command. The boot IS the turn when nothing is
        // pending: a second message would describe no traffic and buy a second entry saying nothing.
        var messages = Lines(harness, STREAM_MESSAGE);
        Assert.Equal($"/supervisor {orchId}", Assert.Single(messages)["prompt"]!.GetValue<string>());
        Assert.Single(Lines(harness, STREAM_START));
    }

    /// <summary>
    /// ONCE. The condition is "no turn has ever completed", so the tick after the greeting has to find
    /// nothing to do — otherwise the owner's topic fills with a greeting every two seconds for ever.
    /// </summary>
    [Fact]
    public async Task TheBootTurn_RunsExactlyONCE_AndEveryTickAfterItFindsNothingToDo()
    {
        using var harness = new PrintRunnerTestHarness("supervisor:stream");
        var orchId = "repo-1";
        var memberId = harness.Register_Supervisor(orchId);
        harness.Write_Scenario("""{"default":{"result":"supervisor online — Repo\n\nReady."}}""");
        var dispatcher = harness.Create_Dispatcher();

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Supervisor, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));

        // Driven on, deliberately, against a condition that can never hold: if a second boot turn were
        // dispatched this would see it. Waiting for "it did not happen" is the only shape that proves it.
        Assert.False(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Supervisor, orchId, memberId).ExecutedTurns.Count > 1, TimeSpan.FromSeconds(3)));
        await dispatcher.Stop_Async();

        Assert.Single(Owner_Entries(harness, orchId), entry => entry.Author == ChannelAuthors.Supervisor);
    }

    /// <summary>
    /// A MEMBER GETS NO BOOT TURN, and that is a decision rather than an omission. Its greeting reaches
    /// nobody but its supervisor, so booting every member on registration would buy two model turns each
    /// — one to greet, one for the supervisor the greeting wakes — for something no owner is waiting on.
    /// The rule is "owns the owner channel", not "is bridge-driven".
    /// </summary>
    [Fact]
    public async Task AnImplementer_IsNotBooted_BecauseNobodyIsWaitingOnItsGreeting()
    {
        using var harness = new PrintRunnerTestHarness("implementer:stream");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer, runner: SessionRunners.Stream);
        harness.Write_Scenario("""{"default":{"result":"imp-1 online\n\nready"}}""");
        var dispatcher = harness.Create_Dispatcher();

        Assert.False(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count > 0, TimeSpan.FromSeconds(3)));
        await dispatcher.Stop_Async();

        // No turn, and therefore no process at all — the registration's own promise ("no window, no
        // process until its first inbound entry") still holds for everybody it was written about.
        Assert.Empty(Lines(harness, STREAM_START));
    }

    /// <summary>
    /// THE TASK RIDES THE BOOT TURN. This is the shape a start-orchestration request that carries a task
    /// produces: the app files the owner's words into the new orchestration's channel the instant the
    /// launch returns, and the session's first turn is the role command AND the task, in that order, on
    /// one stdin — which is the same two-message shape every other first turn has.
    /// </summary>
    [Fact]
    public async Task ATaskAlreadyWaiting_RidesTheSameTurn_AsTheSecondMessageOnStdin()
    {
        using var harness = new PrintRunnerTestHarness("supervisor:stream");
        var orchId = "repo-1";
        var memberId = harness.Register_Supervisor(orchId);
        harness.Write_Scenario("""{"turns":[{"result":"supervisor online — Repo\n\nOn it."}]}""");
        var dispatcher = harness.Create_Dispatcher();

        // AFTER the registration, which is what makes it traffic rather than history — the launcher's
        // order, not the test's convenience. See BridgeEngineModel.Process_StartRequests.
        Assert.True(ChannelAppender.Append_OwnerEntry(harness.Paths.Get_OwnerChannelFile(orchId), "Create and commit HELLO.md", DateTime.Now));

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Supervisor, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var messages = Lines(harness, STREAM_MESSAGE);
        Assert.Equal(2, messages.Count);
        Assert.Equal($"/supervisor {orchId}", messages[0]["prompt"]!.GetValue<string>());
        Assert.Contains("Create and commit HELLO.md", messages[1]["prompt"]!.GetValue<string>());

        // ONE process and ONE turn for both messages — the task did not cost an extra boot.
        Assert.Single(Lines(harness, STREAM_START));
    }

    /// <summary>
    /// A BASIC ORCHESTRATION HAS THE SAME DEADLOCK, and its solo is the session that owns the owner
    /// channel. Print rather than stream on purpose: this is the transport the owner actually runs
    /// members on, and it proves the rule is about WHICH CHANNEL the session owns and not about the tube.
    /// </summary>
    [Fact]
    public async Task ASoloOnPrint_IsBootedToo_BecauseItOwnsTheOwnerChannelOfABasicOrchestration()
    {
        using var harness = new PrintRunnerTestHarness("solo");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Solo);
        harness.Write_Scenario("""{"turns":[{"result":"solo-1 online — Repo\n\nReady. Text me what you need."}]}""");
        var dispatcher = harness.Create_Dispatcher();

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Solo, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var greeting = Assert.Single(Owner_Entries(harness, orchId), entry => entry.Author == ChannelAuthors.Solo);
        Assert.Equal("solo-1 online — Repo", greeting.Subject);

        // On print the role command is the POSITIONAL prompt and stdin stays closed on a first turn, so
        // the boot turn needs nothing added to that transport — asserted so a change to the command
        // builder that starts sending a prompt here cannot pass unnoticed.
        var invocation = Assert.Single(harness.Read_Invocations());
        Assert.Equal($"/solo {orchId}", PrintRunnerTestHarness.Args(invocation)[^1]);
    }

    /// <summary>
    /// The record of a turn that answered nothing. Zero is not an index any channel has — they are
    /// numbered from 1 — so the turn reads as "answered no entry" instead of borrowing the first entry of
    /// a turn it never saw, and the <c>turn_ended</c> entry beside it says so in words.
    /// </summary>
    [Fact]
    public async Task TheBootTurnsRecord_SaysItAnsweredNoEntry_RatherThanBorrowingOne()
    {
        using var harness = new PrintRunnerTestHarness("supervisor:stream");
        var orchId = "repo-1";
        var memberId = harness.Register_Supervisor(orchId);
        harness.Write_Scenario("""{"turns":[{"result":"supervisor online — Repo\n\nReady."}]}""");
        var dispatcher = harness.Create_Dispatcher();

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Supervisor, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var executed = Assert.Single(harness.Read_State(SessionRoles.Supervisor, orchId, memberId).ExecutedTurns);
        Assert.Equal(0, executed.FirstEntryIndex);
        Assert.Equal(0, executed.LastEntryIndex);

        var ended = Assert.Single(Owner_Entries(harness, orchId), entry => entry.Author == ChannelAuthors.App);
        Assert.Contains("entries: none (boot turn)", ended.Body);
    }
}
