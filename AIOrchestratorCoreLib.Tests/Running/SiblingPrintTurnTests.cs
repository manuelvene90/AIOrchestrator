using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.TurnSource;
using AIOrchestratorCoreLib.Sessions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// A PRINT-RUN SOLO WITH A SIBLING, END TO END ON THE FAKECLAUDE STUB (plan Task 13). The sibling is not
/// bridge-driven: its outbox entries are appended directly, which is what a terminal solo does through
/// <c>channel-append.sh</c>. These pin the two things the pure tests in <see cref="SiblingTurnSourcesTests"/>
/// cannot see, because they only exist once a turn has run:
/// <list type="bullet">
/// <item>the reply goes to the solo's OWN channel, never into a sibling's outbox — where it would read as
/// that sibling's words and wake the writer on its own reply;</item>
/// <item>a turn taken while paused keeps the sibling cursor, so on unpause the backlog is delivered once
/// and the history absorbed at registration is not handed over again.</item>
/// </list>
/// </summary>
public class SiblingPrintTurnTests
{
    const string ENDEAVOUR = "repo-1";
    const string SOLO_ORCH = "repo-1";
    const string SIBLING_ORCH = "repo-2";
    const string HANDOVER_SUBJECT = "HANDOVER — docs pass";

    /// <summary>
    /// A solo in <see cref="SOLO_ORCH"/>, registered print-run, with one open sibling whose outbox already
    /// holds the HANDOVER — so the registration baseline absorbs it, exactly as a child's first sight of
    /// its parent's outbox does.
    /// </summary>
    static (string OrchId, string MemberId) Linked_Solo(PrintRunnerTestHarness harness)
    {
        harness.Store.Create_Orchestration(SOLO_ORCH, "Repo", harness.RepoPath);
        harness.Store.Set_SiblingLink(SOLO_ORCH, ENDEAVOUR, ENDEAVOUR, $"{ENDEAVOUR}#1", harness.RepoPath);

        var siblingTree = Path.Combine(harness.TempRoot, "sibling-tree");
        Directory.CreateDirectory(siblingTree);
        harness.Store.Create_Orchestration(SIBLING_ORCH, "Repo", harness.RepoPath);
        harness.Store.Set_SiblingLink(SIBLING_ORCH, ENDEAVOUR, SOLO_ORCH, $"{ENDEAVOUR}#1", siblingTree);

        Append_Sibling(harness, HANDOVER_SUBJECT, "The job: the docs pass.");

        return harness.Register_Member(MemberKinds.Solo, SOLO_ORCH);
    }

    static void Append_Sibling(PrintRunnerTestHarness harness, string subject, string body)
    {
        Assert.True(ChannelAppender.Append_SessionEntry(harness.Paths.Get_SiblingOutboxFile(SIBLING_ORCH), ChannelAuthors.Solo, subject, body, DateTime.Now));
    }

    static IReadOnlyList<IChannelEntry> Outbox(PrintRunnerTestHarness harness)
    {
        return ChannelEntry_Parser.Parse_All(File.ReadAllText(harness.Paths.Get_SiblingOutboxFile(SIBLING_ORCH)));
    }

    static int Turns(PrintRunnerTestHarness harness, string orchId, string memberId)
    {
        return harness.Read_State(SessionRoles.Solo, orchId, memberId).ExecutedTurns.Count;
    }

    /// <summary>The prompts that carried pending traffic — a print session's boot turn sends none.</summary>
    static IReadOnlyList<string> TrafficPrompts(PrintRunnerTestHarness harness)
    {
        return
        [
            .. harness.Read_Invocations()
                .Where(line => line["prompt_source"]?.GetValue<string>() == "stdin")
                .Select(line => line["prompt"]?.GetValue<string>() ?? string.Empty)
                .Where(prompt => prompt.StartsWith("[bridge turn ", StringComparison.Ordinal))
        ];
    }

    [Fact]
    public async Task ASiblingsEntry_StartsOneSoloTurn_AndTheReplyStaysInTheSolosOwnChannel()
    {
        using var harness = new PrintRunnerTestHarness("solo");
        var (orchId, memberId) = Linked_Solo(harness);

        // The session TRIES to answer in the sibling's outbox. Nothing it writes may land there.
        var siblingKey = $"{TurnSource_Factory.SIBLING_KEY_PREFIX}{SIBLING_ORCH}";
        harness.Write_Scenario("{\"default\":{\"result\":\"TO: " + siblingKey + "\\nCLAIM — parser\\n\\nI take the parser.\"}}");
        var dispatcher = harness.Create_Dispatcher();

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Turns(harness, orchId, memberId) == 1, PrintRunnerTestHarness.GENEROUS), "the boot turn never ran");

        Append_Sibling(harness, "FYI — tests", "The suite is green on my side.");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Turns(harness, orchId, memberId) == 2, PrintRunnerTestHarness.GENEROUS), "the sibling's entry started no turn");

        // No third turn: the reply is not in any source this solo is woken by, so nothing wakes it again.
        Assert.False(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Turns(harness, orchId, memberId) > 2, TimeSpan.FromSeconds(3)), "the solo woke itself — a one-member loop");
        await dispatcher.Stop_Async();

        Assert.Equal([HANDOVER_SUBJECT, "FYI — tests"], Outbox(harness).Select(entry => entry.Subject));

        var owner = ChannelEntry_Parser.Parse_All(File.ReadAllText(harness.Paths.Get_OwnerChannelFile(orchId)));
        Assert.Contains(owner, entry => entry.Author == ChannelAuthors.Solo && entry.RawText.Contains("I take the parser.", StringComparison.Ordinal));

        var prompt = Assert.Single(TrafficPrompts(harness));
        Assert.Contains("FYI — tests", prompt);
        Assert.DoesNotContain(HANDOVER_SUBJECT, prompt);
    }

    [Fact]
    public async Task ATurnTakenWhilePaused_KeepsTheSiblingCursor_AndTheBacklogArrivesOnceOnUnpause()
    {
        using var harness = new PrintRunnerTestHarness("solo");
        var (orchId, memberId) = Linked_Solo(harness);
        harness.Write_Scenario("""{"default":{"result":"noted\n\nok"}}""");
        var dispatcher = harness.Create_Dispatcher();

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Turns(harness, orchId, memberId) == 1, PrintRunnerTestHarness.GENEROUS), "the boot turn never ran");

        harness.Store.Set_Paused(orchId, true);
        Append_Sibling(harness, "FYI — while you slept", "I merged the parser.");

        // The owner's message is a turn even while paused (writing in the topic is what lifts a pause, and
        // the tick can land between the two) — and that turn resolves no sibling source.
        Assert.True(ChannelAppender.Append_OwnerEntry(harness.Paths.Get_OwnerChannelFile(orchId), "status?", DateTime.Now));
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Turns(harness, orchId, memberId) == 2, PrintRunnerTestHarness.GENEROUS), "the owner's message started no turn");

        Assert.Contains(harness.Read_State(SessionRoles.Solo, orchId, memberId).Cursors, cursor => cursor.SourceKey == $"{TurnSource_Factory.SIBLING_KEY_PREFIX}{SIBLING_ORCH}");

        harness.Store.Set_Paused(orchId, false);

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Turns(harness, orchId, memberId) == 3, PrintRunnerTestHarness.GENEROUS), "the backlog was not delivered on unpause");
        await dispatcher.Stop_Async();

        var prompts = TrafficPrompts(harness);

        Assert.Equal(2, prompts.Count);
        Assert.DoesNotContain("FYI — while you slept", prompts[0]);
        Assert.Contains("FYI — while you slept", prompts[1]);
        Assert.DoesNotContain(HANDOVER_SUBJECT, prompts[1]);
    }
}
