using AIOrchestratorCoreLib.Planning;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning;

/// <summary>
/// THE LAST STEP ON THE OWNER'S DIRECTIVE PATH, and until now the only one with no guarantee.
///
/// `/progress` and `/tasks` build the ledger and then, when the Italian layer is on, hand the WHOLE
/// message to a `claude -p` subprocess. Nothing checked that what came back had the same rows, in the
/// same order, carrying the same markers — so "nothing hidden, no cap, no truncation" was proven at
/// the formatter and unproven at the phone. Rule 11 makes the Italian layer persisted and the owner's
/// normal mode, so that is the production path, not an edge case.
///
/// The branch did not introduce the mechanism; it changed the EXPOSURE. /progress used to send a
/// capped short version and now sends every row, which is the directive's whole point.
///
/// A LEDGER IS A DRAWING — the same reason `MonospaceBlocks_Formatter` lifts fenced blocks out before
/// translation. This verifies the drawing survived instead of extracting it, and the shape it checks
/// is exactly the shape the owner asked for: one line per ledger line, its marker, in order.
/// </summary>
public class LedgerTranslationVerifierTests
{
    const string ENGLISH =
        "handoff backlog · 2/7 done (28%)\n" +
        "[>] fix R1 — clear the awaiting-answer flag\n" +
        "[ ] rebase onto master\n" +
        "[x] audit R2–R8\n" +
        "[-] rewrite the mirror loop — superseded";

    /// <summary>Prose changes, structure does not. This is the whole point: the translation is fine.</summary>
    [Fact]
    public void AFaithfulTranslationIsAccepted()
    {
        var italian =
            "arretrato di consegna · 2/7 fatti (28%)\n" +
            "[>] correggi R1 — azzera il flag di risposta attesa\n" +
            "[ ] rebase su master\n" +
            "[x] verifica R2–R8\n" +
            "[-] riscrivi il mirror loop — superato";

        Assert.Null(LedgerTranslation_Verifier.Describe_ShapeChange_OrNull(ENGLISH, italian));
    }

    /// <summary>
    /// The translator returns the ORIGINAL text on failure or timeout, by contract. That must read as
    /// preserved, or every failed translation would also trip the fallback and log a shape problem
    /// that never happened.
    /// </summary>
    [Fact]
    public void TheUntranslatedOriginalIsAccepted()
    {
        Assert.Null(LedgerTranslation_Verifier.Describe_ShapeChange_OrNull(ENGLISH, ENGLISH));
    }

    /// <summary>
    /// A ROW LOST. This is the defect the whole directive was issued about, arriving one step after
    /// the renderer that was fixed to prevent it — and it is exactly what a summarising model does to
    /// a list it thinks is repetitive.
    /// </summary>
    [Fact]
    public void ADroppedRowIsRefused()
    {
        var missingTheDroppedLine =
            "arretrato di consegna · 2/7 fatti (28%)\n" +
            "[>] correggi R1\n" +
            "[ ] rebase su master\n" +
            "[x] verifica R2–R8";

        Assert.NotNull(LedgerTranslation_Verifier.Describe_ShapeChange_OrNull(ENGLISH, missingTheDroppedLine));
    }

    /// <summary>A row INVENTED, or one wrapped onto two lines, is refused for the same reason.</summary>
    [Fact]
    public void AnExtraRowIsRefused()
    {
        Assert.NotNull(LedgerTranslation_Verifier.Describe_ShapeChange_OrNull(ENGLISH, ENGLISH + "\n[ ] una riga in più"));
    }

    /// <summary>
    /// A MARKER TRANSLATED OR TIDIED. `[x]` reading `[fatto]`, or `[ ]` losing its space, is the
    /// ledger's vocabulary going out in two languages — and the marker is the one part of the row the
    /// owner reads as a symbol rather than as words.
    /// </summary>
    [Theory]
    [InlineData("[fatto] verifica R2–R8")]
    [InlineData("[X] verifica R2–R8")]
    [InlineData("[] verifica R2–R8")]
    [InlineData("verifica R2–R8")]
    public void AChangedMarkerIsRefused(string mangledRow)
    {
        var lines = ENGLISH.Split('\n');
        lines[3] = mangledRow;

        Assert.NotNull(LedgerTranslation_Verifier.Describe_ShapeChange_OrNull(ENGLISH, string.Join('\n', lines)));
    }

    /// <summary>
    /// ROWS REORDERED — a model grouping the finished ones together. The count still matches, which is
    /// why the check is per-INDEX and not a tally: a set comparison would call this preserved.
    /// </summary>
    [Fact]
    public void AReorderedLedgerIsRefused()
    {
        var reordered =
            "handoff backlog · 2/7 done (28%)\n" +
            "[x] audit R2–R8\n" +
            "[>] fix R1\n" +
            "[ ] rebase onto master\n" +
            "[-] rewrite the mirror loop";

        Assert.NotNull(LedgerTranslation_Verifier.Describe_ShapeChange_OrNull(ENGLISH, reordered));
    }

    /// <summary>
    /// The HEADING is prose and may change freely — it carries no marker, and "no marker" is itself a
    /// shape that must be preserved. A heading that comes back wearing one means the model has read
    /// the list and rewritten it as another row.
    /// </summary>
    [Fact]
    public void TheHeadingMayChangeButMayNotBecomeARow()
    {
        var lines = ENGLISH.Split('\n');
        lines[0] = "una intestazione completamente diversa";

        Assert.Null(LedgerTranslation_Verifier.Describe_ShapeChange_OrNull(ENGLISH, string.Join('\n', lines)));

        lines[0] = "[>] una intestazione completamente diversa";

        Assert.NotNull(LedgerTranslation_Verifier.Describe_ShapeChange_OrNull(ENGLISH, string.Join('\n', lines)));
    }

    /// <summary>
    /// An empty answer is refused rather than sent. A subprocess that returns nothing is the one case
    /// where the owner would be shown a blank message in place of their ledger.
    ///
    /// AGAINST A SINGLE-LINE ORIGINAL, and that is the whole correction. The five-line fixture this
    /// used reached green by TWO routes — the emptiness guard and, independently, the line count
    /// (1 != 5) — so it pinned neither, and deleting the guard left it green. Item 20: never assert on
    /// a state with two routes to it.
    ///
    /// The single-line shape is not hypothetical; it is most of what this path really carries.
    /// "no open orchestrations", "no orchestration is bound to this topic" and the /tasks
    /// out-of-topic hint are all one line. Without the guard, `Describe_ShapeChange_OrNull("no open
    /// orchestrations", "")` finds one line each and no marker on either, answers "unchanged", and the
    /// empty string goes to the chunker — which yields ZERO chunks for empty input, so the send loop
    /// iterates nothing and the owner receives NOTHING. The guard is load-bearing on exactly the shape
    /// the old fixture could not see.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyTranslationIsRefused(string translated)
    {
        Assert.NotNull(LedgerTranslation_Verifier.Describe_ShapeChange_OrNull("no open orchestrations", translated));

        // The multi-line case too, so the guard is pinned without losing the coverage it had.
        Assert.NotNull(LedgerTranslation_Verifier.Describe_ShapeChange_OrNull(ENGLISH, translated));
    }

    /// <summary>
    /// Trailing whitespace is not a shape change. The translator trims its own output and a
    /// subprocess may append a newline; comparing those as structure would fall back to English on
    /// every single call, which is a silent loss of the Italian layer rather than a guard.
    /// </summary>
    [Fact]
    public void TrailingWhitespaceIsNotAShapeChange()
    {
        Assert.Null(LedgerTranslation_Verifier.Describe_ShapeChange_OrNull(ENGLISH, ENGLISH + "\n"));
        Assert.Null(LedgerTranslation_Verifier.Describe_ShapeChange_OrNull(ENGLISH + "\n", ENGLISH));
    }

    /// <summary>
    /// /tasks IS TRANSLATED TOO, and it renders a different vocabulary — `  x row`, not `[x] row`.
    /// A verifier that only knew /progress's markers would degrade to a line count on that command
    /// while looking like it covered both, which is the shape of guard this repo keeps finding.
    /// </summary>
    [Fact]
    public void TheFullFormVocabularyIsCheckedAsWell()
    {
        const string english = "  x the delivered thing\n  · the open one\n  ! the blocked one";

        Assert.Null(LedgerTranslation_Verifier.Describe_ShapeChange_OrNull(english, "  x la cosa consegnata\n  · quella aperta\n  ! quella bloccata"));

        // The marker rewritten, with the row and the count intact.
        Assert.NotNull(LedgerTranslation_Verifier.Describe_ShapeChange_OrNull(english, "  x la cosa consegnata\n  - quella aperta\n  ! quella bloccata"));

        // And the indent dropped, which is the same row stripped of its state.
        Assert.NotNull(LedgerTranslation_Verifier.Describe_ShapeChange_OrNull(english, "  x la cosa consegnata\nquella aperta\n  ! quella bloccata"));
    }

    /// <summary>
    /// THE ONLY CASE COUPLED TO THE RENDERERS, and the gap the verifier's own comment admitted: "if a
    /// renderer's prefix ever changes without this changing with it, the check quietly weakens to the
    /// line count instead of failing". Every other fixture here is hand-typed, so nothing detected
    /// that drift — the component's contract IS these two renderers' output, and none of it was
    /// tested against them.
    ///
    /// Change `Describe_FullFormPrefix` to emit `[x]` instead of `x` and `Read_Marker_OrEmpty` stops
    /// recognising a /tasks row: both sides read as prose, every marker comparison trivially matches,
    /// and the production path silently falls back to a bare line count while all the hand-typed
    /// cases stay green. That is item 20 again — a guard certifying the absence of the thing it
    /// stopped testing.
    ///
    /// The stripped row is what makes it bite: it can only be REFUSED if the verifier recognises the
    /// vocabulary the formatter actually emits. Built through the real parser and the real formatters
    /// in the real message shape, interior blank line included, so no fixture stands between them.
    ///
    /// WHICH LINES ARE ROWS IS DECIDED BY THE FIXTURE, NOT BY THE COMPONENT, and the first version of
    /// this test got that exactly backwards. It skipped a line when
    /// `Describe_ShapeChange_OrNull(...)` returned null for it — asking the component under test
    /// whether to test the component. Apply the very mutation this docstring names and the reader
    /// stops recognising `/tasks` rows, so the inner call returns null for every one, every iteration
    /// skips, and the message contributes ZERO assertions while the test passes green. Make the reader
    /// return empty unconditionally and the whole loop asserts nothing at all, with the verifier
    /// entirely dead.
    ///
    /// I wrote the warning in the paragraph above and then wrote the guard it warns about, in the same
    /// file. The row indexes come from the fixture now — the test built the message and knows where
    /// the counts line and the blank separator are — and the count of rows actually exercised is
    /// ASSERTED, which is the clause that makes the failure impossible to repeat silently.
    /// </summary>
    [Fact]
    public void TheMarkersItRecognisesAreTheOnesTheFormattersEmit()
    {
        var progress = PlanLedger_Parser.Parse_OrNull(string.Join('\n', new[]
        {
            "- [>] the running one",
            "- [ ] the open one",
            "- [x] the delivered thing",
            "- [-] the superseded thing",
            "- [!] the blocked one",
        }))!;

        const int LEDGER_ROWS = 5;

        var counts = PlanProgress_Formatter.Describe_Counts(progress);

        // The first ROW index is fixture knowledge: /progress is the counts line then the rows,
        // /tasks is the counts line, a blank separator, then the rows. Stated here so the loop below
        // never has to ask the component what it is looking at.
        (string Message, int FirstRowIndex)[] realMessages =
        [
            ($"{counts}\n{PlanProgress_Formatter.Describe_Ledger(progress)}", 1),
            ($"{counts}\n\n{PlanProgress_Formatter.Describe_EveryLine(progress)}", 2),
        ];

        foreach (var (message, firstRowIndex) in realMessages)
        {
            Assert.Null(LedgerTranslation_Verifier.Describe_ShapeChange_OrNull(message, message));

            var lines = message.Split('\n');

            // The fixture is what this test thinks it is — asserted, so a renderer that changed its
            // preamble fails HERE rather than quietly shifting which lines get stripped.
            Assert.Equal(LEDGER_ROWS, lines.Length - firstRowIndex);

            var rowsExercised = 0;

            // EVERY row, not just the last. Stripping one exercises one marker, and the single remap
            // `Describe_FullFormPrefix` performs — the open-task prefix — is not necessarily the one
            // you happen to land on.
            for (var index = firstRowIndex; index < lines.Length; index++)
            {
                var stripped = (string[])lines.Clone();
                stripped[index] = stripped[index][4..];

                Assert.NotNull(LedgerTranslation_Verifier.Describe_ShapeChange_OrNull(message, string.Join('\n', stripped)));
                rowsExercised++;
            }

            // THE CLAUSE THAT MAKES IT UNREPEATABLE: a loop that asserted nothing is a failure, not a
            // pass. Without this, any future skip condition can empty the loop and stay green.
            Assert.Equal(LEDGER_ROWS, rowsExercised);
        }
    }

    /// <summary>
    /// A PLACEHOLDER ROW — `- [ ]` with no task text — renders as four characters ending in the space
    /// before the empty text, and a model returning the visually identical three-character line was a
    /// shape change. That pinned the orchestration to English for every /progress and /tasks from then
    /// on, PERSISTENTLY, because the trigger is a property of the ledger FILE rather than of one
    /// sample — and whole-string trimming never reaches it unless it is the last line.
    ///
    /// It is a supported state: the parser accepts it deliberately, counts it in the denominator, and
    /// both formatters render it. A supported state that silently disables a persisted owner setting
    /// is not garbage-in.
    /// </summary>
    [Fact]
    public void APlaceholderRowSurvivesLosingItsTrailingSpace()
    {
        var progress = PlanLedger_Parser.Parse_OrNull("- [ ]\n- [x] the delivered thing")!;

        foreach (var rendered in new[] { PlanProgress_Formatter.Describe_Ledger(progress), PlanProgress_Formatter.Describe_EveryLine(progress) })
        {
            var lines = rendered.Split('\n');

            Assert.Equal(4, lines[0].Length);
            Assert.EndsWith(" ", lines[0]);

            lines[0] = lines[0].TrimEnd();

            Assert.Null(LedgerTranslation_Verifier.Describe_ShapeChange_OrNull(rendered, string.Join('\n', lines)));
        }
    }

    /// <summary>
    /// AND IT STILL CATCHES A REWRITTEN MARKER on such a row — the tolerance is for the trailing
    /// space alone, not for the marker body. Asserted beside the case above so the fix cannot pass by
    /// having stopped checking placeholder rows altogether.
    /// </summary>
    [Fact]
    public void APlaceholderRowStillHasItsMarkerChecked()
    {
        var rendered = PlanProgress_Formatter.Describe_Ledger(PlanLedger_Parser.Parse_OrNull("- [ ]\n- [x] the delivered thing")!);
        var lines = rendered.Split('\n');

        lines[0] = "[x]";

        Assert.NotNull(LedgerTranslation_Verifier.Describe_ShapeChange_OrNull(rendered, string.Join('\n', lines)));
    }

    /// <summary>
    /// THE DIAGNOSTIC TELLS A LOST ROW FROM A REFLOWED SEPARATOR. Every topic-scope /progress carries
    /// an interior blank line by construction, so a model that merely closes the gap was reported as
    /// "5 lines came back as 4" — indistinguishable from a vanished ledger row, in the one line that
    /// exists to diagnose exactly that difference.
    /// </summary>
    [Fact]
    public void ALostBlankSeparatorIsNotReportedAsALostRow()
    {
        const string withSeparator = "handoff backlog · 2/7 done (28%)\n\n[>] fix R1\n[x] audit R2–R8";

        var reflowed = LedgerTranslation_Verifier.Describe_ShapeChange_OrNull(
            withSeparator, "arretrato · 2/7 fatti (28%)\n[>] correggi R1\n[x] verifica R2–R8");

        Assert.Equal("4 lines came back as 3 (blank separators 1 → 0)", reflowed);

        // A genuinely lost ROW keeps its separator, and says nothing about blanks.
        Assert.Equal(
            "4 lines came back as 3",
            LedgerTranslation_Verifier.Describe_ShapeChange_OrNull(withSeparator, "arretrato · 2/7 fatti (28%)\n\n[>] correggi R1"));
    }

    /// <summary>
    /// A ledger with no rows at all — the "no task ledger yet" answer — is prose end to end and must
    /// still translate. A verifier that refused everything without markers would silence it.
    /// </summary>
    [Fact]
    public void APlainProseAnswerStillTranslates()
    {
        Assert.Null(LedgerTranslation_Verifier.Describe_ShapeChange_OrNull(
            "CRM: no task ledger yet — the supervisor writes PLAN.md once you approve a direction",
            "CRM: nessun elenco attività — il supervisore scrive PLAN.md quando approvi una direzione"));
    }

    /// <summary>
    /// THE DESCRIPTION IS THE DIAGNOSTIC, so it is asserted rather than merely non-null. Rule 15
    /// correctly keeps this off the owner's phone, which makes one log line the entire surface for
    /// the failure this component exists to detect — and "the shape changed" in a forty-row ledger
    /// is the silence rule 21 names, one level down.
    ///
    /// Both kinds, because they are the two things that can go wrong and a reader needs to know
    /// WHICH: a count, and a marker at a named line.
    /// </summary>
    [Fact]
    public void TheChangeIsNamedPreciselyEnoughToDiagnose()
    {
        Assert.Equal(
            "5 lines came back as 4",
            LedgerTranslation_Verifier.Describe_ShapeChange_OrNull(ENGLISH, string.Join('\n', ENGLISH.Split('\n')[..4])));

        var lines = ENGLISH.Split('\n');
        lines[3] = "[-] verifica R2–R8";

        // The marker BODY, without the trailing space it used to carry: a placeholder row ends at the
        // marker, so including the space made an empty row disagree with itself about what it is.
        Assert.Equal(
            "line 4: marker '[x]' came back as '[-]'",
            LedgerTranslation_Verifier.Describe_ShapeChange_OrNull(ENGLISH, string.Join('\n', lines)));

        Assert.Equal("the answer came back empty", LedgerTranslation_Verifier.Describe_ShapeChange_OrNull(ENGLISH, ""));
    }
}
