using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Status;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

public class MemberStateResolverTests
{
    static IChannelEntry Build_Entry(int index, ChannelAuthors author, string subject, string body)
    {
        return ChannelEntry_Factory.Create(index, author, "2026-08-06", subject, body, $"## [{index}] FROM x — d — {subject}\n{body}");
    }

    [Fact]
    public void Resolve_NoEntries_NewNoTraffic()
    {
        Assert.Equal(MemberStates.NewNoTraffic, MemberState_Resolver.Resolve([]));
    }

    [Fact]
    public void Resolve_LastEntryFromSupervisor_ImplementerWorking()
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "orders", "do X"),
        };

        Assert.Equal(MemberStates.ImplementerWorking, MemberState_Resolver.Resolve(entries));
    }

    [Fact]
    public void Resolve_LastEntryFromImplementer_AwaitingSupervisorReview()
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "orders", "do X"),
            Build_Entry(2, ChannelAuthors.Implementer, "report", "done X"),
        };

        Assert.Equal(MemberStates.AwaitingSupervisorReview, MemberState_Resolver.Resolve(entries));
    }

    [Fact]
    public void Resolve_ImplementerReportsBlockedOnOwner_BlockedOnOwner()
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "orders", "do X"),
            Build_Entry(2, ChannelAuthors.Implementer, "report", "🚩 BLOCKED ON OWNER — need a product decision"),
        };

        Assert.Equal(MemberStates.BlockedOnOwner, MemberState_Resolver.Resolve(entries));
    }

    [Fact]
    public void Resolve_WritingWindowOpenWithoutClose_WritingWindowOpen()
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "orders", "do X"),
            Build_Entry(2, ChannelAuthors.Implementer, "WRITING WINDOW OPEN", "starting the batch"),
        };

        Assert.Equal(MemberStates.WritingWindowOpen, MemberState_Resolver.Resolve(entries));
    }

    [Fact]
    public void Resolve_WritingWindowOpenedThenClosed_FallsBackToLastEntryRule()
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Implementer, "WRITING WINDOW OPEN", "starting"),
            Build_Entry(2, ChannelAuthors.Implementer, "WRITING WINDOW CLOSED", "five fixes landed"),
        };

        Assert.Equal(MemberStates.AwaitingSupervisorReview, MemberState_Resolver.Resolve(entries));
    }

    [Fact]
    public void Resolve_MutationWindowOpen_ReportsWindowOpen()
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Implementer, "MUTATION WINDOW OPEN", "running the seven mutations"),
        };

        Assert.Equal(MemberStates.WritingWindowOpen, MemberState_Resolver.Resolve(entries));
    }

    [Fact]
    public void Resolve_WindowReopenedAfterClose_WindowWinsAgain()
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Implementer, "WRITING WINDOW OPEN", "batch 1"),
            Build_Entry(2, ChannelAuthors.Implementer, "WRITING WINDOW CLOSED", "batch 1 done"),
            Build_Entry(3, ChannelAuthors.Implementer, "WRITING WINDOW OPEN", "batch 2"),
        };

        Assert.Equal(MemberStates.WritingWindowOpen, MemberState_Resolver.Resolve(entries));
    }

    // ── A MARKER DECLARES ONLY WHEN IT BEGINS A LINE ────────────────────────────────────────────
    //
    // It used to be a bare substring anywhere in the entry, so DISCUSSING the vocabulary set the
    // state — and these are the sessions instructed to discuss the vocabulary. Every case below is a
    // real sentence somebody wrote on a channel tonight, not an invented adversarial input.

    /// <summary>
    /// The one that actually happened: a supervisor's brief WARNING a reviewer about this bug
    /// contained the phrase, and pinned that reviewer in WritingWindowOpen for four hours — through
    /// every nudge, and past the report that should have closed the window.
    /// </summary>
    [Fact]
    public void AnEntryDiscussingTheWindowMarkerDoesNotOpenAWindow()
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "a bug you should know about",
                "so an entry that merely QUOTES \"writing window open\" flips the member's state."),
        };

        Assert.NotEqual(MemberStates.WritingWindowOpen, MemberState_Resolver.Resolve(entries));
    }

    /// <summary>
    /// The worse direction, and the reason this could not be deferred: a false StandingBy is silence.
    /// It disarms the orphan recovery that is the app's only proof a monitor is dead, so a session
    /// whose last entry merely mentions the phrase would be detected by nothing at all.
    /// </summary>
    [Theory]
    [InlineData("report filed, standing by for your verdict")]
    [InlineData("I am NOT standing by — still working")]
    [InlineData("added the STANDING BY marker, 484 tests pass")]
    [InlineData("> STANDING BY")]
    [InlineData("\"STANDING BY\" is the new marker")]
    public void MentioningTheDeclarationIsNotDeclaring(string body)
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "brief", "do the work"),
            Build_Entry(2, ChannelAuthors.Implementer, "a report", body),
        };

        Assert.Equal(MemberStates.AwaitingSupervisorReview, MemberState_Resolver.Resolve(entries));
    }

    /// <summary>
    /// And the declarations that must still work, including the markdown a member actually writes.
    /// Without these the anchoring could be "fixed" by never matching anything.
    /// </summary>
    [Theory]
    [InlineData("STANDING BY")]
    [InlineData("STANDING BY — waiting on rev-3's review")]
    [InlineData("**STANDING BY** — nothing owed, nothing running")]
    [InlineData("standing by. Nothing in flight.")]
    [InlineData("- STANDING BY")]
    [InlineData("🚩 STANDING BY")]
    public void ADeclarationAtTheStartOfALineStillCounts(string body)
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "brief", "do the work"),
            Build_Entry(2, ChannelAuthors.Implementer, "a report", body),
        };

        Assert.Equal(MemberStates.StandingBy, MemberState_Resolver.Resolve(entries));
    }

    /// <summary>A longer token that merely starts the same way is a different word.</summary>
    [Fact]
    public void ALongerTokenStartingWithTheMarkerIsNotTheMarker()
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "brief", "do the work"),
            Build_Entry(2, ChannelAuthors.Implementer, "a report", "STANDING BYSTANDER, still working"),
        };

        Assert.Equal(MemberStates.AwaitingSupervisorReview, MemberState_Resolver.Resolve(entries));
    }

    /// <summary>
    /// A declaration further down the body counts — members write a line of context first, and the
    /// rule is "begins a line", not "begins the entry".
    /// </summary>
    [Fact]
    public void ADeclarationOnALaterLineCounts()
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "brief", "do the work"),
            Build_Entry(2, ChannelAuthors.Implementer, "a report", "Commit abc1234, 489 tests pass.\n\nSTANDING BY — waiting for the next brief."),
        };

        Assert.Equal(MemberStates.StandingBy, MemberState_Resolver.Resolve(entries));
    }
}
