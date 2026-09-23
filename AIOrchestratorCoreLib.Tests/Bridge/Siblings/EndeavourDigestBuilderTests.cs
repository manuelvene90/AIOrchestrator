using AIOrchestratorCoreLib.Bridge.Siblings;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Planning.PlanProgress;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.Siblings;

/// <summary>
/// <c>ENDEAVOUR.md</c>, BOUNDED BY CONSTRUCTION (spec 2026-09-23 §5.1). A linked solo reads it at every
/// boundary, so every cap here is a token budget: 15 unfinished lines, 6 owner-channel entries of at most
/// 400 body characters, 3 outbox subjects. And no clock — the file is rewritten only when its text changes.
/// </summary>
public class EndeavourDigestBuilderTests
{
    const string NAME = "AI-Orch · limits rework";
    const string ORCH = "aiorchestrator-2";
    const string WORKING_PATH = "/trees/limits";

    [Fact]
    public void TheHeader_NamesTheSibling_ItsTree_ItsBranch_AndWhetherItIsLive()
    {
        var text = EndeavourDigest_Builder.Build([Input(branch: "sib/limits")]);

        var header = text.Split('\n').Single(line => line.Contains(ORCH, StringComparison.Ordinal));
        Assert.Contains(NAME, header);
        Assert.Contains(WORKING_PATH, header);
        Assert.Contains("branch sib/limits", header);
        Assert.Contains("live", header);

        Assert.Contains("branch ?", EndeavourDigest_Builder.Build([Input(branch: null)]));
        Assert.Contains("paused", EndeavourDigest_Builder.Build([Input(paused: true)]));
    }

    [Fact]
    public void TheCountsLine_IsTheOneFormatter_OrSaysThereIsNoLedger()
    {
        var progress = Ledger("- [x] one\n- [ ] two\n");

        Assert.Contains(PlanProgress_Formatter.Describe_Counts(progress), EndeavourDigest_Builder.Build([Input(progress: progress)]));
        Assert.Contains("no task ledger yet", EndeavourDigest_Builder.Build([Input(progress: null)]));
    }

    [Fact]
    public void MoreThan15UnfinishedLines_AreCapped_WithAPlusKMoreTail()
    {
        var plan = string.Concat(Enumerable.Range(1, 20).Select(n => $"- [ ] task {n:00}\n"));

        var text = EndeavourDigest_Builder.Build([Input(progress: Ledger(plan))]);

        Assert.Contains("[ ] task 15", text);
        Assert.DoesNotContain("task 16", text);
        Assert.Contains("+5 more", text);
        Assert.True(text.IndexOf("task 01", StringComparison.Ordinal) < text.IndexOf("task 15", StringComparison.Ordinal), "file order");
    }

    [Fact]
    public void FinishedLines_AreNotShown()
    {
        var plan = "- [x] shipped it\n- [-] dropped it\n- [>] doing it\n- [ ] owe it\n- [!] stuck on it\n- [?] asking the owner\n";

        var text = EndeavourDigest_Builder.Build([Input(progress: Ledger(plan))]);

        Assert.DoesNotContain("shipped it", text);
        Assert.DoesNotContain("dropped it", text);
        Assert.Contains("[>] doing it", text);
        Assert.Contains("[ ] owe it", text);
        Assert.Contains("[!] stuck on it", text);
        Assert.Contains("[?] asking the owner", text);
    }

    [Fact]
    public void OnlyTheLast6OwnerOrSoloEntries_AreShown_NewestLast()
    {
        var entries = Enumerable.Range(1, 10)
            .Select(n => Entry(n, n % 2 == 0 ? ChannelAuthors.Owner : ChannelAuthors.Solo, $"subject {n:00}", $"body {n:00}"))
            .ToList();

        var text = EndeavourDigest_Builder.Build([Input(ownerTail: entries)]);

        Assert.DoesNotContain("subject 04", text);
        Assert.Contains("[5] FROM solo — subject 05", text);
        Assert.Contains("[10] FROM owner — subject 10", text);
        Assert.Contains("body 10", text);
        Assert.True(text.IndexOf("subject 05", StringComparison.Ordinal) < text.IndexOf("subject 10", StringComparison.Ordinal), "newest last");
    }

    /// <summary>App entries of EITHER audience are the app talking to itself about the sibling — never what the sibling or the owner said.</summary>
    [Fact]
    public void AnAppEntry_IsNeverShown()
    {
        List<IChannelEntry> entries =
        [
            Entry(1, ChannelAuthors.Owner, "via Telegram", "the owner's words"),
            Entry(2, ChannelAuthors.App, AppEntryAudience_Tag.Apply("for the agent", AppEntryAudiences.Agent), "agent-audience body"),
            Entry(3, ChannelAuthors.App, AppEntryAudience_Tag.Apply("for the owner", AppEntryAudiences.Owner), "owner-audience body"),
        ];

        var text = EndeavourDigest_Builder.Build([Input(ownerTail: entries)]);

        Assert.Contains("the owner's words", text);
        Assert.DoesNotContain("for the agent", text);
        Assert.DoesNotContain("for the owner", text);
        Assert.DoesNotContain("audience body", text);
    }

    [Fact]
    public void ABodyOver400Chars_IsCut()
    {
        var body = new string('a', EndeavourDigest_Builder.MAX_ENTRY_BODY_CHARS) + "TAIL-NEVER-SHOWN";

        var text = EndeavourDigest_Builder.Build([Input(ownerTail: [Entry(1, ChannelAuthors.Solo, "long one", body)])]);

        Assert.Contains(new string('a', EndeavourDigest_Builder.MAX_ENTRY_BODY_CHARS), text);
        Assert.DoesNotContain("TAIL-NEVER-SHOWN", text);
        Assert.Contains("…", text);
    }

    [Fact]
    public void OnlyTheLast3OutboxSubjects_AreShown()
    {
        var text = EndeavourDigest_Builder.Build([Input(outbox: ["out 1", "out 2", "out 3", "out 4", "out 5"])]);

        Assert.DoesNotContain("out 2", text);
        Assert.Contains("out 3", text);
        Assert.Contains("out 5", text);
        Assert.Contains("outbox:", text);
    }

    [Fact]
    public void EverySiblingGetsItsOwnBlock()
    {
        var text = EndeavourDigest_Builder.Build(
        [
            Input(),
            new SiblingDigestInput("AI-Orch · docs pass", "aiorchestrator-3", "/trees/docs", "sib/docs", false, null, [], []),
        ]);

        Assert.Contains(ORCH, text);
        Assert.Contains("aiorchestrator-3", text);
        Assert.Contains("AI-Orch · docs pass", text);
    }

    /// <summary>
    /// NO CLOCK IN THE TEXT (the GeneralDashboard_Composer rule): the file is rewritten only when the text
    /// changes, so a timestamp would make every tick a change.
    /// </summary>
    [Fact]
    public async Task TheTextCarriesNoClock()
    {
        var entries = new List<IChannelEntry> { Entry(1, ChannelAuthors.Owner, "via Telegram", "hello") };

        var first = EndeavourDigest_Builder.Build([Input(progress: Ledger("- [ ] one\n"), ownerTail: entries, outbox: ["out 1"])]);
        await Task.Delay(1100);
        var second = EndeavourDigest_Builder.Build([Input(progress: Ledger("- [ ] one\n"), ownerTail: entries, outbox: ["out 1"])]);

        Assert.Equal(first, second);
        Assert.DoesNotContain(DateTime.Now.ToString("yyyy-MM-dd"), first);
    }

    static SiblingDigestInput Input(
        string? branch = "sib/limits",
        bool paused = false,
        IPlanProgress? progress = null,
        IReadOnlyList<IChannelEntry>? ownerTail = null,
        IReadOnlyList<string>? outbox = null)
    {
        return new SiblingDigestInput(NAME, ORCH, WORKING_PATH, branch, paused, progress, ownerTail ?? [], outbox ?? []);
    }

    static IPlanProgress Ledger(string plan)
    {
        return PlanLedger_Parser.Parse_OrNull(plan) ?? throw new InvalidOperationException($"the fixture ledger did not parse:\n{plan}");
    }

    static IChannelEntry Entry(int index, ChannelAuthors author, string subject, string body)
    {
        return ChannelEntry_Factory.Create(index, author, "2026-01-01 12:00", subject, body, $"## [{index}] FROM x — {subject}\n{body}");
    }
}
