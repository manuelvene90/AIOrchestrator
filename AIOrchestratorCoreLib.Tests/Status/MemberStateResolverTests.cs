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
    ///
    /// A window is a statement about what the MEMBER is doing, so only a member can make it. This
    /// now passes for TWO reasons — the author gate and the body anchoring — so the member-authored
    /// case below carries the anchoring on its own. A case with two routes to its result pins
    /// neither, which is the defect this branch shipped once already.
    /// </summary>
    [Fact]
    public void ASupervisorCannotOpenAMembersWindowByDiscussingIt()
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "a bug you should know about",
                "WRITING WINDOW OPEN is matched as a bare substring, which is the bug."),
        };

        Assert.NotEqual(MemberStates.WritingWindowOpen, MemberState_Resolver.Resolve(entries));
    }

    /// <summary>
    /// The same discussion from a MEMBER, where the author gate cannot help and the body rule is the
    /// only thing deciding. Members discuss the vocabulary constantly — they are instructed to.
    /// </summary>
    [Fact]
    public void AMemberDiscussingTheWindowMarkerMidSentenceDoesNotOpenAWindow()
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "brief", "do the work"),
            Build_Entry(2, ChannelAuthors.Implementer, "a report",
                "so an entry that merely QUOTES \"writing window open\" flips the member's state."),
        };

        Assert.NotEqual(MemberStates.WritingWindowOpen, MemberState_Resolver.Resolve(entries));
    }

    // ── SUBJECTS ARE MATCHED AS TOKENS, NOT BY POSITION ─────────────────────────────────────────
    //
    // Measured against all 95 marker-bearing subjects on this machine: start-anchoring the subject
    // lost 10 GENUINE declarations to catch 1 discussion. These are the real shapes it lost.

    /// <summary>House style: the result first, the marker after it.</summary>
    [Fact]
    public void ASubjectThatNamesAResultBeforeItsMarkerStillDeclares()
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "brief", "do the work"),
            Build_Entry(2, ChannelAuthors.Implementer, "TASK 1 committed d123150. WRITING WINDOW OPEN for the hardening", "batch 2"),
        };

        Assert.Equal(MemberStates.WritingWindowOpen, MemberState_Resolver.Resolve(entries));
    }

    /// <summary>The compound that missed BOTH markers at once under the position rule.</summary>
    [Fact]
    public void TheCombinedWindowSubjectStillOpensAWindow()
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "brief", "do the work"),
            Build_Entry(2, ChannelAuthors.Implementer, "WRITING + MUTATION WINDOW OPEN — Parser.cs, Model.cs", "starting"),
        };

        Assert.Equal(MemberStates.WritingWindowOpen, MemberState_Resolver.Resolve(entries));
    }

    /// <summary>
    /// THE INVERSION, and the reason the subject rule was a regression rather than a lost
    /// opportunity. A missing close reads as still-open, so a CLOSED marker that stops counting pins
    /// the member in WritingWindowOpen — the four-hour failure, reached through the other door. This
    /// subject is a live one from the corpus.
    /// </summary>
    [Fact]
    public void ACompoundSubjectStillCLOSESTheWindow()
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "brief", "run the mutations"),
            Build_Entry(2, ChannelAuthors.Implementer, "MUTATION WINDOW OPEN", "seven mutations"),
            Build_Entry(3, ChannelAuthors.Implementer, "A3 COMPLETE · 512 tests · MUTATION WINDOW CLOSED", "all seven caught"),
        };

        Assert.Equal(MemberStates.AwaitingSupervisorReview, MemberState_Resolver.Resolve(entries));
    }

    /// <summary>A longer word merely containing the marker is still not the marker.</summary>
    [Fact]
    public void ASubjectTokenMustHaveWordBoundariesOnBothSides()
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "brief", "do the work"),
            Build_Entry(2, ChannelAuthors.Implementer, "STANDING BYSTANDER", "still working"),
        };

        Assert.Equal(MemberStates.AwaitingSupervisorReview, MemberState_Resolver.Resolve(entries));
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
