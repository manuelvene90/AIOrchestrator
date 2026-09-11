using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.TurnSource;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// The stdin prompt of a FRESH turn (2026-09-08). A fresh session used to boot on its role command
/// alone and spend most of its calls reading channel files to find what was pending — measured on
/// the general supervisor, the one fresh role: 7.8 of 9.2 calls per turn. It now gets the entries the
/// bridge already tracked, exactly as a resumed turn does, plus a preamble saying what it is.
/// </summary>
public class PrintTurnPromptBuilderTests
{
    static readonly ITurnSource IMP = TurnSource_Factory.Create_Spoke("imp-1", "/o/imp-1/channel.md");
    static readonly ITurnSource OWNER = TurnSource_Factory.Create_Owner("/o/owner-channel.md");

    static PendingEntry Entry(ITurnSource source, int index, string author, string subject, string body)
    {
        var text = $"## [{index}] FROM {author} — 2026-09-08 10:0{index} — {subject}\n\n{body}\n";
        return new PendingEntry(source, ChannelEntry_Parser.Parse_All(text)[0]);
    }

    [Fact]
    public void FreshSession_ForAMember_CarriesHeaderPreambleNoGreetingEntriesAndContract()
    {
        var pending = new[] { Entry(IMP, 7, "supervisor", "BRIEF — split the barrel", "Do it in two commits.") };

        var prompt = PrintTurnPrompt_Builder.Build_FreshSession("repo-1/imp-1/3", "/aiorch:implementer repo-1 imp-1", pending, [IMP], greetsOnBoot: false);

        Assert.StartsWith("[bridge turn repo-1/imp-1/3]\n", prompt);
        Assert.Contains(PrintTurnPrompt_Builder.FRESH_SESSION_PREAMBLE, prompt);
        Assert.Contains("If your role command `/aiorch:implementer repo-1 imp-1` has not run", prompt);
        Assert.Contains(PrintTurnPrompt_Builder.FRESH_SESSION_NO_GREETING, prompt);
        Assert.Contains("New traffic in your channel — 1 entry:", prompt);
        Assert.Contains("BRIEF — split the barrel", prompt);
        Assert.Contains("Do it in two commits.", prompt);
        Assert.Contains("Your final message IS your channel entry", prompt);
        Assert.DoesNotContain(PrintTurnPrompt_Builder.ALREADY_EXECUTED_PREFIX, prompt);
        Assert.DoesNotContain(PrintTurnPrompt_Builder.BOOT_TURN, prompt);
    }

    [Fact]
    public void FreshSession_ForTheGeneral_KeepsItsGreeting()
    {
        // The general supervisor's greeting per launch is an owner directive (2026-08-25): the phone
        // must say when it is reachable. Only members are told not to greet.
        var pending = new[] { Entry(OWNER, 12, "owner", "status?", "how are the orchestrations") };

        var prompt = PrintTurnPrompt_Builder.Build_FreshSession("general/general/5", "/aiorch:general-supervisor", pending, [OWNER], greetsOnBoot: true);

        Assert.Contains(PrintTurnPrompt_Builder.FRESH_SESSION_PREAMBLE, prompt);
        Assert.DoesNotContain(PrintTurnPrompt_Builder.FRESH_SESSION_NO_GREETING, prompt);
        Assert.Contains("status?", prompt);
    }

    [Fact]
    public void FreshSession_WithSeveralSources_UsesTheMultiSourceContract()
    {
        var pending = new[]
        {
            Entry(OWNER, 3, "owner", "ruling", "cancel"),
            Entry(IMP, 9, "implementer", "report", "green"),
        };

        var prompt = PrintTurnPrompt_Builder.Build_FreshSession("repo-1/sup/4", "/aiorch:supervisor repo-1", pending, [OWNER, IMP], greetsOnBoot: false);

        Assert.Contains("New traffic on 2 channels, 2 entries, oldest first:", prompt);
        Assert.Contains($"{PrintTurnPrompt_Builder.SOURCE_LABEL_PREFIX}owner", prompt);
        Assert.Contains($"{PrintTurnPrompt_Builder.SOURCE_LABEL_PREFIX}imp-1", prompt);
        Assert.Contains("ADDRESS EACH PART", prompt);
    }

    [Fact]
    public void FreshSession_WithNothingPending_IsARefusal()
    {
        // A turn nobody triggered is the boot turn, and the boot turn takes no stdin prompt.
        Assert.Throws<ArgumentException>(() => PrintTurnPrompt_Builder.Build_FreshSession("repo-1/imp-1/1", "/aiorch:implementer repo-1 imp-1", [], [IMP], greetsOnBoot: false));
    }

    [Fact]
    public void FollowUp_IsUnchanged_ByTheFreshVariant()
    {
        var pending = new[] { Entry(IMP, 7, "supervisor", "BRIEF", "body") };

        var prompt = PrintTurnPrompt_Builder.Build_FollowUp("repo-1/imp-1/3", pending, [], [IMP]);

        Assert.StartsWith("[bridge turn repo-1/imp-1/3]\nNew traffic in your channel — 1 entry:", prompt);
        Assert.DoesNotContain(PrintTurnPrompt_Builder.FRESH_SESSION_PREAMBLE, prompt);
    }
}
