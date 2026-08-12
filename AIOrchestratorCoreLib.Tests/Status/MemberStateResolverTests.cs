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
        // The brief is part of the fixture because it is part of every real channel: a member is
        // only ever "awaiting review" on work it was asked to do. Without it this reads as a member
        // that has never been briefed, which is waiting for WORK, not for a verdict.
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "BRIEF", "fix the five defects"),
            Build_Entry(2, ChannelAuthors.Implementer, "WRITING WINDOW OPEN", "starting"),
            Build_Entry(3, ChannelAuthors.Implementer, "WRITING WINDOW CLOSED", "five fixes landed"),
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

    /// <summary>
    /// A REVIEWER can never hold a writing window: it is launched without Write/Edit/NotebookEdit,
    /// so it cannot open one. Before this was enforced, a reviewer filing a finding ABOUT a window
    /// pinned itself with NO EXIT — the window test runs before the last-entry rules, so its own
    /// next entry could not clear it, and once markers became member-gated neither could the
    /// supervisor. Reviewers are the members most likely to write about markers; it is their job.
    /// </summary>
    [Fact]
    public void AReviewerCannotOpenAWritingWindow()
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "review the marker fix", "depth standard"),
            Build_Entry(2, ChannelAuthors.Reviewer, "F3 · MEDIUM · WRITING WINDOW OPEN is matched too loosely", "the finding"),
        };

        Assert.NotEqual(MemberStates.WritingWindowOpen, MemberState_Resolver.Resolve(entries));
    }

    /// <summary>An implementer, by contrast, is exactly who may announce one.</summary>
    [Fact]
    public void AnImplementerStillOpensAWindowWithTheSameSubject()
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "brief", "do the work"),
            Build_Entry(2, ChannelAuthors.Implementer, "WRITING WINDOW OPEN — Parser.cs", "starting"),
        };

        Assert.Equal(MemberStates.WritingWindowOpen, MemberState_Resolver.Resolve(entries));
    }

    /// <summary>
    /// The subject lost its quotation protection when it moved to a token match: the exact string the
    /// body rule FORBIDS was declaring from a subject. Same exclusion, both fields.
    ///
    /// EVERY CASE HERE MUST REACH THE QUOTATION REFUSAL, and one of them did not. `on 'STANDING BY'
    /// and why it is needed` begins with a letter, so once the marker was required to LEAD the subject
    /// it was refused for that reason and returned before the quotation check ran — it passed with the
    /// check deleted outright, in the very test written to answer a quotation finding. It has moved to
    /// the leading Theory below, where it pins what it actually pins.
    ///
    /// So each subject here opens with the quotation mark: the marker leads, the tail carries no
    /// clause break, and the ONLY thing standing between it and a declaration is the refusal. A
    /// reviewer verified by mutation that this is a live decision and not a belief — deleting the two
    /// lines flips six subjects to declaring, and the block quote below is the one case where nothing
    /// else in the resolver catches it.
    /// </summary>
    [Theory]
    [InlineData("\"STANDING BY\" is the new marker")]
    [InlineData("'STANDING BY' and why it is needed")]
    [InlineData("> STANDING BY")]
    [InlineData("> STANDING BY, quoting the member I am reviewing")]
    public void AQuotedMarkerInASubjectIsNotADeclaration(string subject)
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "brief", "do the work"),
            Build_Entry(2, ChannelAuthors.Implementer, subject, "the report"),
        };

        Assert.Equal(MemberStates.AwaitingSupervisorReview, MemberState_Resolver.Resolve(entries));
    }

    /// <summary>
    /// WHERE THE MOVED CASE LIVES NOW, saying what it pins: a subject that talks its way up to the
    /// marker is discussion, and the marker has to LEAD. It was sitting in the quotation Theory above,
    /// passing on this rule instead of on the one that Theory exists for.
    ///
    /// Kept rather than deleted because the property is real and was otherwise pinned only by a corpus
    /// subject: two different sentences can reach a state, and a case that names which one it means is
    /// worth more than the assertion count suggests.
    /// </summary>
    [Theory]
    [InlineData("on 'STANDING BY' and why it is needed")]
    [InlineData("a note about STANDING BY for the next member")]
    public void AMarkerThatDoesNotLeadTheSubjectIsNotADeclaration(string subject)
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "brief", "do the work"),
            Build_Entry(2, ChannelAuthors.Implementer, subject, "the report"),
        };

        Assert.Equal(MemberStates.AwaitingSupervisorReview, MemberState_Resolver.Resolve(entries));
    }

    /// <summary>
    /// AUTHOR-WORD DRIFT MUST NOT PIN A WINDOW. Open under a clean header, close under a drifted one
    /// — `FROM **implementer**`, which agents really write — and the close used to be invisible,
    /// because a drifted word parses to Unknown and window markers are member-gated. A missing close
    /// reads as still-open forever, and the app then tells the member to append the close it already
    /// appended.
    /// </summary>
    [Fact]
    public void ADriftedAuthorWordStillClosesTheWindow()
    {
        var entries = ChannelEntry_Parser.Parse_All(string.Join('\n',
        [
            "## [1] FROM supervisor — 2026-08-12 09:00 — brief",
            "do the work",
            "",
            "## [2] FROM implementer — 2026-08-12 09:10 — WRITING WINDOW OPEN — Parser.cs",
            "starting the batch",
            "",
            "## [3] FROM **implementer** — 2026-08-12 09:40 — WRITING WINDOW CLOSED — done",
            "batch landed",
        ]));

        Assert.Equal(3, entries.Count);
        Assert.Equal(MemberStates.AwaitingSupervisorReview, MemberState_Resolver.Resolve(entries));
    }

    /// <summary>
    /// THE ASYMMETRY, and it is the reason normalising the author word was not enough on its own.
    /// Normalisation handles decoration; a genuinely unrecognised author word still parses to
    /// Unknown, and its close was still skipped.
    ///
    /// A missed close pins the member FOREVER — the app then tells it to append the close it already
    /// appended — while a spurious close merely ends a window early, which the member undoes by
    /// declaring again. So ambiguity resolves toward NOT-open on both scans: strict for the open,
    /// lenient for the close.
    /// </summary>
    [Fact]
    public void AnUnknownAuthorStillClosesAWindow()
    {
        var entries = ChannelEntry_Parser.Parse_All(string.Join('\n',
        [
            "## [1] FROM supervisor — 2026-08-12 09:00 — brief",
            "do the work",
            "",
            "## [2] FROM implementer — 2026-08-12 09:10 — WRITING WINDOW OPEN — Parser.cs",
            "starting the batch",
            "",
            "## [3] FROM imp-1 — 2026-08-12 09:40 — WRITING WINDOW CLOSED — done",
            "batch landed",
        ]));

        Assert.Equal(ChannelAuthors.Unknown, entries[2].Author);
        Assert.NotEqual(MemberStates.WritingWindowOpen, MemberState_Resolver.Resolve(entries));
    }

    /// <summary>
    /// But an uncertain author still cannot OPEN one. The leniency is one-directional by design — if
    /// it ran both ways, an unparseable header would create the very state this is meant to prevent.
    /// </summary>
    [Fact]
    public void AnUnknownAuthorCannotOpenAWindow()
    {
        var entries = ChannelEntry_Parser.Parse_All(string.Join('\n',
        [
            "## [1] FROM supervisor — 2026-08-12 09:00 — brief",
            "do the work",
            "",
            "## [2] FROM imp-1 — 2026-08-12 09:10 — WRITING WINDOW OPEN — Parser.cs",
            "starting the batch",
        ]));

        Assert.Equal(ChannelAuthors.Unknown, entries[1].Author);
        Assert.NotEqual(MemberStates.WritingWindowOpen, MemberState_Resolver.Resolve(entries));
    }

    /// <summary>A SUPERVISOR close is still ignored — it is known not to be the member.</summary>
    [Fact]
    public void ASupervisorCannotCloseAMembersWindow()
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "brief", "do the work"),
            Build_Entry(2, ChannelAuthors.Implementer, "WRITING WINDOW OPEN — Parser.cs", "starting"),
            Build_Entry(3, ChannelAuthors.Supervisor, "WRITING WINDOW CLOSED — closing this for you", "done"),
        };

        Assert.Equal(MemberStates.WritingWindowOpen, MemberState_Resolver.Resolve(entries));
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
    /// Without these the matcher could be "fixed" by never matching anything.
    ///
    /// THE VARIANTS MOVED FROM THE BODY TO THE SUBJECT, because that is where a declaration now lives
    /// — see the reclassified case at the bottom of this file. What is under test did not change, and
    /// is why this is a Theory: the formatting members really write must not defeat the match.
    /// </summary>
    [Theory]
    [InlineData("STANDING BY")]
    [InlineData("STANDING BY — waiting on rev-3's review")]
    [InlineData("**STANDING BY** — nothing owed, nothing running")]
    [InlineData("standing by. Nothing in flight.")]
    [InlineData("- STANDING BY")]
    [InlineData("🚩 STANDING BY")]
    public void ADeclarationSurvivesTheMarkdownMembersWrite(string subject)
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "hold", "nothing queued for you"),
            Build_Entry(2, ChannelAuthors.Implementer, subject, "Nothing owed, nothing running."),
        };

        Assert.Equal(MemberStates.StandingBy, MemberState_Resolver.Resolve(entries));
    }

    /// <summary>
    /// THE SHAPE BARE PLACEMENT COULD NOT SURVIVE: a declaration and owed content sharing one subject.
    /// A reviewer went through the live channels on this machine and found members write it regularly
    /// — that is what these fixtures are evidence of, and it is the only claim the corpus supports.
    /// It cannot say how often the new rule will be wrong, because every one of those entries was
    /// written before any placement rule was taught.
    ///
    /// These three are REAL SUBJECTS from that corpus, not invented ones. Each would have declared
    /// itself idle while owing its supervisor an answer — and the second one is the very entry that
    /// was recorded as the fourth independent confirmation of the defect this branch exists to fix. A
    /// rule that silences the report which established the defect is not finished.
    ///
    /// Two properties carry all three: the marker must LEAD the subject (the second trails it behind
    /// the content), and what follows must not open a second clause (the first with a semicolon, the
    /// third with a colon).
    /// </summary>
    [Theory]
    [InlineData("standing by; clearing the nudge, and ONE ITEM STILL OPEN ON YOUR SIDE")]
    [InlineData("answering the three questions left open in your [9] and [10], then standing by")]
    [InlineData("STANDING BY — one correction to the queued residual: the wrong file is named")]
    public void ADeclarationSharingItsSubjectWithOwedContentIsNotADeclaration(string subject)
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "brief", "do the work"),
            Build_Entry(2, ChannelAuthors.Implementer, subject, "the body"),
        };

        Assert.Equal(MemberStates.AwaitingSupervisorReview, MemberState_Resolver.Resolve(entries));
    }

    /// <summary>
    /// AND THE COMMA IS NOT A CLAUSE BREAK, asserted on its own because it is the one piece of the
    /// rule that had to be decided rather than measured. "nothing owed, nothing running" is a single
    /// thought and is what members actually write; treating every comma as a second clause would
    /// leave almost nothing able to declare at all.
    /// </summary>
    [Fact]
    public void ACommaInsideTheDeclarationDoesNotBreakIt()
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "hold", "nothing queued"),
            Build_Entry(2, ChannelAuthors.Implementer, "STANDING BY — nothing owed, nothing running", "."),
        };

        Assert.Equal(MemberStates.StandingBy, MemberState_Resolver.Resolve(entries));
    }

    /// <summary>
    /// A longer token that merely starts the same way is a different word.
    ///
    /// ALSO MOVED TO THE SUBJECT, and not cosmetically: left in the body this would still have
    /// expected AwaitingSupervisorReview and passed no matter what the matcher did, because a body is
    /// no longer where a declaration is read from. A test that cannot fail is worse than no test.
    /// </summary>
    [Fact]
    public void ALongerTokenStartingWithTheMarkerIsNotTheMarker()
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "brief", "do the work"),
            Build_Entry(2, ChannelAuthors.Implementer, "STANDING BYSTANDER, still working", "in flight"),
        };

        Assert.Equal(MemberStates.AwaitingSupervisorReview, MemberState_Resolver.Resolve(entries));
    }

    /// <summary>
    /// RECLASSIFIED, and this assertion is now the inverse of what it used to make. It said a report
    /// whose BODY ends with the marker is standing by — that is the entry shape members file every
    /// single time, and reading it as idle is what silenced the verdict reminder and printed `idle` in
    /// the owner's topic for a member whose work was sitting unread.
    ///
    /// PLACEMENT is what separates the two states, because nothing else in the entry can: a
    /// declaration is titled by the marker, a report is titled by its result and may close with the
    /// marker. The role commands already say a declaration is a ONE-LINE entry; they now say where the
    /// marker goes, so the docs and the matcher teach one thing.
    ///
    /// The failure directions are not symmetric, which is why placement is read this way round. A
    /// member that puts the marker only in the body of a genuinely-nothing-owed entry costs its
    /// supervisor one spurious wake. The reverse — titling filed work with the marker — costs that
    /// work its reader, silently, which is the defect this whole branch exists to remove.
    /// </summary>
    [Fact]
    public void AReportThatOnlyClosesWithTheMarkerIsStillAwaitingAVerdict()
    {
        var entries = new[]
        {
            Build_Entry(1, ChannelAuthors.Supervisor, "brief", "do the work"),
            Build_Entry(2, ChannelAuthors.Implementer, "a report", "Commit abc1234, 489 tests pass.\n\nSTANDING BY — waiting for the next brief."),
        };

        Assert.Equal(MemberStates.AwaitingSupervisorReview, MemberState_Resolver.Resolve(entries));
    }
}
