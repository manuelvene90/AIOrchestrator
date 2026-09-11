using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Formatting;
using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Planning.PlanProgress;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TopicStatusMember;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// The per-topic status line: posted once, edited forever, never pinned.
///
/// The owner refused pinning for a reason that decides this file's contents: "working now" elsewhere
/// in the app is FILE MTIME, true for ~2 minutes after a turn ends, and pinned that wrong state
/// would sit in front of them permanently. So NO MTIME appears here — every word comes from parsed
/// channel state and the ledger, which is why these tests can build a line from entries alone with
/// no filesystem at all. If a future change makes that impossible, that is the signal.
///
/// THE TOPIC NAME IS NOT IN IT ANY MORE (owner, 2026-08-24): *"the name of the topic is not needed,
/// I already know where I am, and if I don't I have it at the top of the screen."* The opening field
/// is now the literal word PULSE, and what it is FOR is telling the two recurring messages apart at
/// a glance — this one, edited constantly and never notifying, against the half-hourly digest that
/// opens with STATUS. That is why every expectation below reads `PULSE · …` where it used to name a
/// topic, and why the builder no longer takes a title at all.
///
/// REWRITTEN 2026-09-10 for the owner's 2026-09-09 six-field decision (Brief C): the periodic STATUS
/// message is gone, so PULSE is now the ONLY status surface and carries six fields in order —
/// `⏳ waiting on you`, `sup · …`, the member rows, `last`, `<merged>/<total> merged · NN %`, and
/// `updated HH:MM` — each its OWN LINE rather than one long header. Tests below that pinned the old
/// single-line lead ("PULSE · 72/113 · 63%") are rewritten for the field that replaced them; tests
/// whose subject survives (idle vs. not, the future-stamp guard, the row budget, no padding, the
/// bullet convention) keep testing it against the new shape.
/// </summary>
public class TopicStatusLineBuilderTests
{
    static readonly DateTime NOW = new(2026, 8, 12, 12, 30, 0);

    /// <summary>
    /// REPLACES TheLeadLineCarriesTheLedgerCountAndPercent. The ledger figures moved OFF the lead
    /// line and onto field 5, their own line, worded "merged" rather than a bare fraction (owner,
    /// 2026-09-09: "the honest word — merged, not done"). The lead line is now the bare word PULSE,
    /// possibly decorated with a mode glyph, and nothing else.
    /// </summary>
    [Fact]
    public void TheMergedFieldCarriesTheLedgerCountAndPercent()
    {
        var line = TopicStatusLine_Builder.Build(Progress(72, 113), [], null, NOW, aMessageIsAlreadyPosted: false);

        Assert.Equal("PULSE\n72/113 merged · 63 %\nupdated 12:30", line);
    }

    /// <summary>
    /// NOTHING TO SAY MEANS SAY NOTHING. With no ledger, no live member and no history, the line
    /// would have been a lead word and nothing else — a message that tells the owner nothing they
    /// are not already looking at. Item 15.
    /// </summary>
    [Fact]
    public void AnOrchestrationWithNothingToReportEmitsNothing()
    {
        Assert.Equal("", TopicStatusLine_Builder.Build(null, [], null, NOW, aMessageIsAlreadyPosted: false));
    }

    /// <summary>
    /// REPLACES ALeadLineWithALedgerIsWorthWriting. A real ledger is still substance worth writing —
    /// it just prints on the merged field's own line rather than beside the lead word.
    /// </summary>
    [Fact]
    public void TheMergedFieldAloneIsWorthWriting()
    {
        Assert.Equal(
            "PULSE\n3/4 merged · 75 %\nupdated 12:30",
            TopicStatusLine_Builder.Build(Progress(3, 4), [], null, NOW, aMessageIsAlreadyPosted: false));
    }

    /// <summary>
    /// REPLACES TheLeadLineSaysHowLongTheFiguresHaveNotMoved. The owner asked for this line to say
    /// how long the figures have STOOD STILL, rather than carry a delta: it refreshes constantly, so
    /// a difference "would go back to 0" (2026-08-19). That clause now rides on the MERGED field,
    /// beside the figures it is about, rather than on the lead line that no longer carries figures.
    /// </summary>
    [Fact]
    public void TheMergedFieldSaysHowLongTheFiguresHaveNotMoved()
    {
        Assert.Equal(
            "PULSE\n3/4 merged · 75 % · unchanged 25 min\nupdated 12:30",
            TopicStatusLine_Builder.Build(
                Progress(3, 4), [], null, NOW, aMessageIsAlreadyPosted: false,
                figuresUnchangedFor: TimeSpan.FromMinutes(27)));
    }

    /// <summary>Figures that JUST moved say nothing on the merged field — and neither does an unknown span.</summary>
    [Fact]
    public void TheMergedFieldSaysNothingWhileTheFiguresAreStillMoving()
    {
        Assert.Equal(
            "PULSE\n3/4 merged · 75 %\nupdated 12:30",
            TopicStatusLine_Builder.Build(
                Progress(3, 4), [], null, NOW, aMessageIsAlreadyPosted: false,
                figuresUnchangedFor: TimeSpan.FromMinutes(2)));

        Assert.Equal(
            "PULSE\n3/4 merged · 75 %\nupdated 12:30",
            TopicStatusLine_Builder.Build(
                Progress(3, 4), [], null, NOW, aMessageIsAlreadyPosted: false,
                figuresUnchangedFor: null));
    }

    /// <summary>
    /// A closed member does not resurrect the line either — the contract says it falls off, and one
    /// message must not disagree with itself about whether a member exists.
    /// </summary>
    [Fact]
    public void AnOrchestrationWhoseOnlyMemberIsClosedEmitsNothing()
    {
        var line = TopicStatusLine_Builder.Build(
            null, [Member("imp-1", Brief("the old task", "2026-08-12 09:00"), isClosed: true)], null, NOW, aMessageIsAlreadyPosted: false);

        Assert.Equal("", line);
    }

    /// <summary>
    /// N8: a PLAN.md of nothing but struck-out `- [-]` lines parses to a NON-NULL progress with
    /// Total 0. Weakening the check to `progress != null` left 610 green, because no case covered a
    /// ledger that exists and owes nothing.
    /// </summary>
    [Fact]
    public void ALedgerOfNothingButDroppedLinesHasNothingToSay()
    {
        Assert.Equal("", TopicStatusLine_Builder.Build(Progress(0, 0), [], null, NOW, aMessageIsAlreadyPosted: false));
    }

    /// <summary>
    /// The FALLBACK lives in the builder, so the decider sees the text that is actually sent. With a
    /// message already up, nothing-to-say is the BARE LEAD WORD — leaving silence would freeze the
    /// last row it printed, with a running duration for a member that has been closed.
    ///
    /// It used to be the bare TITLE. Since the topic name left the line (owner, 2026-08-24) the
    /// fallback is the literal `PULSE`, which is the one thing this line always has to say.
    /// </summary>
    [Fact]
    public void WithAMessageAlreadyPostedNothingToSayIsTheBareLeadWord()
    {
        Assert.Equal(
            "PULSE",
            TopicStatusLine_Builder.Build(null, [], null, NOW, aMessageIsAlreadyPosted: true));
    }

    /// <summary>And with nothing posted it is still silence — the two are decided in one place.</summary>
    [Fact]
    public void WithNoMessagePostedNothingToSayIsStillSilence()
    {
        Assert.Equal("", TopicStatusLine_Builder.Build(null, [], null, NOW, aMessageIsAlreadyPosted: false));
    }

    /// <summary>
    /// ADAPTED, not weakened: a member row now carries a STATE WORD too (owner, 2026-09-09 — "the
    /// state field is new … it sits between the task and the duration"), so the row reads
    /// who · what · state · how long rather than who · what · how long. Asserted as the exact row,
    /// which is what "who, what, state, and how long" actually means.
    /// </summary>
    [Fact]
    public void AMemberRowIsWhoWhatStateAndHowLong()
    {
        var line = TopicStatusLine_Builder.Build(
            null,
            [Member("imp-1", Brief("committing the marker fix", "2026-08-12 12:26"))],
            null,
            NOW, aMessageIsAlreadyPosted: false);

        // "under 5 min", not "4 min": the duration STEPS to five minutes since 2026-09-10 (owner's
        // ruling), because a per-minute reading made PULSE's text differ on every tick and the repost
        // gate compares that text. Rendered by the ONE duration formatter this repo has — item 12
        // forbids a second one just to shorten a column — with the floor applied before it, so
        // "1 h 5 min" is still spelled in exactly one place.
        Assert.Equal("• imp-1 · committing the marker fix · working · under 5 min", line.Split('\n')[1]);
    }

    /// <summary>
    /// A DECLARED-idle member reads "idle" — that is the whole point of the marker existing, and the
    /// owner should not be shown a stale task for somebody who has said they have nothing running.
    ///
    /// THE FIXTURE CHANGED AND THE SUPERVISOR OVERRULED THE OLD ONE. It used to answer a review brief
    /// with the subject "STANDING BY — review filed", which is a FILED REVIEW asserting that it reads
    /// idle — the exact collapse this pair of tests now separates. The declaration that belongs here
    /// is one with nothing behind it: the supervisor asked for nothing and the member confirms it has
    /// nothing running.
    /// </summary>
    [Fact]
    public void ADeclaredIdleMemberReadsStandingBy()
    {
        var entries = new[]
        {
            Entry(1, ChannelAuthors.Supervisor, "hold — nothing queued for you", "2026-08-12 11:00"),
            Entry(2, ChannelAuthors.Reviewer, "STANDING BY — nothing owed, nothing running", "2026-08-12 12:00"),
        };

        var line = TopicStatusLine_Builder.Build(null, [Member("rev-3", entries)], null, NOW, aMessageIsAlreadyPosted: false);

        Assert.Contains("• rev-3 · standing by", line);
        Assert.DoesNotContain("hold — nothing queued", line);
    }

    /// <summary>
    /// AND THE OTHER HALF OF THE SPLIT: a member that FILED and then declared is waiting on the
    /// SUPERVISOR, so it must not read idle and must still show what is pending.
    ///
    /// This is the case the overruled fixture asserted backwards. It costs the owner the one queue
    /// they can actually unblock: a topic reading `rev-3 idle` for a reviewer whose review has been
    /// sitting unread says there is nothing to look at, which is the opposite of true.
    ///
    /// Asserted apart from the case above so neither can pass for the other's reason — the two differ
    /// only in whether work was filed behind the marker.
    /// </summary>
    [Fact]
    public void AMemberThatFiledAndThenDeclaredIsNotIdle()
    {
        var entries = new[]
        {
            Entry(1, ChannelAuthors.Supervisor, "review the marker fix", "2026-08-12 11:00"),
            Entry(2, ChannelAuthors.Reviewer, "review filed — 3 findings, one blocking", "2026-08-12 12:00"),
            Entry(3, ChannelAuthors.Reviewer, "STANDING BY", "2026-08-12 12:01"),
        };

        var line = TopicStatusLine_Builder.Build(null, [Member("rev-3", entries)], null, NOW, aMessageIsAlreadyPosted: false);

        // Asserted POSITIVELY as well, on the whole row: "does not contain idle" is also satisfied by
        // a row that does not exist, so on its own it would survive the member vanishing entirely.
        Assert.DoesNotContain("• rev-3 · idle", line);
        Assert.StartsWith("• rev-3 · review the marker fix", line.Split('\n')[1]);
    }

    /// <summary>
    /// AWAITING A VERDICT IS NOT IDLE. That member is waiting on the SUPERVISOR, and showing it as
    /// idle would hide the one queue the owner can actually unblock.
    /// </summary>
    [Fact]
    public void AMemberAwaitingAVerdictIsNotShownAsStandingBy()
    {
        var entries = new[]
        {
            Entry(1, ChannelAuthors.Supervisor, "fix the ledger denominator", "2026-08-12 12:00"),
            Entry(2, ChannelAuthors.Implementer, "done, 565 tests pass", "2026-08-12 12:20"),
        };

        var line = TopicStatusLine_Builder.Build(null, [Member("imp-1", entries)], null, NOW, aMessageIsAlreadyPosted: false);

        Assert.DoesNotContain("standing by", line);
        Assert.Contains("fix the ledger", line);
    }

    /// <summary>A member that has never been briefed has nothing to show, and says so.</summary>
    [Fact]
    public void ANeverBriefedMemberReadsStandingBy()
    {
        var line = TopicStatusLine_Builder.Build(
            null,
            [Member("rev-1", [Entry(1, ChannelAuthors.Reviewer, "rev-1 online", "2026-08-12 12:00")])],
            null,
            NOW, aMessageIsAlreadyPosted: false);

        Assert.Contains("• rev-1 · standing by", line);
    }

    /// <summary>Contract item 4: a closed member drops off rather than lingering as a stale row.</summary>
    [Fact]
    public void AClosedMemberDropsOffTheLine()
    {
        var brief = Brief("the old task", "2026-08-12 09:00");

        var line = TopicStatusLine_Builder.Build(
            null,
            [Member("imp-1", brief, isClosed: true), Member("imp-2", Brief("the live task", "2026-08-12 12:25"))],
            null,
            NOW, aMessageIsAlreadyPosted: false);

        Assert.DoesNotContain("imp-1", line);
        Assert.Contains("imp-2", line);
    }

    /// <summary>
    /// A FUTURE stamp yields no duration rather than a confident wrong number — the one formatter
    /// this repo has returns null for it, and this line must not invent one. A supervisor really did
    /// stamp an entry 10 hours ahead.
    ///
    /// ADAPTED: the row still carries its new STATE WORD even with no duration to append — only the
    /// duration field drops, not the state beside it.
    /// </summary>
    [Fact]
    public void AFutureStampShowsTheTaskAndStateWithoutADuration()
    {
        var line = TopicStatusLine_Builder.Build(
            null,
            [Member("imp-1", Brief("the task", "2026-08-13 23:00"))],
            null,
            NOW, aMessageIsAlreadyPosted: false);

        // Asserted as the WHOLE member ROW, not as "contains the task": a duration appended after it
        // is exactly what must not happen, and a Contains check cannot see a trailing anything.
        Assert.Equal("• imp-1 · the task · working", line.Split('\n')[1]);
    }

    [Fact]
    public void TheLastLineIsAddedOnlyWhenThereIsSomethingToSay()
    {
        Assert.Contains("last", TopicStatusLine_Builder.Build(null, [], Last("gate cleared on 34e5515"), NOW, aMessageIsAlreadyPosted: false));
        Assert.DoesNotContain("last", TopicStatusLine_Builder.Build(null, [], null, NOW, aMessageIsAlreadyPosted: false));
        Assert.DoesNotContain("last", TopicStatusLine_Builder.Build(null, [], Last("   "), NOW, aMessageIsAlreadyPosted: false));
    }

    /// <summary>
    /// THE WHOLE SHAPE, as the owner approved it 2026-09-09 (Brief C): six fields, each its OWN LINE
    /// — the lead word, the member rows (now carrying a state word each), `last`, the merged count
    /// (worded "merged", not a bare fraction) and the heartbeat. Pinned here because the individual
    /// assertions above can all pass while the line reads as something nobody would want on their
    /// phone.
    ///
    /// SUPERSEDES the 2026-08-13 restyle this test used to pin (5 lines, ledger folded into the lead
    /// line, no state word, no heartbeat). The bullet convention, the absence of padding and the
    /// divider being gone all survive from that restyle unchanged; what moved is which fields share a
    /// line.
    /// </summary>
    [Fact]
    public void TheApprovedShape()
    {
        var line = TopicStatusLine_Builder.Build(
            Progress(72, 113),
            [
                Member("imp-1", Brief("committing the marker fix", "2026-08-12 12:26")),
                Member("rev-2", Brief("reviewing the hooks branch", "2026-08-12 12:18")),
                Member("rev-3", [Entry(1, ChannelAuthors.Reviewer, "rev-3 online", "2026-08-12 12:00")]),
            ],
            Last("gate cleared on 34e5515"),
            NOW, aMessageIsAlreadyPosted: false);

        var lines = line.Split('\n');

        Assert.Equal(7, lines.Length);
        Assert.Equal("PULSE", lines[0]);
        // Both durations STEPPED to five minutes (2026-09-10): four minutes is below the first step
        // and twelve floors to ten. The floor never overstates — the row says less than the truth.
        Assert.Equal("• imp-1 · committing the marker fix · working · under 5 min", lines[1]);
        Assert.Equal("• rev-2 · reviewing the hooks branch · working · 10 min", lines[2]);
        Assert.Equal("• rev-3 · standing by", lines[3]);
        Assert.Equal("last · gate cleared on 34e5515", lines[4]);
        Assert.Equal("72/113 merged · 63 %", lines[5]);
        Assert.Equal("updated 12:30", lines[6]);
    }

    /// <summary>
    /// THE HEADLINE OF THE RESTYLE, and until now invisible to the suite. rev-1 F3, which I
    /// reproduced before writing this: NO fixture anywhere had a brief longer than FOUR words, so
    /// `Summarize_Task` never truncated in a single test — `MEMBER_TASK_WORDS` could be set to 40 and
    /// 695 tests stayed green. The whole point of the change (a row that fits a phone) rested on a
    /// number nothing observed.
    ///
    /// Seven words in, and BOTH budgets come out of one fixture: four on the member row, six on the
    /// `last` row, which is what the `+2` means and is otherwise a constant nobody can see either.
    ///
    /// The subject is chosen to survive `Summarize_Task`'s other passes so the count is the only
    /// thing under test: no bookkeeping prefix, no clause break (no comma, dash, "so", "because"),
    /// and not one word from FILLER_WORDS — "onto" is deliberately not "into", which IS filler.
    /// </summary>
    [Fact]
    public void ALongBriefIsCutToTheRowBudget()
    {
        const string sevenWords = "migrate every supervisor session onto worktree isolation";

        var lines = TopicStatusLine_Builder.Build(
            null,
            [Member("imp-9", Brief(sevenWords, "2026-08-12 12:26"))],
            Last(sevenWords),
            NOW, aMessageIsAlreadyPosted: false).Split('\n');

        Assert.Equal("• imp-9 · migrate every supervisor session · working · under 5 min", lines[1]);
        Assert.Equal("last · migrate every supervisor session onto worktree", lines[2]);
    }

    /// <summary>
    /// THE OWNER'S FIRST COMPLAINT: "wide spaces". Asserted as a PROPERTY over every row rather than
    /// as one expected string, because the padding can come back in any one of four places — the
    /// title, the idle row, the task column, the duration column — and a fixture only ever pins the
    /// one shape it was written for.
    ///
    /// Two spaces is the whole test: a proportional font cannot align columns, so any run of them is
    /// width spent on nothing.
    /// </summary>
    [Fact]
    public void NoRowIsPaddedWithASpaceRun()
    {
        var line = TopicStatusLine_Builder.Build(
            Progress(72, 113),
            [
                Member("imp-1", Brief("committing the marker fix", "2026-08-12 12:26")),
                Member("rev-3", [Entry(1, ChannelAuthors.Reviewer, "rev-3 online", "2026-08-12 12:00")]),
                Member("imp-4", Brief("the task", "2026-08-13 23:00")),
            ],
            Last("gate cleared on 34e5515"),
            NOW, aMessageIsAlreadyPosted: false);

        Assert.DoesNotContain("  ", line);
    }

    /// <summary>
    /// THE OWNER'S SECOND COMPLAINT: the rows did not read as rows. Every member row now opens with
    /// its own bullet — and the `last` row deliberately does NOT, so it cannot be misread as a member
    /// that is somehow called "last".
    /// </summary>
    [Fact]
    public void EveryMemberRowOpensWithABulletAndTheLastRowDoesNot()
    {
        var lines = TopicStatusLine_Builder.Build(
            null,
            [
                Member("imp-1", Brief("committing the marker fix", "2026-08-12 12:26")),
                Member("rev-3", [Entry(1, ChannelAuthors.Reviewer, "rev-3 online", "2026-08-12 12:00")]),
            ],
            Last("gate cleared on 34e5515"),
            NOW, aMessageIsAlreadyPosted: false).Split('\n');

        Assert.StartsWith("• imp-1", lines[1]);
        Assert.StartsWith("• rev-3", lines[2]);
        Assert.StartsWith("last ", lines[3]);
    }

    /// <summary>
    /// THE OWNER'S THIRD COMPLAINT: 28 box-drawing dashes wrapped onto a second line of their own, so
    /// the divider that was meant to separate two rows was itself two rows. It is gone, and the
    /// bullets do that job. Asserted on the CHARACTER, not on the old 28-dash constant, so bringing
    /// the divider back at any width still reddens this.
    /// </summary>
    [Fact]
    public void TheWideDividerIsGone()
    {
        var line = TopicStatusLine_Builder.Build(
            Progress(3, 4),
            [Member("imp-1", Brief("committing the marker fix", "2026-08-12 12:26"))],
            Last("gate cleared on 34e5515"),
            NOW, aMessageIsAlreadyPosted: false);

        Assert.DoesNotContain("─", line);
    }

    /// <summary>
    /// ONE reading of an agent stamp, and the two used to disagree inside a single message: the
    /// builder printed no duration for a future-stamped entry while the engine's own comparison
    /// promoted that same entry to `last` and held it there until real time caught up.
    /// </summary>
    [Fact]
    public void AFutureStampIsRefusedByTheSharedReaderTheEngineAlsoUses()
    {
        Assert.False(SessionDuration_Formatter.Try_ReadTrustedStamp("2026-08-13 23:00", NOW, out _));
        Assert.False(SessionDuration_Formatter.Try_ReadTrustedStamp("not a date", NOW, out _));
        Assert.True(SessionDuration_Formatter.Try_ReadTrustedStamp("2026-08-12 12:26", NOW, out _));

        // The skew tolerance survives: a stamp a minute ahead is a minute-rounded clock, not a lie.
        Assert.True(SessionDuration_Formatter.Try_ReadTrustedStamp("2026-08-12 12:31", NOW, out _));
    }

    /// <summary>
    /// A SOLO'S ROW MUST NOT CLAIM "standing by" WHILE IT IS MID-WORK — the owner sent this back on
    /// 2026-08-24, the app's own busy line reading "still at it — running a command" directly above
    /// "solo-1 · standing by".
    ///
    /// It was not a race and not a stale read: Find_LastBrief_OrNull looks for a `FROM supervisor`
    /// entry, a basic orchestration HAS no supervisor, and a solo's member channel is the owner
    /// channel — which carries only `FROM solo`, `FROM owner` and `FROM app`. So the brief was
    /// structurally always null and the row printed "standing by" 100% of the time, for every solo,
    /// in every state. This test builds exactly that channel shape.
    ///
    /// WHAT THE ROW SHOWS INSTEAD IS THE SOLO'S OWN LAST ENTRY, not a state word alone: a solo is
    /// never briefed and never will be, so its own last subject is the only answer to "what is this
    /// member working on" — summarised to the same MEMBER_TASK_WORDS budget every other member row
    /// uses, dated from the same stamp, and now carrying the same STATE WORD every other filled row
    /// does. Asserted as the WHOLE ROW rather than as "contains the subject", because the duration
    /// and the state word both ride on it and a Contains check cannot see either.
    /// </summary>
    [Fact]
    public void ASolosRow_WithNoSupervisorToBriefIt_ShowsItsOwnLastEntryInsteadOfStandingBy()
    {
        // A solo's own channel. No supervisor entry exists here and none ever will — that is what a
        // basic orchestration IS.
        IReadOnlyList<IChannelEntry> entries =
        [
            Entry(1, ChannelAuthors.Owner, "go", "2026-08-12 12:20"),
            Entry(2, ChannelAuthors.Solo, "tab shifting fix", "2026-08-12 12:26"),
        ];

        var line = TopicStatusLine_Builder.Build(
            Progress(1, 4), [Member("solo-1", entries)], null, NOW, aMessageIsAlreadyPosted: false);

        Assert.Equal("• solo-1 · tab shifting fix · working · under 5 min", line.Split('\n')[1]);
        Assert.DoesNotContain("standing by", line);
    }

    /// <summary>
    /// THE FALLBACK IS SOLO-ONLY, and this is the pair that pins it. An implementer or a reviewer with
    /// no brief is genuinely waiting to be told what to do, so "standing by" is a true and useful
    /// statement about it — see ANeverBriefedMemberReadsStandingBy (rev-1) and TheApprovedShape
    /// (rev-3), both of which own the other side of this rule and neither of which may move.
    ///
    /// Reading a member's own last entry as its task there would trade one wrong answer for another:
    /// a reviewer's last word is what it SAID, not what it was asked to do.
    /// </summary>
    [Fact]
    public void AnImplementersRow_WithNoBrief_StillSaysStandingBy()
    {
        IReadOnlyList<IChannelEntry> entries =
        [
            Entry(1, ChannelAuthors.Implementer, "imp-1 online", "2026-08-12 12:00"),
            Entry(2, ChannelAuthors.Implementer, "poked at the parser while waiting", "2026-08-12 12:26"),
        ];

        var line = TopicStatusLine_Builder.Build(
            Progress(1, 4), [Member("imp-1", entries)], null, NOW, aMessageIsAlreadyPosted: false);

        Assert.Contains("• imp-1 · standing by", line);
        Assert.DoesNotContain("poked at the parser", line);
    }

    /// <summary>
    /// The other half of the solo's row: when the state really IS idle, "standing by" is still what it
    /// should say. Without this, the fix above could be "never say standing by", which would trade
    /// one wrong answer for another.
    ///
    /// The declaration is what makes it idle, and the row is asserted WHOLE: a bare Contains would
    /// also be satisfied by some other member's row, and this line has only one member on it.
    /// </summary>
    [Fact]
    public void ASolosRow_WithNothingDeclared_StillSaysStandingBy()
    {
        IReadOnlyList<IChannelEntry> entries =
        [
            Entry(1, ChannelAuthors.Solo, "STANDING BY", "2026-08-12 12:00"),
        ];

        var line = TopicStatusLine_Builder.Build(
            Progress(1, 4), [Member("solo-1", entries)], null, NOW, aMessageIsAlreadyPosted: false);

        Assert.Equal("• solo-1 · standing by", line.Split('\n')[1]);
    }

    /// <summary>
    /// ADDED, 2026-09-10: the mode glyph moved OFF the Telegram topic NAME and onto PULSE's own
    /// header line (owner, 2026-09-09 — "fewer renames, fewer service messages"). This is genuinely
    /// new surface with no other coverage in this file, since every test above uses the default
    /// (Normal) mode.
    /// </summary>
    [Fact]
    public void TheHeaderCarriesTheDeliveryModeGlyph()
    {
        var deferred = TopicStatusLine_Builder.Build(
            Progress(1, 4), [], null, NOW, aMessageIsAlreadyPosted: false,
            fields: new TopicStatusFields(Mode: TelegramDeliveryModes.Deferred));

        var silenced = TopicStatusLine_Builder.Build(
            Progress(1, 4), [], null, NOW, aMessageIsAlreadyPosted: false,
            fields: new TopicStatusFields(Mode: TelegramDeliveryModes.Silenced));

        Assert.Equal("🌙 PULSE", deferred.Split('\n')[0]);
        Assert.Equal("🔕 PULSE", silenced.Split('\n')[0]);
    }

    /// <summary>
    /// AWAY ARRIVED HERE ON 2026-09-10, from the topic name — the claim
    /// AwayModePolicyTests.AwayAndMode_ShowTogether used to make about a title. Away is app-wide, so
    /// on a name one toggle renamed every open topic at once and Telegram wrote a service message
    /// into each thread: the owner was told, once per orchestration, something they had just done
    /// themselves. Here it is one character in a message that is being edited anyway.
    /// </summary>
    [Fact]
    public void TheHeaderCarriesTheAwayGlyph()
    {
        Assert.Equal("✈ PULSE", Header(new TopicStatusFields(IsAway: true)));
    }

    /// <summary>
    /// QUIET, likewise moved from the name (AwayModePolicyTests.Quiet_ShowsOnItsOwnTopic). Unlike
    /// away it is PER TOPIC — this one orchestration stopped asking after three unanswered messages,
    /// which may simply mean the owner is working in another topic.
    /// </summary>
    [Fact]
    public void TheHeaderCarriesTheQuietGlyph()
    {
        Assert.Equal("🤐 PULSE", Header(new TopicStatusFields(IsQuiet: true)));
    }

    /// <summary>
    /// TERMINAL, the third glyph to arrive from the name: the owner is sitting in THIS
    /// orchestration's terminal, so nothing is pushed and nothing blocks on a tap.
    /// </summary>
    [Fact]
    public void TheHeaderCarriesTheTerminalGlyph()
    {
        Assert.Equal("💻 PULSE", Header(new TopicStatusFields(Presence: OwnerPresenceModes.Terminal)));
    }

    /// <summary>
    /// AWAY SUPERSEDES QUIET — the rule kept verbatim from the topic name it moved off, and the
    /// reason is unchanged: away already means every orchestration has stopped asking, so drawing
    /// both states says one fact twice on the one line the owner reads at a glance.
    /// </summary>
    [Fact]
    public void AwaySupersedesQuietOnTheHeader()
    {
        Assert.Equal("✈ PULSE", Header(new TopicStatusFields(IsAway: true, IsQuiet: true)));
    }

    /// <summary>
    /// TERMINAL REPLACES THE DELIVERY GLYPH rather than joining it — also moved verbatim. Sitting in
    /// the terminal is WHAT silences the topic, so 💻 🔕 would state one fact twice: the
    /// presence/delivery conflation this mode exists to remove, rendered.
    /// </summary>
    [Fact]
    public void TerminalPresenceReplacesTheDeliveryGlyph()
    {
        var header = Header(new TopicStatusFields(
            Mode: TelegramDeliveryModes.Silenced,
            Presence: OwnerPresenceModes.Terminal));

        Assert.Equal("💻 PULSE", header);
    }

    /// <summary>
    /// AWAY STILL SHOWS BESIDE TERMINAL, and the distinction is the whole reason both exist: away is
    /// about the owner's PHONE and is app-wide, terminal is about where they are sitting for THIS one
    /// endeavour. Neither implies the other, so collapsing them would lose a fact.
    /// </summary>
    [Fact]
    public void AwayStillShowsBesideTerminal()
    {
        var header = Header(new TopicStatusFields(
            IsAway: true,
            Presence: OwnerPresenceModes.Terminal));

        Assert.Equal("✈ 💻 PULSE", header);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // FIELD 2 — "sup · …": the supervisor's own declared state, plus a usage-limit pause the app
    // adds on its own. Untested anywhere before this rewrite: the old lead-line-only builder had no
    // such field, and ContextOnTheStatusLineTests exercises only the context half of this row.
    // ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// CONTENT PROBE (a) from Brief C's "Done when": a session paused for a usage limit renders the
    /// pause AND its resume time. This is the ONE thing the app adds to the supervisor's own words —
    /// what the false stall alerts of 2026-09-09 were actually looking at.
    /// </summary>
    [Fact]
    public void ASupervisorPausedForAUsageLimitRendersThePauseAndItsResumeTime()
    {
        var fields = new TopicStatusFields(UsageLimitResumeAt: new DateTime(2026, 8, 12, 14, 0, 0));

        var line = TopicStatusLine_Builder.Build(
            Progress(1, 4), [], null, NOW, aMessageIsAlreadyPosted: false, fields: fields);

        Assert.Contains("sup · paused for usage limit until 14:00", line);
    }

    /// <summary>
    /// The other half of field 2: the supervisor's own one-line state, in its own words, with its
    /// declared clock — the skill's new `STATE:` line (Brief C) is what feeds this.
    /// </summary>
    [Fact]
    public void TheSupervisorsDeclaredStateRendersWithADeclaredClock()
    {
        var fields = new TopicStatusFields(
            SupervisorDeclaredState: "waiting for imp-2's review, then I hand you the merge",
            SupervisorDeclaredAt: new DateTime(2026, 8, 12, 12, 12, 0));

        var line = TopicStatusLine_Builder.Build(
            Progress(1, 4), [], null, NOW, aMessageIsAlreadyPosted: false, fields: fields);

        Assert.Contains("sup · waiting for imp-2's review, then I hand you the merge · declared 12:12", line);
    }

    /// <summary>
    /// CLAUDE.md decision 12, applied to this new clock: a declaration stamped in the FUTURE must not
    /// print a confident "declared HH:MM" — the state itself still shows, only the clock is refused.
    /// </summary>
    [Fact]
    public void AFutureDeclaredAtIsNotPrintedButTheStateStillShows()
    {
        var fields = new TopicStatusFields(
            SupervisorDeclaredState: "on it",
            SupervisorDeclaredAt: NOW.AddHours(10));

        var line = TopicStatusLine_Builder.Build(
            Progress(1, 4), [], null, NOW, aMessageIsAlreadyPosted: false, fields: fields);

        Assert.Contains("sup · on it", line);
        Assert.DoesNotContain("declared", line);
    }

    /// <summary>
    /// NULL DEGRADES, IT DOES NOT INVENT (builder's own docstring): with nothing declared and no
    /// pause, field 2 is OMITTED rather than filled with a placeholder row.
    /// </summary>
    [Fact]
    public void WithNothingDeclaredAndNoPauseTheSupervisorRowIsOmitted()
    {
        var line = TopicStatusLine_Builder.Build(Progress(1, 4), [], null, NOW, aMessageIsAlreadyPosted: false);

        Assert.DoesNotContain("sup ·", line);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // FIELD 1 — "⏳ waiting on you": open questions AND ledger lines blocked on the owner.
    // ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// CONTENT PROBE (b) from Brief C's "Done when": a ledger `[?]` line renders under
    /// `⏳ waiting on you`, WITH ITS ID — the owner's own example is "your browser pass on
    /// FIN-D-277", and the id is the part that makes the row actionable rather than merely present.
    /// </summary>
    [Fact]
    public void ALedgerLineBlockedOnTheOwnerRendersUnderWaitingOnYouWithItsId()
    {
        var progress = ProgressWithLedgerLine(1, 4, "?", "your browser pass on FIN-D-277");

        var line = TopicStatusLine_Builder.Build(progress, [], null, NOW, aMessageIsAlreadyPosted: false);

        Assert.Equal("⏳ waiting on you · ledger: your browser pass on FIN-D-277", line.Split('\n')[1]);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // FIELD 3 — the member roster: up to 4 live rows, a closed COUNT rather than a roster.
    // ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// CONTENT PROBE (c) from Brief C's "Done when": 9 members of which 7 are closed render TWO live
    /// rows plus "7 closed" — never a closed roster (Out: "the closed roster").
    /// </summary>
    [Fact]
    public void NineMembersOfWhichSevenAreClosedRenderTwoLiveRowsAndASevenClosedCount()
    {
        List<ITopicStatusMember> members =
        [
            Member("imp-1", Brief("fix the parser", "2026-08-12 12:26")),
            Member("rev-2", [Entry(1, ChannelAuthors.Reviewer, "STANDING BY", "2026-08-12 12:00")]),
        ];

        for (var i = 1; i <= 7; i++)
            members.Add(Member($"closed-{i}", [], isClosed: true));

        var line = TopicStatusLine_Builder.Build(null, members, null, NOW, aMessageIsAlreadyPosted: false);
        var lines = line.Split('\n');

        Assert.StartsWith("• imp-1", lines[1]);
        Assert.StartsWith("• rev-2", lines[2]);
        Assert.Equal("7 closed", lines[3]);
        Assert.DoesNotContain("closed-", line);
    }

    static IPlanProgress Progress(int done, int total)
    {
        return PlanProgress_Factory.Create(done, 0, 0, 0, total, null, [], [], []);
    }

    /// <summary>
    /// PULSE's first line only, for the glyph tests. A ledger is passed so the line has substance and
    /// is written at all — the header is not a message on its own — and no member, so the assertion
    /// is about the header and nothing below it.
    /// </summary>
    static string Header(TopicStatusFields fields)
    {
        var line = TopicStatusLine_Builder.Build(
            Progress(1, 4), [], null, NOW, aMessageIsAlreadyPosted: false, fields: fields);

        return line.Split('\n')[0];
    }

    /// <summary>A ledger that carries one line, for field 1's ledger-derived-ask probe.</summary>
    static IPlanProgress ProgressWithLedgerLine(int done, int total, string marker, string text)
    {
        return PlanProgress_Factory.Create(
            done, 0, 0, 0, total, null, [], [], [], null, [new PlanLedgerLine(marker, text)]);
    }

    static IReadOnlyList<IChannelEntry> Brief(string subject, string stamp)
    {
        return [Entry(1, ChannelAuthors.Supervisor, subject, stamp)];
    }

    static ITopicStatusMember Member(string memberId, IReadOnlyList<IChannelEntry> entries, bool isClosed = false)
    {
        return TopicStatusMember_Factory.Create(memberId, entries, isClosed);
    }

    static IChannelEntry Entry(int index, ChannelAuthors author, string subject, string stamp)
    {
        return ChannelEntry_Factory.Create(
            index, author, stamp, subject, "body",
            $"## [{index}] FROM {author} — {stamp} — {subject}\nbody");
    }

    /// <summary>
    /// Field 4's value as ONE argument, which is what it became on 2026-09-10. The clock defaults to
    /// null so every test that only ever cared about the subject reads as it did — and a test that
    /// wants the clock now has to name the event it belongs to, which is the whole point.
    /// </summary>
    static TopicLastEvent? Last(string subject, DateTime? at = null)
    {
        return new TopicLastEvent(subject, at);
    }
}
