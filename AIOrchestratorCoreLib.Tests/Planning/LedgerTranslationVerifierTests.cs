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

        Assert.True(LedgerTranslation_Verifier.Is_ShapePreserved(ENGLISH, italian));
    }

    /// <summary>
    /// The translator returns the ORIGINAL text on failure or timeout, by contract. That must read as
    /// preserved, or every failed translation would also trip the fallback and log a shape problem
    /// that never happened.
    /// </summary>
    [Fact]
    public void TheUntranslatedOriginalIsAccepted()
    {
        Assert.True(LedgerTranslation_Verifier.Is_ShapePreserved(ENGLISH, ENGLISH));
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

        Assert.False(LedgerTranslation_Verifier.Is_ShapePreserved(ENGLISH, missingTheDroppedLine));
    }

    /// <summary>A row INVENTED, or one wrapped onto two lines, is refused for the same reason.</summary>
    [Fact]
    public void AnExtraRowIsRefused()
    {
        Assert.False(LedgerTranslation_Verifier.Is_ShapePreserved(ENGLISH, ENGLISH + "\n[ ] una riga in più"));
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

        Assert.False(LedgerTranslation_Verifier.Is_ShapePreserved(ENGLISH, string.Join('\n', lines)));
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

        Assert.False(LedgerTranslation_Verifier.Is_ShapePreserved(ENGLISH, reordered));
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

        Assert.True(LedgerTranslation_Verifier.Is_ShapePreserved(ENGLISH, string.Join('\n', lines)));

        lines[0] = "[>] una intestazione completamente diversa";

        Assert.False(LedgerTranslation_Verifier.Is_ShapePreserved(ENGLISH, string.Join('\n', lines)));
    }

    /// <summary>
    /// An empty answer is refused rather than sent. A subprocess that returns nothing is the one case
    /// where the owner would be shown a blank message in place of their ledger.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyTranslationIsRefused(string translated)
    {
        Assert.False(LedgerTranslation_Verifier.Is_ShapePreserved(ENGLISH, translated));
    }

    /// <summary>
    /// Trailing whitespace is not a shape change. The translator trims its own output and a
    /// subprocess may append a newline; comparing those as structure would fall back to English on
    /// every single call, which is a silent loss of the Italian layer rather than a guard.
    /// </summary>
    [Fact]
    public void TrailingWhitespaceIsNotAShapeChange()
    {
        Assert.True(LedgerTranslation_Verifier.Is_ShapePreserved(ENGLISH, ENGLISH + "\n"));
        Assert.True(LedgerTranslation_Verifier.Is_ShapePreserved(ENGLISH + "\n", ENGLISH));
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

        Assert.True(LedgerTranslation_Verifier.Is_ShapePreserved(english, "  x la cosa consegnata\n  · quella aperta\n  ! quella bloccata"));

        // The marker rewritten, with the row and the count intact.
        Assert.False(LedgerTranslation_Verifier.Is_ShapePreserved(english, "  x la cosa consegnata\n  - quella aperta\n  ! quella bloccata"));

        // And the indent dropped, which is the same row stripped of its state.
        Assert.False(LedgerTranslation_Verifier.Is_ShapePreserved(english, "  x la cosa consegnata\nquella aperta\n  ! quella bloccata"));
    }

    /// <summary>
    /// A ledger with no rows at all — the "no task ledger yet" answer — is prose end to end and must
    /// still translate. A verifier that refused everything without markers would silence it.
    /// </summary>
    [Fact]
    public void APlainProseAnswerStillTranslates()
    {
        Assert.True(LedgerTranslation_Verifier.Is_ShapePreserved(
            "CRM: no task ledger yet — the supervisor writes PLAN.md once you approve a direction",
            "CRM: nessun elenco attività — il supervisore scrive PLAN.md quando approvi una direzione"));
    }
}
