using AIOrchestratorCoreLib.Bridge.Decisions;
using AIOrchestratorCoreLib.Configuration.GuardrailSettings;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

public class HighRiskClassifierTests
{
    /// <summary>
    /// Matching is a plain case-insensitive substring test, so a pattern written in the owner's
    /// config as lowercase "push" still fires on a sentence an agent wrote in any case.
    /// </summary>
    [Fact]
    public void MatchingIsCaseInsensitive_SoAConfigPatternFiresRegardlessOfHowTheAgentCapitalizedIt()
    {
        Assert.True(HighRisk_Classifier.Is_HighRisk("Ready to PUSH to main?", ["push"]));
    }

    /// <summary>
    /// It reads the QUESTION text, not the option labels — an option is usually a bare word like
    /// "yes" that carries no risk of its own, while the question is where the agent actually wrote
    /// "ready to push to main?". Classifying the label instead would mean guessing which of several
    /// innocuous-looking labels is the dangerous one, which is exactly the guess this class exists
    /// to remove.
    /// </summary>
    [Fact]
    public void ItClassifiesTheQuestionText_BecauseThatIsWhatAYesIsActuallyAnsweringNotTheLabel()
    {
        Assert.True(HighRisk_Classifier.Is_HighRisk("Ready to push to main?", ["push"]));
        Assert.False(HighRisk_Classifier.Is_HighRisk("Merge now or hold?", ["push"]));
    }

    /// <summary>
    /// An empty pattern list is the owner explicitly saying nothing is high risk, and that must be
    /// honored as false rather than silently substituted with the built-in defaults — the fallback
    /// to defaults belongs only to the config factory (a MISSING key), and applying it again here
    /// would make an explicit empty list impossible to ever express.
    /// </summary>
    [Fact]
    public void AnEmptyPatternList_MatchesNothing_BecauseTheOwnerIsAllowedToDeclareNothingHighRisk()
    {
        Assert.False(HighRisk_Classifier.Is_HighRisk("ready to push to main and deploy to production?", []));
    }

    /// <summary>
    /// The empty-list behaviour above is deliberately different from a MISSING config key: the
    /// factory turns a missing key into DEFAULT_HIGH_RISK_PATTERNS, which does classify "push".
    /// Pinning both sides shows the two are not the same input reaching the same code path.
    /// </summary>
    [Fact]
    public void AnEmptyList_DiffersFromAMissingKey_WhichTheFactoryTurnsIntoDefaults()
    {
        var fromMissingKey = GuardrailSettings_Factory.Create_Default().HighRiskPatterns;

        Assert.True(HighRisk_Classifier.Is_HighRisk("time to push", fromMissingKey));
        Assert.False(HighRisk_Classifier.Is_HighRisk("time to push", []));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullOrWhitespaceQuestionText_IsNeverHighRisk(string? questionText)
    {
        Assert.False(HighRisk_Classifier.Is_HighRisk(questionText, ["push"]));
    }

    /// <summary>
    /// A blank entry in the pattern list must be skipped, not matched — an empty or whitespace
    /// substring is contained in every string, so treating it as a real pattern would turn one typo
    /// in config.json (a stray blank line) into every question in the system requiring a typed code
    /// forever. This is the difference between a guard and a permanent code prompt.
    /// </summary>
    [Fact]
    public void BlankPatternEntries_AreSkipped_RatherThanMatchingEveryQuestion()
    {
        IReadOnlyList<string> patternsWithBlanks = ["", "  ", "deploy"];

        Assert.False(HighRisk_Classifier.Is_HighRisk("merge now or hold?", patternsWithBlanks));
        Assert.True(HighRisk_Classifier.Is_HighRisk("ready to deploy?", patternsWithBlanks));
    }

    [Fact]
    public void Find_MatchedPattern_OrNull_ReturnsTheMatchedPattern()
    {
        Assert.Equal("deploy", HighRisk_Classifier.Find_MatchedPattern_OrNull("ready to deploy now?", ["push", "deploy"]));
    }

    [Fact]
    public void Find_MatchedPattern_OrNull_ReturnsNull_WhenNothingMatched()
    {
        Assert.Null(HighRisk_Classifier.Find_MatchedPattern_OrNull("merge now or hold?", ["push", "deploy"]));
    }

    /// <summary>
    /// Every pattern the factory ships as a default must actually fire on a realistic question an
    /// agent would write — an entry in the default list that never matches anything would be a
    /// silent hole in the owner's out-of-the-box protection.
    /// </summary>
    [Theory]
    [InlineData("push", "git push origin main — ready?")]
    [InlineData("deploy", "ready to deploy this to the cluster?")]
    [InlineData("release", "cut a release now?")]
    [InlineData("publish", "publish the package to the registry?")]
    [InlineData("production", "should this go out to production?")]
    [InlineData("rm -rf", "run rm -rf on the old build folder?")]
    [InlineData("force-push", "do a force-push to fix the history?")]
    [InlineData("force push", "ok to force push this branch?")]
    [InlineData("--force", "run git push --force here?")]
    [InlineData("reset --hard", "should I git reset --hard to the last commit?")]
    [InlineData("drop table", "drop table sessions to clear it out?")]
    [InlineData("delete branch", "ok to delete branch stage/old-feature?")]
    public void EveryDefaultPattern_ClassifiesARealisticAgentQuestion(string pattern, string realisticQuestion)
    {
        Assert.Contains(pattern, GuardrailSettings_Factory.DEFAULT_HIGH_RISK_PATTERNS);
        Assert.True(HighRisk_Classifier.Is_HighRisk(realisticQuestion, GuardrailSettings_Factory.DEFAULT_HIGH_RISK_PATTERNS));
    }

    [Theory]
    // The four that locked a pure product question on 2026-09-07, and their honest twins.
    [InlineData("Ready to push the release branch to main?", "push", true)]
    [InlineData("I pushed it already — merge now?", "push", false)]
    [InlineData("The deployed engine rejects these keys; which tier gets them?", "deploy", false)]
    [InlineData("Deploy to production tonight?", "deploy", true)]
    [InlineData("Publishing was last week; which plan gets the row?", "publish", false)]
    // Punctuation-edged patterns keep working: their own edges are not letters, so nothing
    // adjacent to them can be "the same word".
    [InlineData("Shall I run rm -rf on the build folder?", "rm -rf", true)]
    [InlineData("Use --force on the push?", "--force", true)]
    [InlineData("Warm the cache first?", "rm ", false)]
    public void APatternMatchesWholeWords_NotSubstrings(string surface, string pattern, bool expected)
    {
        Assert.Equal(expected, HighRisk_Classifier.Is_HighRisk(surface, [pattern]));
        Assert.Equal(expected ? pattern : null, HighRisk_Classifier.Find_MatchedPattern_OrNull(surface, [pattern]));
    }

    [Fact]
    public void APatternIsStillALiteral_NeverARegexTheOwnerWrote()
    {
        // A config entry full of metacharacters matches itself and nothing else — it cannot become
        // a pattern that matches everything, and it cannot throw at parse time.
        Assert.True(HighRisk_Classifier.Is_HighRisk("run c++ .* now?", ["c++ .*"]));
        Assert.False(HighRisk_Classifier.Is_HighRisk("run anything at all?", ["c++ .*"]));
    }
}
