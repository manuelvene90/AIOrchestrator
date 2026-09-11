using AIOrchestratorCoreLib.Bridge;
using Xunit;
using AIOrchestratorCoreLib.Channels;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE RULES THE PROTOCOL ASKS FOR, CHECKED INSTEAD OF HOPED FOR.
///
/// <para>
/// A mechanical audit of the supervisor protocol on 2026-09-07 counted 229 imperative rules and 14
/// pairs that cannot both be obeyed — "lead with the decision" against a mandatory receipt 396 lines
/// below it, "one open question at a time" against "ask in passing" 783 lines below it. The lower one
/// wins because it is what the model read last. These pin the handful of rules a machine can settle,
/// so they stop depending on which of 229 instructions got weighed.
/// </para>
/// <para>
/// The conforming examples are real entries from the owner's own topic; so are most of the faults.
/// </para>
/// </summary>
public class OwnerMessageContractTests
{
    static void Assert_Clean(string body)
    {
        Assert.Empty(OwnerMessage_Contract.Check(body));
    }

    static void Assert_Faults(string body, OwnerMessageFaults expected)
    {
        Assert.Contains(expected, OwnerMessage_Contract.Check(body));
    }

    // ── What conforms ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AnAnswerWithAQuestionAndOptions_Conforms()
    {
        Assert_Clean(
            "No card, like the old app. A card wall at cutover would be a regression for every migrating customer.\n"
            + "\n"
            + "QUESTION: Does the trial ask for a card at signup?\n"
            + "OPTION: No card\n"
            + "OPTION: Card required");
    }

    [Fact]
    public void APlainStatementWithNoQuestion_Conforms()
    {
        Assert_Clean("Both tickets are merged. The pricing page now names the Live cap per tier.");
    }

    [Fact]
    public void AnEmptyBody_Conforms()
    {
        Assert_Clean("");
    }

    /// <summary>A screenshot carries a path by design — it is how a picture reaches the phone.</summary>
    [Fact]
    public void AnImageLine_IsNotCode()
    {
        Assert_Clean("The comparison table now shows the cap.\nIMAGE: /Users/nvene/Desktop/pricing/comparison-table.png");
        Assert_Clean("The plan-card mockup is ready.\nATTACH: /Users/nvene/repo/mockups/plan-card.html");
    }

    /// <summary>Member ids are how the owner refers to the crew. They must never be flagged.</summary>
    [Fact]
    public void MemberIdsAndTicketCodes_AreNotCode()
    {
        Assert_Clean("imp-1 finished FIN-D-272 and rev-2 cleared it. Nothing is waiting on you.");
    }

    // ── The faults ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TwoQuestionsInOneEntry_IsAFault()
    {
        Assert_Faults(
            "QUESTION: Merge 272 now?\nOPTION: Merge\nOPTION: Hold\nQUESTION: And 267?\nOPTION: Yes\nOPTION: No",
            OwnerMessageFaults.TwoQuestions);
    }

    /// <summary>
    /// The topic's own shape: a question, then more prose under it. The owner answers from a lock
    /// screen and never sees what came after.
    /// </summary>
    [Fact]
    public void ProseBelowTheQuestion_IsAFault()
    {
        Assert_Faults(
            "QUESTION: Merge 272 into dev now?\nOPTION: Merge\nOPTION: Hold\nimp-1 is on 274 meanwhile.",
            OwnerMessageFaults.ProseAfterTheQuestion);
    }

    [Theory]
    [InlineData("OPTION: Merge\nOPTION: Hold")]
    [InlineData("Ready when you are.\nOPTION: Go\nOPTION: Wait")]
    public void ButtonsWithNoQuestion_IsNoLongerThisChecksBusiness(string body)
    {
        // It moved to OwnerQuestion_Contract, which does not coach it after the fact — it REFUSES
        // to forward the question. Two homes for one rule told the agent twice, and the telling
        // here had become untrue: it said "a tap answers something that was never asked", of
        // buttons the app no longer sends. See OwnerQuestionContractTests.
        Assert_Clean(body);
    }

    /// <summary>Markers that belong to the question are not prose and must not trip the check.</summary>
    [Fact]
    public void DeadlineAndDefaultBelowTheQuestion_AreNotProse()
    {
        Assert_Clean("QUESTION: Merge 272?\nOPTION: Merge\nOPTION: Hold\nDEADLINE: 30m\nDEFAULT: 2");
    }

    /// <summary>
    /// THE BURIED ANSWER, in its smallest form. The protocol mandates a receipt before the work and
    /// also says line one is the answer; the receipt wins, and this is what the owner reads.
    /// </summary>
    [Theory]
    [InlineData("Noted — I'll look at the trial policy and report back.")]
    [InlineData("Got it. Checking the Stripe config now.")]
    [InlineData("I'll verify the merged code and come back to you.")]
    [InlineData("On it — imp-2 is measuring the pricing page.")]
    public void OpeningWithAReceipt_IsAFault(string body)
    {
        Assert_Faults(body, OwnerMessageFaults.OpensWithAReceipt);
    }

    /// <summary>The same words further down are where the contract actually puts them.</summary>
    [Fact]
    public void AReceiptOnLineTwo_IsFine()
    {
        Assert_Clean("No card, like the old app.\nI'll write it up as ruled and brief the code.");
    }

    /// <summary>"Noted" inside a sentence is a word, not a receipt.</summary>
    [Fact]
    public void TheWordNotedMidSentence_IsNotAReceipt()
    {
        Assert_Clean("The risk you noted on 03/09 is now recorded on the ticket instead of living in a chat.");
    }

    [Theory]
    [InlineData("The fix is in /Users/nvene/Visual Studio/AIOrchestrator/src/engine.cs")]
    [InlineData("at Orchestrator.Run(Program.cs:42)")]
    [InlineData("PlanLedger_Parser was returning null for that row.")]
    public void CodeInAnOwnerMessage_IsAFault(string body)
    {
        Assert_Faults(body, OwnerMessageFaults.CarriesCode);
    }

    /// <summary>A date is not a path, and neither is a fraction. These broke a naive separator count.</summary>
    [Theory]
    [InlineData("Done on 07/09 — the trial now grants the plan being bought.")]
    [InlineData("Two of the five launch tickets are done, so 2/5 overall.")]
    public void DatesAndFractions_AreNotPaths(string body)
    {
        Assert_Clean(body);
    }

    [Fact]
    public void OverTheCharacterCeiling_IsAFault()
    {
        Assert_Faults(new string('a', OwnerMessage_Contract.MAXIMUM_CHARACTERS + 1), OwnerMessageFaults.TooLong);
    }

    [Fact]
    public void OverTheLineCeiling_IsAFault()
    {
        var body = string.Join("\n", Enumerable.Range(1, OwnerMessage_Contract.MAXIMUM_LINES + 1).Select(n => $"line {n}"));

        Assert_Faults(body, OwnerMessageFaults.TooLong);
    }

    [Fact]
    public void BlankLines_DoNotCountTowardTheCeiling()
    {
        Assert_Clean("one\n\ntwo\n\nthree\n\nfour");
    }

    // ── The coaching ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every fault must say what is wrong AND why it costs the owner something. Coaching that only
    /// names a rule teaches the session to satisfy the checker.
    /// </summary>
    [Theory]
    [InlineData(OwnerMessageFaults.TwoQuestions)]
    [InlineData(OwnerMessageFaults.ProseAfterTheQuestion)]
    [InlineData(OwnerMessageFaults.OpensWithAReceipt)]
    [InlineData(OwnerMessageFaults.CarriesCode)]
    [InlineData(OwnerMessageFaults.TooLong)]
    public void EveryFault_IsExplainedInFull(OwnerMessageFaults fault)
    {
        var described = OwnerMessage_Contract.Describe(fault);

        Assert.False(string.IsNullOrWhiteSpace(described));
        Assert.True(described.Length > 60, $"too terse to act on: {described}");
    }

    /// <summary>One entry can break the contract more than one way, and must be told about all of it.</summary>
    [Fact]
    public void SeveralFaultsInOneEntry_AreAllReported()
    {
        var faults = OwnerMessage_Contract.Check(
            "Noted — checking now.\nQUESTION: Merge?\nQUESTION: And 267?\nstill working on 274.");

        Assert.Contains(OwnerMessageFaults.OpensWithAReceipt, faults);
        Assert.Contains(OwnerMessageFaults.TwoQuestions, faults);
        Assert.Contains(OwnerMessageFaults.ProseAfterTheQuestion, faults);
    }

    /// <summary>
    /// ONE CEILING, and this test is the thing that keeps it one. The two constants were literals in
    /// two files and had ALREADY drifted — this contract said six lines while Brevity_Policy said
    /// five — so an entry of exactly six was coached as too long by the surface that measured what
    /// was mirrored and passed as fine by the surface that checked it beforehand. The supervisor
    /// could obey whichever it happened to be told.
    ///
    /// Asserted as an IDENTITY rather than by value, so nobody can "fix" a future disagreement by
    /// editing the number here: there is one number, and this says so.
    /// </summary>
    [Fact]
    public void TheCeilingIsBrevityPolicys_NotACopyOfIt()
    {
        Assert.Equal(Brevity_Policy.MAX_LINES, OwnerMessage_Contract.MAXIMUM_LINES);
        Assert.Equal(Brevity_Policy.MAX_CHARACTERS, OwnerMessage_Contract.MAXIMUM_CHARACTERS);

        // And the owner's numbers, so a change to BOTH is still visible as a change.
        Assert.Equal(5, OwnerMessage_Contract.MAXIMUM_LINES);
        Assert.Equal(600, OwnerMessage_Contract.MAXIMUM_CHARACTERS);
    }

    /// <summary>
    /// THE DRIFT, MADE CONCRETE: six lines is over the ceiling now and was not before. Five is not,
    /// so the boundary is asserted on both sides and cannot pass by calling everything too long.
    /// </summary>
    [Fact]
    public void SixLinesIsOverTheCeiling_FiveIsNot()
    {
        Assert.Contains(OwnerMessageFaults.TooLong, OwnerMessage_Contract.Check("a\nb\nc\nd\ne\nf"));
        Assert.DoesNotContain(OwnerMessageFaults.TooLong, OwnerMessage_Contract.Check("a\nb\nc\nd\ne"));
    }

    /// <summary>
    /// A FIFTH OPTION IS COACHED, NOT REFUSED (owner, brief C/E2). The distinction is the point of
    /// the fault: too FEW options is refused by OwnerQuestion_Contract, because a question with one
    /// option is not a choice and sending it back costs nothing. Five options IS a choice — refusing
    /// it would lose the owner a decision they can make, so the question goes out numbered and the
    /// supervisor hears about it afterwards.
    ///
    /// Both sides: four options raise nothing, so the rule cannot pass by flagging every question.
    /// </summary>
    [Fact]
    public void AFifthOptionIsCoached_AndFourAreFine()
    {
        var five = "Pick one.\nQUESTION: which?\nOPTION: a\nOPTION: b\nOPTION: c\nOPTION: d\nOPTION: e";
        var four = "Pick one.\nQUESTION: which?\nOPTION: a\nOPTION: b\nOPTION: c\nOPTION: d";

        Assert.Contains(OwnerMessageFaults.TooManyOptions, OwnerMessage_Contract.Check(five));
        Assert.DoesNotContain(OwnerMessageFaults.TooManyOptions, OwnerMessage_Contract.Check(four));
    }

    /// <summary>
    /// The coaching line carries the NUMBER, like every other one here — "too many options" without
    /// saying how many is the rule again, which is what Brevity_Policy's own summary is about.
    /// </summary>
    [Fact]
    public void TheTooManyOptionsCoachingSaysHowMany()
    {
        Assert.Contains("4", OwnerMessage_Contract.Describe(OwnerMessageFaults.TooManyOptions));
    }

    /// <summary>
    /// A WELL-FORMED QUESTION IS NOT TOO LONG — the owner's ruling of 2026-09-10, and the defect it
    /// removes was total rather than occasional: a question owes the owner ROW, RISK, QUESTION, two
    /// to four OPTIONs and RECOMMEND, so the SHORTEST valid question is six marker lines. A counter
    /// that included them coached EVERY question ever asked as over a five-line ceiling — and brief C
    /// asks for "zero coaching entries about format".
    ///
    /// Six marker lines and one line of prose, which is a question with something said before it.
    /// </summary>
    [Fact]
    public void AWellFormedQuestionIsNotCoachedAsTooLong()
    {
        var entry = string.Join('\n',
        [
            "The perf branch is green and I would take it now.",
            $"{ChannelGrammar.ROW} FIN-D-277a",
            $"{ChannelGrammar.RISK} high",
            $"{ChannelGrammar.QUESTION} merge wf-perf now, or hold for your review?",
            $"{ChannelGrammar.OPTION} Merge it",
            $"{ChannelGrammar.OPTION} Hold",
            $"{ChannelGrammar.RECOMMEND} Hold — you asked to read every merge to master first.",
        ]);

        Assert.DoesNotContain(OwnerMessageFaults.TooLong, OwnerMessage_Contract.Check(entry));

        // And the reason is the count, not luck: one prose line among seven.
        Assert.Equal(1, Brevity_Policy.Count_Lines(entry));
    }

    /// <summary>
    /// PROSE IS STILL COUNTED, and the boundary is asserted on both sides so the rule cannot pass by
    /// counting nothing. Six sentences is still six sentences however many markers surround them.
    /// </summary>
    [Fact]
    public void ProseIsStillCounted_MarkersOrNot()
    {
        var sixSentencesAroundAQuestion = string.Join('\n',
        [
            "one", "two", "three", "four", "five", "six",
            $"{ChannelGrammar.QUESTION} which?",
            $"{ChannelGrammar.OPTION} a",
            $"{ChannelGrammar.OPTION} b",
        ]);

        Assert.Contains(OwnerMessageFaults.TooLong, OwnerMessage_Contract.Check(sixSentencesAroundAQuestion));

        var fiveSentences = string.Join('\n', ["one", "two", "three", "four", "five"]);

        Assert.DoesNotContain(OwnerMessageFaults.TooLong, OwnerMessage_Contract.Check(fiveSentences));
    }

    /// <summary>
    /// THE CHARACTER CEILING STILL COUNTS EVERYTHING. A wall of text is a wall whatever the marker at
    /// its left edge, and exempting markers from the LINE count must not quietly exempt them from the
    /// other ceiling — that would let a single 2,000-character `OPTION:` through.
    /// </summary>
    [Fact]
    public void ACharacterWallIsStillTooLongEvenBehindAMarker()
    {
        var wall = $"{ChannelGrammar.OPTION} " + new string('x', OwnerMessage_Contract.MAXIMUM_CHARACTERS + 1);

        Assert.Contains(OwnerMessageFaults.TooLong, OwnerMessage_Contract.Check(wall));
    }

    /// <summary>
    /// THE TWO SIDES AGREE BY CONSTRUCTION — the whole point of moving the rule into the grammar. The
    /// tool refuses before the write and this coaches after it; they were counting differently, which
    /// is the same split E3 exists to end, one layer above the marker words.
    ///
    /// Asserted through the shared predicate rather than by re-deriving a count here, because a
    /// second implementation in a test is a second thing to be wrong.
    /// </summary>
    [Fact]
    public void EveryGrammarMarkerIsStructureToTheCounter()
    {
        foreach (var marker in ChannelGrammar.All_Markers)
        {
            Assert.True(
                ChannelGrammar.Is_MarkerLine($"{marker} something"),
                $"'{marker}' is in the grammar but the prose counter does not recognise it as structure, "
                + "so an entry using it would be coached as too long.");

            Assert.Equal(0, Brevity_Policy.Count_Lines($"{marker} something"));
        }
    }
}
