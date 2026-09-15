using AIOrchestratorCoreLib.Bridge;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// A LINE LIFTED OUT OF A MESSAGE LEAVES A RECEIPT.
///
/// <para>
/// The mirror strips marker lines from a body on the way to the phone — the app turns them into
/// buttons and fields. The match is anchored at column 0 and runs on EVERY entry, not only on
/// questions, so a sentence that merely opens with a marker word is removed from what the owner
/// reads. Until 2026-09-15 that happened with nothing recording it anywhere: the channel file said
/// one thing, the phone showed another, and no surface could be asked which.
/// </para>
/// <para>
/// THESE TESTS PASS BOTH SIDES EXPLICITLY — the body, and what the extractor left of it — because
/// that is the describer's actual contract. It compares rather than re-deriving, which is the whole
/// of its correctness: the first version asked a predicate whether each line LOOKED like a marker,
/// and that predicate trims while the extractor anchors at column 0, so it reported an indented line
/// as lost when the owner had in fact received it.
/// </para>
/// </summary>
public class LiftedMarkersDescriberTests
{
    [Fact]
    public void Describe_OrNull_WhenNothingWasRemoved_SaysNothing()
    {
        const string body = "Il disco della macchina di staging è pieno.\nOgni primo deploy muore.";

        Assert.Null(LiftedMarkers_Describer.Describe_OrNull(body, body));
    }

    [Fact]
    public void Describe_OrNull_AnEmptyBody_SaysNothing()
    {
        Assert.Null(LiftedMarkers_Describer.Describe_OrNull(string.Empty, string.Empty));
    }

    /// <summary>
    /// A well-formed question loses six lines BY DESIGN, and the receipt still names them — the log
    /// is how a "the message arrived cut" report gets answered, and an entry where the stripping was
    /// correct has to be distinguishable from one where it was not.
    /// </summary>
    [Fact]
    public void Describe_OrNull_AWellFormedQuestion_NamesEveryLineItLost()
    {
        const string body =
            "Ho guardato il disco.\nROW: FIN-D-043\nRISK: low\nQUESTION: pulisco il disco?\n"
            + "OPTION: sì\nOPTION: no\nRECOMMEND: sì";

        var described = LiftedMarkers_Describer.Describe_OrNull(body, "Ho guardato il disco.");

        Assert.NotNull(described);
        Assert.Contains("6 marker line(s)", described);
        Assert.Contains("QUESTION: pulisco il disco?", described);
    }

    /// <summary>
    /// THE CASE THIS EXISTS FOR. Prose opening with a marker word, in an entry that asks nothing —
    /// this project's supervisors write about configuration defaults and ledger rows constantly. The
    /// owner sees the first line and never learns the second existed.
    /// </summary>
    [Theory]
    [InlineData("DEFAULT: Stripe non imposta nulla, quindi vince il suo default.")]
    [InlineData("ROW: quella che avevi chiesto ieri.")]
    [InlineData("RISK: basso, ma va detto.")]
    public void Describe_OrNull_ProseOpeningWithAMarkerWord_IsReported(string lifted)
    {
        var described = LiftedMarkers_Describer.Describe_OrNull($"Ecco il punto.\n{lifted}", "Ecco il punto.");

        Assert.NotNull(described);
        Assert.Contains("1 marker line(s)", described);
        Assert.Contains(lifted[..20], described);
    }

    /// <summary>
    /// THE FALSE ALARM THIS MUST NEVER PRODUCE. The extractor anchors at column 0, so an indented or
    /// bulleted line survives and reaches the owner. Reporting it as lost would be the false positive
    /// that teaches people to ignore the log — the exact failure `ChannelShape_Validator` records for
    /// its own first version, which flagged ordinary report headings.
    /// </summary>
    [Theory]
    [InlineData("  DEFAULT: indented, so the extractor leaves it")]
    [InlineData("- ROW: bulleted, so the extractor leaves it")]
    [InlineData("Come dicevo, DEFAULT: non a inizio riga")]
    public void Describe_OrNull_ALineTheExtractorKept_IsNotReported(string kept)
    {
        var body = $"Ecco il punto.\n{kept}";

        Assert.Null(LiftedMarkers_Describer.Describe_OrNull(body, body));
    }

    /// <summary>
    /// The extractor finishes with `Trim('\n')`, so leading and trailing blank lines disappear
    /// without any marker being involved. Those are not a loss and must not be reported.
    /// </summary>
    [Fact]
    public void Describe_OrNull_BlankLinesLostToTheOuterTrim_AreNotReported()
    {
        Assert.Null(LiftedMarkers_Describer.Describe_OrNull("\n\nEcco il punto.\n\n", "Ecco il punto."));
    }

    /// <summary>A long line is quoted short — a log line that wraps is a log line nobody reads.</summary>
    [Fact]
    public void Describe_OrNull_ALongLine_IsExcerpted()
    {
        var described = LiftedMarkers_Describer.Describe_OrNull("QUESTION: " + new string('x', 400), string.Empty);

        Assert.NotNull(described);
        Assert.Contains("…", described);
        Assert.True(described.Length < 200, $"the receipt was {described.Length} characters: {described}");
    }
}
