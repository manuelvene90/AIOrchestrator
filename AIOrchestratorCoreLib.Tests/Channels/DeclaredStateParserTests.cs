using AIOrchestratorCoreLib.Channels;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// THE SESSION'S OWN SENTENCE ABOUT ITSELF, and the app's refusal to invent one.
///
/// <para>
/// PULSE's second field used to be inferred from the channel: "the session spoke last, so it is
/// waiting on you". On 2026-09-09 that read *"supervisor: waiting on you"* thirty minutes after the
/// supervisor wrote "Nothing more needed from you", and *"idle — waiting"* for two hours while it
/// was paused for a usage limit. So the session declares its state and the app repeats it.
/// </para>
/// </summary>
public class DeclaredStateParserTests
{
    [Fact]
    public void ADeclaredState_IsTakenVerbatim()
    {
        Assert.Equal(
            "waiting for imp-2's review, then I hand you the merge",
            DeclaredState_Parser.Find_OrNull("Two fixes landed.\nSTATE: waiting for imp-2's review, then I hand you the merge"));
    }

    /// <summary>
    /// AN OMITTED LINE IS A BLANK ROW, not a guess. That is the whole reason this returns null
    /// rather than a default sentence: every default would be a claim about a session's state made
    /// by something that cannot know it.
    /// </summary>
    [Theory]
    [InlineData("Two fixes landed and the suite is green.")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NoDeclaration_IsNull_RatherThanAnInventedState(string? body)
    {
        Assert.Null(DeclaredState_Parser.Find_OrNull(body));
    }

    /// <summary>
    /// AT THE START OF A LINE, like every marker in this vocabulary. A session explaining the
    /// protocol — "end every turn with a STATE: line" — is not declaring a state called "line".
    /// </summary>
    [Fact]
    public void TheMarkerMidSentence_IsNotADeclaration()
    {
        Assert.Null(DeclaredState_Parser.Find_OrNull("I will end every turn with a STATE: line from now on, as asked."));
    }

    [Fact]
    public void AMarkerWithNothingAfterIt_DeclaresNothing()
    {
        Assert.Null(DeclaredState_Parser.Find_OrNull("done for now\nSTATE:"));
        Assert.Null(DeclaredState_Parser.Find_OrNull("done for now\nSTATE:    "));
    }

    /// <summary>
    /// TWO DECLARATIONS MEAN THE SESSION CHANGED ITS MIND WHILE WRITING, and the later line is the
    /// one it meant. The opposite rule to the question directives' "first readable one wins", and
    /// deliberately so: there a repeated marker at the bottom must not silently override the one a
    /// human reads at the top, while here the bottom line IS the correction.
    /// </summary>
    [Fact]
    public void TheLastDeclarationInTheEntry_IsTheOneThatCounts()
    {
        Assert.Equal(
            "actually blocked on your browser pass",
            DeclaredState_Parser.Find_OrNull("STATE: running the suite\nthen, on reflection:\nSTATE: actually blocked on your browser pass"));
    }

    [Fact]
    public void ADeclarationLongerThanTheRow_IsCutRatherThanDropped()
    {
        var declared = new string('x', DeclaredState_Parser.MAX_LENGTH + 40);

        var found = DeclaredState_Parser.Find_OrNull($"STATE: {declared}");

        Assert.NotNull(found);
        Assert.EndsWith("…", found);
        Assert.True(found.Length <= DeclaredState_Parser.MAX_LENGTH + 1, $"the row budget was blown: {found.Length}");
    }

    /// <summary>
    /// The session writes it in the OWNER's language — it is addressed to them — while the label the
    /// app wraps it in stays English. Nothing here transforms the text.
    /// </summary>
    [Fact]
    public void TheOwnersLanguageIsCarriedThrough_Untouched()
    {
        Assert.Equal(
            "aspetto la revisione di imp-2, poi ti passo il merge",
            DeclaredState_Parser.Find_OrNull("STATE: aspetto la revisione di imp-2, poi ti passo il merge"));
    }
}
