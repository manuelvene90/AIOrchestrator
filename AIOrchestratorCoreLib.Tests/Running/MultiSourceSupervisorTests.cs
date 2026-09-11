using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Status;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE SUPERVISOR WOKEN BY EVERYTHING IT LISTENS TO, on the FakeClaude stub. Until this stage a
/// bridge-driven supervisor was woken by <c>owner-channel.md</c> alone: a member finishing and writing
/// in its spoke started no turn, and the role command was told to <c>cat</c> the spokes at the end of
/// every turn to make up for it. These pin the replacement — one cursor per channel, one turn taking
/// whatever is pending across all of them, and the answer filed back into the channel each part is for.
///
/// The members here are NOT bridge-driven: their entries are appended directly, which is exactly what a
/// terminal implementer does through <c>kit/channel-append.sh</c>. That is the shape the supervisor has
/// to work against, headers and all.
/// </summary>
public class MultiSourceSupervisorTests
{
    static void Append_Owner(PrintRunnerTestHarness harness, string orchId, string text)
    {
        Assert.True(ChannelAppender.Append_OwnerEntry(harness.Paths.Get_OwnerChannelFile(orchId), text, DateTime.Now));
    }

    static void Append_Member(PrintRunnerTestHarness harness, string orchId, string memberId, ChannelAuthors author, string subject, string body)
    {
        Assert.True(ChannelAppender.Append_SessionEntry(harness.Paths.Get_ImplementerChannelFile(orchId, memberId), author, subject, body, DateTime.Now));
    }

    static IReadOnlyList<IChannelEntry> Spoke(PrintRunnerTestHarness harness, string orchId, string memberId)
    {
        return ChannelEntry_Parser.Parse_All(File.ReadAllText(harness.Paths.Get_ImplementerChannelFile(orchId, memberId)));
    }

    static IReadOnlyList<IChannelEntry> Owner(PrintRunnerTestHarness harness, string orchId)
    {
        return ChannelEntry_Parser.Parse_All(File.ReadAllText(harness.Paths.Get_OwnerChannelFile(orchId)));
    }

    static string Prompt(JsonObject invocation)
    {
        return invocation["prompt"]?.GetValue<string>() ?? string.Empty;
    }

    /// <summary>
    /// The prompts that carried PENDING TRAFFIC, in order. It is not every invocation, and the reason is
    /// the boot: a print session's first turn puts its role command on the command line and sends no
    /// prompt at all (the role command reads the channels itself), while a stream session has no command
    /// line to put it on and sends the role command as its first message and the traffic as its second.
    /// So "which prompts held the entries" is stdin for print and a stream MESSAGE for stream, and a test
    /// that asked for "the prompt" would be asserting on the boot on one transport and on the traffic on
    /// the other.
    /// </summary>
    static IReadOnlyList<string> TrafficPrompts(PrintRunnerTestHarness harness)
    {
        return
        [
            .. harness.Read_Invocations()
                .Where(line => line["prompt_source"]?.GetValue<string>() == "stdin" || line["line_kind"]?.GetValue<string>() == "stream-message")
                .Select(Prompt)
                .Where(prompt => prompt.StartsWith("[bridge turn ", StringComparison.Ordinal))
        ];
    }

    /// <summary>An orchestration with a bridge-driven supervisor and one member the bridge does NOT drive.</summary>
    static (string OrchId, string MemberId) Crew(PrintRunnerTestHarness harness, string orchId = "repo-1", SessionRunners runner = SessionRunners.Print)
    {
        harness.Register_Supervisor(orchId, runner);

        var session = harness.Store.Add_Member(orchId, MemberKinds.Implementer);

        return (orchId, session.Members[^1].MemberId);
    }

    static bool Supervisor_HasTurned(PrintRunnerTestHarness harness, string orchId, int turns)
    {
        return harness.Read_State(SessionRoles.Supervisor, orchId, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID).ExecutedTurns.Count == turns;
    }

    /// <summary>
    /// THE WHOLE POINT OF THE STAGE: an implementer files its report and the supervisor takes a turn,
    /// with the owner never having said a word. Before this, the entry sat in the spoke until the owner
    /// wrote something or the supervisor happened to read the file at the end of an unrelated turn.
    /// </summary>
    [Fact]
    public async Task AMemberWritingInItsSpoke_StartsASupervisorTurn_WithNoOwnerMessageAtAll()
    {
        using var harness = new PrintRunnerTestHarness("supervisor:stream");
        var (orchId, memberId) = Crew(harness, runner: SessionRunners.Stream);
        harness.Write_Scenario("""{"turns":[{"result":"sup online"},{"result":"Report accepted\n\nTO: imp-1\nVERDICT — accepted\n\nGood, next: the writer."}]}""");
        var dispatcher = harness.Create_Dispatcher();

        Append_Member(harness, orchId, memberId, ChannelAuthors.Implementer, "REPORT — parser", "Parser done, tests green.");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Supervisor_HasTurned(harness, orchId, 1), PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var prompt = Assert.Single(TrafficPrompts(harness));

        // The entry reached the turn, labelled with the channel it came from.
        Assert.Contains($"{PrintTurnPrompt_Builder.SOURCE_LABEL_PREFIX}{memberId}", prompt);
        Assert.Contains("REPORT — parser", prompt);

        // The verdict landed in the SPOKE, under the supervisor's author word.
        var verdict = Assert.Single(Spoke(harness, orchId, memberId), entry => entry.Author == ChannelAuthors.Supervisor);
        Assert.Equal("VERDICT — accepted", verdict.Subject);
        Assert.Equal("Good, next: the writer.", verdict.Body);
    }

    /// <summary>
    /// The state a member sits in between filing a report and being answered, and the eight-minute nudge
    /// loop that used to be the only thing that broke it. <see cref="MemberState_Resolver"/> clears it on
    /// a supervisor entry IN THAT MEMBER'S CHANNEL and on nothing else — so this is the test that the
    /// reply routing is not cosmetic: a verdict filed in the owner channel would leave the member
    /// awaiting review for ever, and the owner would be told about it every eight minutes.
    /// </summary>
    [Fact]
    public async Task AMemberAwaitingReview_LeavesThatStateInOneSupervisorTurn_WithoutANudge()
    {
        using var harness = new PrintRunnerTestHarness("supervisor");
        var (orchId, memberId) = Crew(harness);
        harness.Write_Scenario("""{"default":{"result":"Handled\n\nTO: imp-1\nVERDICT — accepted\n\nMerged. Next task follows."}}""");
        var dispatcher = harness.Create_Dispatcher();

        // The supervisor briefed it and the member filed: the shape Is_AwaitingVerdict is looking for.
        Append_Member(harness, orchId, memberId, ChannelAuthors.Supervisor, "BRIEF — parser", "Write the parser.");
        Append_Member(harness, orchId, memberId, ChannelAuthors.Implementer, "REPORT — parser", "Parser done, tests green.");

        Assert.Equal(MemberStates.AwaitingSupervisorReview, MemberState_Resolver.Resolve(Spoke(harness, orchId, memberId)));
        Assert.True(Nudge_Decider.Owes_MemberAVerdict(Spoke(harness, orchId, memberId)));

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Supervisor_HasTurned(harness, orchId, 1), PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        Assert.NotEqual(MemberStates.AwaitingSupervisorReview, MemberState_Resolver.Resolve(Spoke(harness, orchId, memberId)));
        Assert.False(Nudge_Decider.Owes_MemberAVerdict(Spoke(harness, orchId, memberId)));
    }

    /// <summary>
    /// Two channels, one turn. The coalesce window is a property of the SESSION and not of a file, so an
    /// owner message and a member's report landing together are read together — and the owner's is first,
    /// which is the only place the priority rule bites (both are read either way).
    /// </summary>
    [Fact]
    public async Task TrafficOnTwoChannels_RidesOneTurn_OwnerFirst()
    {
        // FIVE SECONDS, not the sub-second a laptop at rest needs. The window is the whole subject of
        // this test: the two appends must land inside it or each rides its own turn and the assertion
        // below fails for the machine's load rather than for the rule. Measured on 2026-09-06 at load
        // average 42 — several agents building at once — a run of this suite lost two tests to exactly
        // that kind of race. A test whose green depends on how busy the machine is stops being read.
        using var harness = new PrintRunnerTestHarness("supervisor:stream", coalesceSeconds: 5);
        var (orchId, memberId) = Crew(harness, runner: SessionRunners.Stream);
        harness.Write_Scenario("""{"turns":[{"result":"sup online"},{"result":"TO: owner\nStatus\n\nimp-1 is done.\n\nTO: imp-1\nVERDICT\n\nAccepted."}]}""");
        var dispatcher = harness.Create_Dispatcher();

        // The member first, the owner second: the ORDER IN THE PROMPT is the owner's anyway.
        Append_Member(harness, orchId, memberId, ChannelAuthors.Implementer, "REPORT — parser", "Parser done.");
        Append_Owner(harness, orchId, "how is it going?");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Supervisor_HasTurned(harness, orchId, 1), PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var prompt = Assert.Single(TrafficPrompts(harness));
        var ownerLabel = prompt.IndexOf($"{PrintTurnPrompt_Builder.SOURCE_LABEL_PREFIX}owner", StringComparison.Ordinal);
        var memberLabel = prompt.IndexOf($"{PrintTurnPrompt_Builder.SOURCE_LABEL_PREFIX}{memberId}", StringComparison.Ordinal);

        Assert.True(ownerLabel >= 0 && memberLabel >= 0, $"both channels must be labelled in:\n{prompt}");
        Assert.True(ownerLabel < memberLabel, $"the owner must come first in:\n{prompt}");

        // And both channels got their half of the answer. Note the scenario addresses EVERY part: text
        // before the first marker is a block of its own bound for the owner, so a preamble would land as
        // a second owner entry rather than being folded into the one that follows it.
        Assert.Equal("Status", Assert.Single(Owner(harness, orchId), entry => entry.Author == ChannelAuthors.Supervisor).Subject);
        Assert.Equal("VERDICT", Assert.Single(Spoke(harness, orchId, memberId), entry => entry.Author == ChannelAuthors.Supervisor).Subject);
    }

    /// <summary>
    /// Text the session addressed to nobody goes to the owner — the same place a single-source session's
    /// whole message has always gone, so a supervisor that ignores the format is not broken by it.
    /// </summary>
    [Fact]
    public async Task AReplyWithNoAddress_GoesToTheOwnerChannel()
    {
        using var harness = new PrintRunnerTestHarness("supervisor");
        var (orchId, _) = Crew(harness);
        harness.Write_Scenario("""{"default":{"result":"Working on it\n\nI will brief imp-1 next."}}""");
        var dispatcher = harness.Create_Dispatcher();

        Append_Owner(harness, orchId, "start the parser work");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Supervisor_HasTurned(harness, orchId, 1), PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var reply = Assert.Single(Owner(harness, orchId), entry => entry.Author == ChannelAuthors.Supervisor);
        Assert.Equal("Working on it", reply.Subject);
        Assert.Equal("I will brief imp-1 next.", reply.Body);
    }

    /// <summary>
    /// A part addressed to a channel this session is not woken by — a member that has closed, or a
    /// mistyped id — is written to the owner channel with a note, never dropped. The supervisor is
    /// otherwise left waiting for an answer from a session that was never given the message.
    /// </summary>
    [Fact]
    public async Task APartAddressedToAChannelThatIsNotThere_LandsInTheOwnerChannel_WithANote()
    {
        using var harness = new PrintRunnerTestHarness("supervisor");
        var (orchId, _) = Crew(harness);
        harness.Write_Scenario("""{"default":{"result":"TO: imp-9\nBRIEF — writer\n\nWrite the writer."}}""");
        var dispatcher = harness.Create_Dispatcher();

        Append_Owner(harness, orchId, "get the writer started");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Supervisor_HasTurned(harness, orchId, 1), PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var entries = Owner(harness, orchId);

        Assert.Equal("BRIEF — writer", Assert.Single(entries, entry => entry.Author == ChannelAuthors.Supervisor).Subject);

        var note = Assert.Single(entries, entry => entry.Author == ChannelAuthors.App && entry.Subject.Contains(PrintTurn_Words.MISADDRESSED_SUBJECT, StringComparison.Ordinal));
        Assert.Contains("imp-9", note.Subject);
        Assert.Contains("imp-1", note.Body);
    }

    /// <summary>
    /// A MEMBER IS NEVER SPLIT. It has one channel, was never told the format, and a report that happens
    /// to contain a line beginning "TO:" must arrive whole — cutting it in half on somebody else's rule
    /// is the kind of silent mangling nobody would go looking for.
    /// </summary>
    [Fact]
    public async Task ASingleSourceMember_IsNotSplitByALineThatLooksLikeAnAddress()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Write_Scenario("""{"default":{"result":"REPORT — routing\n\nThe table now reads:\nTO: imp-1\nand that is the whole change."}}""");
        var dispatcher = harness.Create_Dispatcher();

        Append_Member(harness, orchId, memberId, ChannelAuthors.Supervisor, "BRIEF", "do it");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var report = Assert.Single(Spoke(harness, orchId, memberId), entry => entry.Author == ChannelAuthors.Implementer);
        Assert.Equal("REPORT — routing", report.Subject);
        Assert.Contains("TO: imp-1", report.Body);
        Assert.Contains("and that is the whole change.", report.Body);
    }

    /// <summary>
    /// The cursors are in the state file, so a bridge that dies and starts again delivers each entry
    /// exactly once — not twice, and not never. The second dispatcher is a genuinely new object reading
    /// the same files, which is the only way to state "after a restart" without a second process.
    /// </summary>
    [Fact]
    public async Task AfterABridgeRestart_NoEntryIsDeliveredTwice_AndNoneIsLost()
    {
        using var harness = new PrintRunnerTestHarness("supervisor");
        var (orchId, memberId) = Crew(harness);
        harness.Write_Scenario("""{"default":{"result":"noted\n\nok"}}""");

        var first = harness.Create_Dispatcher();
        Append_Member(harness, orchId, memberId, ChannelAuthors.Implementer, "REPORT — one", "first");

        Assert.True(PrintRunnerTestHarness.Drive_Until(first, () => Supervisor_HasTurned(harness, orchId, 1), PrintRunnerTestHarness.GENEROUS));
        await first.Stop_Async();

        var afterFirst = harness.Read_State(SessionRoles.Supervisor, orchId, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID);
        Assert.Single(afterFirst.Cursors.Single(cursor => cursor.SourceKey == memberId).Delivered);

        // A new dispatcher over the same state: the entry already delivered must not come back, and the
        // one appended while nothing was running must.
        var second = harness.Create_Dispatcher();
        Append_Member(harness, orchId, memberId, ChannelAuthors.Implementer, "REPORT — two", "second");

        Assert.True(PrintRunnerTestHarness.Drive_Until(second, () => Supervisor_HasTurned(harness, orchId, 2), PrintRunnerTestHarness.GENEROUS));
        await second.Stop_Async();

        // Turn 1 was the boot (role command on the command line, no prompt), so exactly one prompt
        // carried traffic — and it carries the entry written while nothing was running, and ONLY that
        // one. Both halves matter: the first is "nothing was lost", the second is "nothing came twice".
        var prompt = Assert.Single(TrafficPrompts(harness));

        Assert.Contains("REPORT — two", prompt);
        Assert.DoesNotContain("REPORT — one", prompt);

        var afterSecond = harness.Read_State(SessionRoles.Supervisor, orchId, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID);
        Assert.Equal(2, afterSecond.Cursors.Single(cursor => cursor.SourceKey == memberId).Delivered.Count);
    }

    /// <summary>
    /// A member added while the supervisor is already running becomes a source of its next turn, with
    /// nothing re-registered — and its first entry is TRAFFIC, not absorbed history. That first entry is
    /// the member's boot greeting, which lands between the spawn and the next tick; a rule that baselined
    /// a new source would swallow the only entry it can ever see.
    /// </summary>
    [Fact]
    public async Task AMemberAddedMidLife_BecomesASource_AndItsFirstEntryIsTraffic()
    {
        using var harness = new PrintRunnerTestHarness("supervisor");
        const string ORCH = "repo-1";
        harness.Register_Supervisor(ORCH, SessionRunners.Print);
        harness.Write_Scenario("""{"default":{"result":"noted\n\nok"}}""");

        var dispatcher = harness.Create_Dispatcher();

        // A first turn, so the next one is a resume that carries its traffic on stdin.
        Append_Owner(harness, ORCH, "get started");
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Supervisor_HasTurned(harness, ORCH, 1), PrintRunnerTestHarness.GENEROUS));

        Assert.Equal("owner", Assert.Single(harness.Read_State(SessionRoles.Supervisor, ORCH, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID).Cursors).SourceKey);

        var session = harness.Store.Add_Member(ORCH, MemberKinds.Implementer);
        var memberId = session.Members[^1].MemberId;

        Append_Member(harness, ORCH, memberId, ChannelAuthors.Implementer, $"{memberId} online", "reporting for duty");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Supervisor_HasTurned(harness, ORCH, 2), PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var prompt = Assert.Single(TrafficPrompts(harness));

        Assert.Contains($"{memberId} online", prompt);
        Assert.Equal(["owner", memberId], harness.Read_State(SessionRoles.Supervisor, ORCH, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID).Cursors.Select(cursor => cursor.SourceKey));
    }

    /// <summary>
    /// The stream transport reads the same channels through the same dispatcher — the trigger is not a
    /// property of how a turn reaches the model. Print is exercised by every other case here; this is the
    /// one that says the pair cannot drift.
    /// </summary>
    [Fact]
    public async Task AStreamSupervisor_IsWokenByASpokeToo()
    {
        using var harness = new PrintRunnerTestHarness("supervisor:stream");
        const string ORCH = "repo-1";
        harness.Register_Supervisor(ORCH, SessionRunners.Stream);
        var session = harness.Store.Add_Member(ORCH, MemberKinds.Implementer);
        var memberId = session.Members[^1].MemberId;

        harness.Write_Scenario("""{"turns":[{"result":"sup online"},{"result":"Handled\n\nTO: imp-1\nVERDICT\n\nAccepted."}]}""");
        var dispatcher = harness.Create_Dispatcher();

        Append_Member(harness, ORCH, memberId, ChannelAuthors.Implementer, "REPORT — parser", "done");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Supervisor_HasTurned(harness, ORCH, 1), PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        Assert.Equal("VERDICT", Assert.Single(Spoke(harness, ORCH, memberId), entry => entry.Author == ChannelAuthors.Supervisor).Subject);
    }
}
