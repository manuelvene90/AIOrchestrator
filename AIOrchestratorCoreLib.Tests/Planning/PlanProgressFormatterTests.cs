using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Planning.PlanProgress;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning;

/// <summary>
/// The completion figure the owner reads on /status, /progress and the periodic push. One wording
/// for all three, so they cannot quote different numbers for the same ledger.
/// </summary>
public class PlanProgressFormatterTests
{
    [Fact]
    public void Describe_Counts_LeadsWithDoneOutOfTotalAndAPercentage()
    {
        Assert.Equal("57/76 done (75%)", PlanProgress_Formatter.Describe_Counts(Build(done: 57, total: 76)));
    }

    [Fact]
    public void Describe_Counts_AppendsRunningAndBlockedOnlyWhenThereAreSome()
    {
        Assert.Equal(
            "10/20 done (50%) · 2 running · 1 task blocked",
            PlanProgress_Formatter.Describe_Counts(Build(done: 10, total: 20, inProgress: 2, blocked: 1)));

        Assert.Equal("10/20 done (50%)", PlanProgress_Formatter.Describe_Counts(Build(done: 10, total: 20)));
    }

    /// <summary>
    /// THE NOUN IS THE POINT, and it is why this says "task" rather than shouting. The member roster
    /// printed directly under this count says BLOCKED ON OWNER for a SESSION; when this count said a
    /// bare "1 BLOCKED", one word meant two things in one message and the owner read a blocked ledger
    /// LINE as a stuck session — they asked what had got stuck while nothing was (2026-08-19).
    /// </summary>
    [Fact]
    public void Describe_Counts_SaysBlockedTasksRatherThanABareBlocked()
    {
        Assert.Equal(
            "10/20 done (50%) · 1 task blocked",
            PlanProgress_Formatter.Describe_Counts(Build(done: 10, total: 20, blocked: 1)));

        Assert.Equal(
            "10/20 done (50%) · 3 tasks blocked",
            PlanProgress_Formatter.Describe_Counts(Build(done: 10, total: 20, blocked: 3)));
    }

    /// <summary>
    /// Truncation, not rounding: 75 of 76 is 98.68%, and showing "100%" for unfinished work would
    /// make the only figure that must be trustworthy the one that lies.
    /// </summary>
    [Fact]
    public void Describe_Counts_TruncatesSoOnly_AllDone_Reads100()
    {
        Assert.Equal("75/76 done (98%)", PlanProgress_Formatter.Describe_Counts(Build(done: 75, total: 76)));
        Assert.Equal("76/76 done (100%)", PlanProgress_Formatter.Describe_Counts(Build(done: 76, total: 76)));
    }

    [Fact]
    public void Describe_Counts_OmitsThePercentageForAnEmptyLedger()
    {
        Assert.Equal("0/0 done", PlanProgress_Formatter.Describe_Counts(Build(done: 0, total: 0)));
    }

    // ── /progress AS A LEDGER, owner directive 2026-08-13 ─────────────────────────────────────────
    //
    // It rendered as PROSE: every task of a kind joined onto one line with ` · `, so four tasks became
    // roughly fifteen wrapped visual lines on their phone. Their words: "I want it to be a list of the
    // main macro tasks, not detailed low level list of tens of tasks, the main macro elements, let's
    // say 7/8 max, and it should be in the [-], [>], [X] format."
    //
    // AND THEN, 11:14, correcting the reading of the 7/8: "The done rows must not be hidden. I want to
    // see all the rows, it must not be truncated. If all the tasks don't fit in 8/9 rows it means you
    // haven't managed to group the tasks sufficiently into macrotasks."
    //
    // So 7/8 is a statement about how many macro tasks a LEDGER should have, not a rendering limit.
    // The answer to a ledger of hundreds is a shorter ledger, not a shorter message — a renderer that
    // truncated would hide the author's failure to group, which is the opposite of what was asked.
    // Nothing here caps, counts a remainder, or reorders: one line per ledger line, as written.

    /// <summary>THE SHAPE, exactly — one line per task, each carrying the ledger's own marker.</summary>
    [Fact]
    public void Describe_Ledger_IsOneLinePerTaskCarryingItsLedgerMarker()
    {
        var progress = Parse(
            "- [>] fix R1 — clear the awaiting-answer flag only after a confirmed send",
            "- [>] restyle the topic status line",
            "- [ ] rebase fix/quiet-clock-ignores-app onto master",
            "- [x] audit R2–R8 against current master",
            "- [-] rewrite the mirror loop — superseded")!;

        Assert.Equal(
            string.Join('\n', new[]
            {
                "[>] fix R1 — clear the awaiting-answer flag only after a confirmed send",
                "[>] restyle the topic status line",
                "[ ] rebase fix/quiet-clock-ignores-app onto master",
                "[x] audit R2–R8 against current master",
                "[-] rewrite the mirror loop — superseded",
            }),
            PlanProgress_Formatter.Describe_Ledger(progress));
    }

    /// <summary>
    /// THE DEFECT ITSELF, as a property rather than as one more fixture: no printed line may carry
    /// more than one task. The old renderer joined a whole kind with ` · `, and a fixture only ever
    /// pins the arrangement it was written for — this asks the question of every line.
    ///
    /// Each line minus its four-character marker must be EXACTLY one of the ledger's task texts.
    /// A joined line is not equal to any of them, so it cannot pass.
    /// </summary>
    [Fact]
    public void Describe_Ledger_NeverPutsTwoTasksOnOneLine()
    {
        string[] tasks =
        [
            "fix the parser",
            "rebase the branch",
            "audit the hooks",
            "rewrite the mirror loop",
        ];

        var progress = Parse(
            $"- [>] {tasks[0]}",
            $"- [ ] {tasks[1]}",
            $"- [x] {tasks[2]}",
            $"- [-] {tasks[3]}")!;

        var lines = PlanProgress_Formatter.Describe_Ledger(progress).Split('\n');

        Assert.Equal(tasks.Length, lines.Length);

        foreach (var line in lines)
            Assert.Contains(line[4..], tasks);
    }

    /// <summary>EVERY line opens with one of the ledger's five markers, over a mixed ledger.</summary>
    [Fact]
    public void Describe_Ledger_OpensEveryLineWithALedgerMarker()
    {
        var lines = PlanProgress_Formatter.Describe_Ledger(Mixed(blocked: 2, inProgress: 3, open: 4, done: 5, notDoing: 6)).Split('\n');

        foreach (var line in lines)
            Assert.Contains(line[..3], new[] { "[!]", "[>]", "[ ]", "[x]", "[-]" });
    }

    /// <summary>
    /// NOTHING IS EVER OMITTED, at any size — the owner's correction of 11:14, and the property that
    /// replaces the cap I had been briefed to build. 292 lines in, 292 lines out.
    ///
    /// Asserted on a ledger far past any plausible "macro" one on purpose: the guarantee has to hold
    /// exactly where a truncating renderer would have been tempting, because that is where hiding
    /// rows would have hidden the ledger author's failure to group them.
    /// </summary>
    [Fact]
    public void Describe_Ledger_PrintsEveryLineHoweverBigTheLedgerIs()
    {
        var progress = Mixed(blocked: 9, inProgress: 40, open: 36, done: 178, notDoing: 29);

        Assert.Equal(292, PlanProgress_Formatter.Describe_Ledger(progress).Split('\n').Length);
    }

    /// <summary>
    /// AND NO REMAINDER LINE, ever. A truncating renderer announces itself with a counted tail, so
    /// the absence of one is the observable difference — asserted separately from the count above,
    /// which a renderer that printed 291 rows plus a "+1 more" would otherwise satisfy.
    /// </summary>
    [Fact]
    public void Describe_Ledger_NeverCollapsesARemainderIntoACount()
    {
        var text = PlanProgress_Formatter.Describe_Ledger(Mixed(open: 40));

        Assert.DoesNotContain("more", text);
        Assert.Contains("[ ] open 39", text);
    }

    /// <summary>
    /// DONE ROWS STAY, and this is a straight owner decision of 2026-08-13 rather than a balance
    /// struck against the older rule. "A done line is never printed under any circumstance" was
    /// written when /progress showed 593 of 683 lines; the answer to that ledger is a shorter LEDGER,
    /// and the owner has taken responsibility for grouping it. Dropped rows stay for the same reason.
    /// </summary>
    [Fact]
    public void Describe_Ledger_ShowsDoneAndDroppedRows()
    {
        var text = PlanProgress_Formatter.Describe_Ledger(Mixed(inProgress: 3, done: 2, notDoing: 1));

        Assert.Contains("[x] done 0", text);
        Assert.Contains("[-] not doing 0", text);
    }

    /// <summary>
    /// LEDGER ORDER, not kind order. The ledger is a document somebody wrote in an order that means
    /// something — grouping the five states on the way out reorders their rows, and the owner asked
    /// to see their rows.
    ///
    /// The fixture interleaves states so that no grouping can pass: a renderer that emitted blocked
    /// first, or done last, produces a different sequence from this one.
    /// </summary>
    [Fact]
    public void Describe_Ledger_KeepsTheLedgersOwnOrder()
    {
        var progress = Parse(
            "- [x] first, and finished",
            "- [ ] second, and open",
            "- [!] third, and blocked",
            "- [x] fourth, also finished",
            "- [>] fifth, and running")!;

        Assert.Equal(
            string.Join('\n', new[]
            {
                "[x] first, and finished",
                "[ ] second, and open",
                "[!] third, and blocked",
                "[x] fourth, also finished",
                "[>] fifth, and running",
            }),
            PlanProgress_Formatter.Describe_Ledger(progress));
    }

    /// <summary>
    /// ONE VOCABULARY OUT, whatever went in. `- [X]` and `- [x]` are one state written by two hands
    /// — the parser has always folded them for the COUNTS, and printing the raw capture would put
    /// both spellings in front of the owner inside a single message.
    /// </summary>
    [Fact]
    public void Describe_Ledger_NormalisesTheDoneMarkerToOneSpelling()
    {
        var text = PlanProgress_Formatter.Describe_Ledger(Parse("- [X] shouted", "- [x] quiet")!);

        Assert.Equal("[x] shouted\n[x] quiet", text);
    }

    /// <summary>A ledger with no lines to render says so rather than printing an empty message.</summary>
    [Fact]
    public void Describe_Ledger_SaysSoWhenThereIsNothingToRender()
    {
        Assert.Equal("the ledger is empty", PlanProgress_Formatter.Describe_Ledger(Build(done: 3, total: 3)));
    }

    /// <summary>
    /// A ledger built from counts alone, for the wording tests. Its task LISTS are empty, which is
    /// why it is not used for the rendering ones.
    /// </summary>
    /// <summary>
    /// THE OWNER IS TOLD A BLOCK IS THEIRS, and how much it is holding up — their request, 2026-08-19:
    /// *"so I know there's something blocking on my end, and also how much it's blocking"*.
    ///
    /// It rides this existing count and adds no send path, which was their hard condition: *"I don't
    /// want it to continue spontaneously prompting or to have another annoying loop pop up."*
    /// </summary>
    [Fact]
    public void Describe_Counts_SaysWhenABlockNeedsTheOwnerAndWhetherAnythingElseCanMove()
    {
        // Something else can still move: 10 of 20 done, 1 blocked, so 9 are neither.
        Assert.Equal(
            "10/20 done (50%) · 1 task blocked, needs you — the rest continues",
            PlanProgress_Formatter.Describe_Counts(Build(done: 10, total: 20, blocked: 1, blockedOnOwner: 1)));

        // Everything left is blocked behind them — done + blocked accounts for the whole total.
        Assert.Equal(
            "8/10 done (80%) · 2 tasks blocked, needs you — nothing else can move",
            PlanProgress_Formatter.Describe_Counts(Build(done: 8, total: 10, blocked: 2, blockedOnOwner: 2)));
    }

    /// <summary>
    /// MIXED: three lines blocked, only one of them theirs. Collapsing to "3 tasks blocked, needs you"
    /// would send them after two blocks that were never theirs — the B9 mistake this marker exists to
    /// stop, reintroduced by the renderer instead of the marker.
    /// </summary>
    [Fact]
    public void Describe_Counts_DistinguishesTheirBlocksFromTheRest()
    {
        Assert.Equal(
            "10/20 done (50%) · 3 tasks blocked · 1 needs you — the rest continues",
            PlanProgress_Formatter.Describe_Counts(Build(done: 10, total: 20, blocked: 3, blockedOnOwner: 1)));
    }

    /// <summary>A block that is nobody's business but the crew's says nothing about the owner.</summary>
    [Fact]
    public void Describe_Counts_SaysNothingAboutTheOwnerWhenNoBlockIsTheirs()
    {
        Assert.Equal(
            "10/20 done (50%) · 2 tasks blocked",
            PlanProgress_Formatter.Describe_Counts(Build(done: 10, total: 20, blocked: 2)));
    }

    /// <summary>
    /// `- [?]` is blocked BY EVERY MEASURE THAT ALREADY EXISTED, and additionally the owner's. A
    /// reader asking "how many lines cannot move" must not get a smaller number because the
    /// supervisor was more specific about why.
    /// </summary>
    [Fact]
    public void AnOwnerBlockedLineCountsAsBlockedToo()
    {
        var progress = Parse("- [?] decide the schema — blocked on: you", "- [>] build the parser")!;

        Assert.Equal(2, progress.Total);
        Assert.Equal(1, progress.Blocked);
        Assert.Equal(1, progress.BlockedOnOwner);
        Assert.Equal(1, progress.InProgress);
    }

    /// <summary>
    /// THE CASE THE OWNER ASKED THIS FOR (2026-08-19). A review that finds defects grows the
    /// DENOMINATOR, so half an hour of real work can read as a FALLING percentage — which is exactly
    /// what they were looking at when they asked. The delta is what makes that legible rather than
    /// alarming: three tasks added, one finished, percentage down four points, all visible at once.
    /// </summary>
    [Fact]
    public void Describe_Counts_ShowsWhatChangedSinceTheOwnerWasLastTold()
    {
        Assert.Equal(
            // 16/27 truncates to 59%, 17/30 to 56% — a three-point DROP across half an hour in which
            // a task was finished. That inversion is the whole reason this was asked for.
            "17(+1)/30(+3) done (56% -3%)",
            PlanProgress_Formatter.Describe_Counts(Build(done: 17, total: 30), new PlanProgressSnapshot(16, 27)));
    }

    /// <summary>
    /// A delta of zero is not printed. "(+0)" three times is noise on a phone, and the ABSENCE of a
    /// bracket already says the thing did not move.
    /// </summary>
    [Fact]
    public void Describe_Counts_PrintsOnlyTheDeltasThatAreNotZero()
    {
        Assert.Equal(
            "17(+1)/30 done (56% +3%)",
            PlanProgress_Formatter.Describe_Counts(Build(done: 17, total: 30), new PlanProgressSnapshot(16, 30)));

        Assert.Equal(
            "17/30 done (56%)",
            PlanProgress_Formatter.Describe_Counts(Build(done: 17, total: 30), new PlanProgressSnapshot(17, 30)));
    }

    /// <summary>No baseline — the first message of a run — reads exactly as it always did.</summary>
    [Fact]
    public void Describe_Counts_WithNoBaselineIsUnchanged()
    {
        Assert.Equal(
            PlanProgress_Formatter.Describe_Counts(Build(done: 17, total: 30)),
            PlanProgress_Formatter.Describe_Counts(Build(done: 17, total: 30), null));
    }

    /// <summary>
    /// THE TAIL IS THE SAME CODE EITHER WAY. /status and the periodic push quote one ledger, and the
    /// delta form adding a second copy of "running / blocked / not doing" is how those two surfaces
    /// would start disagreeing — the failure item 10 exists to prevent.
    /// </summary>
    [Fact]
    public void Describe_Counts_CarriesTheSameTailWithOrWithoutABaseline()
    {
        var progress = Build(done: 17, total: 30, inProgress: 2, blocked: 1, blockedOnOwner: 1);

        const string tail = " · 2 running · 1 task blocked, needs you — the rest continues";

        Assert.EndsWith(tail, PlanProgress_Formatter.Describe_Counts(progress));
        Assert.EndsWith(tail, PlanProgress_Formatter.Describe_Counts(progress, new PlanProgressSnapshot(16, 27)));
    }

    static IPlanProgress Build(int done, int total, int inProgress = 0, int blocked = 0, int notDoing = 0, int blockedOnOwner = 0)
    {
        return PlanProgress_Factory.Create(done, inProgress, blocked, notDoing, total, null, [], [], [], null, null, blockedOnOwner);
    }

    /// <summary>A ledger of N lines per kind, named so each one is identifiable in an assertion.</summary>
    static IPlanProgress Mixed(int blocked = 0, int inProgress = 0, int open = 0, int done = 0, int notDoing = 0)
    {
        List<string> lines = [];

        Add(lines, "!", "blocked", blocked);
        Add(lines, ">", "in progress", inProgress);
        Add(lines, " ", "open", open);
        Add(lines, "x", "done", done);
        Add(lines, "-", "not doing", notDoing);

        return Parse([.. lines])!;

        static void Add(List<string> lines, string marker, string label, int count)
        {
            for (var index = 0; index < count; index++)
                lines.Add($"- [{marker}] {label} {index}");
        }
    }

    static IPlanProgress? Parse(params string[] lines)
    {
        return PlanLedger_Parser.Parse_OrNull(string.Join('\n', lines));
    }
}
