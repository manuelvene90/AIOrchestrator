using AIOrchestratorCoreLib.Bridge.Siblings;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.GeneralSupervision;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// A SIBLING IS A SPEND THE SOLO ASKS FOR, and the only place a solo learns when and how to ask is its
/// role protocol (spec 2026-09-23 §8.1, §8.2). The app refuses a malformed request with a reason, but it
/// cannot teach the TEST — width fans out, an independent review promotes, two jobs the owner steers
/// separately split — nor the outbox manners that keep two sessions from acknowledging each other for
/// ever. So this pins the sentences that carry those rules, quoted in the prose's own words, so an edit
/// that keeps a heading and drops the rule turns red (the <see cref="KitProseCarriesTheOwnersRulesTests"/>
/// anchoring).
///
/// <para>
/// Honest about its strength, as <see cref="SoloIsToldToFanOutTests"/> is: this proves the files SAY it.
/// Whether a session obeys is the app's to enforce where it can (the request validator, the tap) and
/// nobody's where it cannot.
/// </para>
/// </summary>
public class SoloIsToldAboutSiblingsTests
{
    const string SKILL = "kit/skills/solo/SKILL.md";
    const string SIBLINGS = "kit/skills/solo/reference/siblings.md";

    [Fact]
    public void TheThreeRoutes_AreKeptDistinct()
    {
        var solo = Read(SKILL);

        Assert.Contains("Width → fan out", solo);
        Assert.Contains("an independent review or a crew → promote", solo);
        Assert.Contains("steer separately", solo);
        Assert.Contains("asking for a SIBLING", solo);
    }

    [Fact]
    public void ASiblingIsOnlyOnTheOwnersWord()
    {
        Assert.Contains("Only on the owner's word", Read(SKILL));
    }

    [Fact]
    public void TheRecipe_NamesTheOutboxTheIndexAndTheRequestFields()
    {
        var solo = Read(SKILL);

        Assert.Contains("HANDOVER", solo);
        Assert.Contains("sibling-outbox.md", solo);
        Assert.Contains("\"action\":   \"spawn-sibling\"", solo);
        Assert.Contains("\"handover\": ", solo);
        Assert.Contains("sibling-$ARGUMENTS-<timestamp>.json", solo);
        Assert.Contains("Do not re-drop", solo);
    }

    /// <summary>
    /// NOBODY CREATES THE OUTBOX (review of f2a6b02, C1): the app only reads it, and
    /// <c>channel-append.sh</c> refuses a channel that does not exist, so the recipe's HANDOVER and a
    /// child's first ASK both died on first use. Both places that teach an append teach the create step.
    /// <c>kit/self-write-suppression-check.sh</c> runs this exact line and then the real helper.
    /// </summary>
    [Fact]
    public void TheOutboxIsCreatedBeforeTheFirstAppend_InBothPlaces()
    {
        const string TOUCH = "touch \"$ORCH/sibling-outbox.md\"";

        Assert.Contains(TOUCH, Read(SKILL));
        Assert.Contains(TOUCH, Read(SIBLINGS));
    }

    /// <summary>
    /// THE ANSWERS A SESSION WILL ACTUALLY SEE (review M2, M3): a malformed file is answered by the
    /// reader's <c>request REJECTED</c>, not a <c>sibling REFUSED</c>; a refusal re-found at the tap ends
    /// with <see cref="SiblingNotice_Wording.REFUSED_AT_THE_TAP"/>; and the helper prints a bare number.
    /// </summary>
    [Fact]
    public void TheRefusalShapes_AreTheOnesTheAppWrites()
    {
        var solo = Read(SKILL);

        Assert.Contains("**`request REJECTED`**", solo);
        Assert.Contains("Fix it and drop a new file (same action string).", solo);
        Assert.Contains(SiblingNotice_Wording.NOTHING_CHANGED.TrimEnd('.'), Flatten(solo));
        Assert.Contains(SiblingNotice_Wording.REFUSED_AT_THE_TAP, Flatten(solo));
        Assert.DoesNotContain("Note the `[n]` the helper prints", solo);
        Assert.Contains("before your first outbox entry after the HANDOVER", Flatten(solo));
    }

    /// <summary>
    /// RULING F (pre-flight, 2026-09-23): <c>SiblingName_Rules</c> requires 2-4 words after the code, so
    /// the spec's own example <c>'AI-Orch · limits'</c> would be REFUSED. A skill that taught it would cost
    /// every first request a re-drop.
    /// </summary>
    [Fact]
    public void TheNameRule_IsTheValidatorsRule_AndTheExampleIsLegal()
    {
        var solo = Read(SKILL);

        // The numbers are the validator's own, so a change to either side turns this red.
        Assert.Contains($"{SiblingName_Rules.MIN_WORDS}-{SiblingName_Rules.MAX_WORDS} words after the code", solo);
        Assert.Contains($"1-{SiblingName_Rules.CODE_MAX_CHARS} characters", solo);
        Assert.Contains($"at most {SiblingName_Rules.NAME_MAX_CHARS}", solo);
        Assert.Contains($"at most {OrchestrationRequests_Reader.SIBLING_JOB_MAX_CHARS} characters", solo);
        Assert.DoesNotContain("\"AI-Orch · limits\"", solo);
        Assert.Contains("\"name\":     \"AI-Orch · limits rework\"", solo);
        Assert.Null(SiblingName_Rules.Describe_Refusal_OrNull("AI-Orch · limits rework"));
    }

    [Fact]
    public void TheBootStep_PointsAtSiblingsMd_WhenTheListExists()
    {
        var solo = Read(SKILL);

        Assert.Contains("$ORCH/.siblings", solo);
        Assert.Contains("reference/siblings.md", solo);
    }

    [Fact]
    public void TheRelayRule_KeepsTheTrace()
    {
        var siblings = Read(SIBLINGS);

        Assert.Contains("RELAY owner#", siblings);
        Assert.Contains("via <topic> #n", siblings);
    }

    [Fact]
    public void ASiblingBlock_IsAMachineBlock()
    {
        Assert.Contains("- [!] … (waiting on sibling", Read(SIBLINGS));
    }

    [Fact]
    public void NeverAcknowledgeAnAcknowledgement()
    {
        Assert.Contains("never acknowledge an acknowledgement", Read(SIBLINGS));
    }

    /// <summary>
    /// O4 (§8.5): the hold is per topic — <c>QuestionHoldIsPerTopicTests</c> pins the engine half — so the
    /// one overlap left is two siblings asking the owner the SAME decision. Only the prose can stop that.
    /// </summary>
    [Fact]
    public void ADecisionAffectingBothJobs_IsAskedOnce()
    {
        var siblings = Read(SIBLINGS);

        Assert.Contains("One question at a time is per topic.", siblings);
        Assert.Contains("asked **once**, by the sibling whose job it blocks", siblings);
    }

    [Fact]
    public void AnOutboxEntry_HasThePhoneBudget()
    {
        Assert.Contains("600 characters", Read(SIBLINGS));
    }

    /// <summary>
    /// DECISION 13 FOR A READER: the app compacts an outbox above 90 entries and keeps 45, so a sibling
    /// more than 45 entries behind finds its last-seen index gone from the live file. The prose must send it
    /// to the archive, or it silently skips what it never read (Task 10 review carry, 2026-09-24).
    /// </summary>
    [Fact]
    public void AReaderFarBehind_IsSentToTheArchive()
    {
        var siblings = Read(SIBLINGS);

        Assert.Contains(Path.GetFileName(Channel_Compactor.Build_ArchiveFilePath("sibling-outbox.md")), siblings);
        Assert.Contains($"above {Channel_Compactor.COMPACT_ABOVE_ENTRIES} entries", siblings);
        Assert.Contains($"the last {Channel_Compactor.KEEP_RECENT_ENTRIES}", siblings);
        Assert.Contains("A wake that finds nothing new means nothing", siblings);
    }

    /// <summary>
    /// RULING S4 (Task 13 fix round): a print solo's reply to its siblings alone is filed <c>[agent]</c> and
    /// never texted. The session must know that, or it re-sends the "missing" answer to the owner.
    /// </summary>
    [Fact]
    public void AReplyToSiblingsAlone_IsFiledNotTexted()
    {
        var siblings = Read(SIBLINGS);

        Assert.Contains("`[agent]`", siblings);
        Assert.Contains("never texted", siblings);
    }

    [Fact]
    public void TheGeneralSupervisor_NeverStartsASibling()
    {
        var general = Read("kit/skills/general-supervisor/SKILL.md");

        Assert.Contains("never start a sibling", general);
        Assert.Contains("🔗", general);
    }

    /// <summary>Prose wraps at ~100 columns; a quoted sentence may cross a line and its indent.</summary>
    static string Flatten(string text)
    {
        return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
    }

    /// <summary>Refuses rather than passing about prose it never read (decision 20).</summary>
    static string Read(string relative)
    {
        return KitRepoFiles.Find(relative.Replace('/', Path.DirectorySeparatorChar)) is string path && File.Exists(path)
            ? File.ReadAllText(path)
            : throw new Exception($"{relative} was not found walking up from {AppContext.BaseDirectory} — REFUSING to assert about a file this test never read.");
    }
}
