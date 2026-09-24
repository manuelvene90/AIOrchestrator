using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// PINS THE FIVE PARAGRAPHS TASK 10 OF THE FORK-MERGE PLAN WAS SENT TO VERIFY. The fork reorganised
/// master's role COMMANDS (<c>kit/commands/*.md</c>) into plugin SKILLS
/// (<c>kit/skills/&lt;role&gt;/SKILL.md</c>); git's rename detection carried most of master's later
/// owner-directive prose across the reorganisation for free, and one block (solo's ONE OPEN QUESTION
/// AT A TIME) was re-inserted by hand during the merge (see the fork-merge report, Task 10 row and
/// Task 2 Step 3). Nothing here checked that the carry actually held — a rename is a good bet, not a
/// guarantee, and the merge ledger itself flagged these five as "VERIFY, DO NOT RE-ADD".
///
/// Each case quotes the paragraph's OWN wording, not a summary of it, so a future edit that keeps the
/// heading but drops the sentence still fails here — the same anchoring
/// <see cref="TheSkillsQuoteTheAppsOwnNumbersTests"/> uses, for the same reason (a title surviving is
/// not the rule surviving).
///
/// Uses <see cref="KitRepoFiles.Find_RoleProtocol"/>, the one shared walk-up from the test binary's
/// own output folder (decision 20: a harness that cannot find what it tests must refuse to run,
/// never silently pass) — a missing file throws rather than letting every case pass by finding
/// nothing.
/// </summary>
public class KitProseCarriesTheOwnersRulesTests
{
    [Theory]
    [InlineData("solo", "ONE OPEN QUESTION AT A TIME")]
    [InlineData("general-supervisor", "ONE OPEN QUESTION AT A TIME")]
    [InlineData("supervisor", "the APP HOLDS THE CHANNEL as well")]
    [InlineData("supervisor", "You may also have been RESUMED")]
    [InlineData("solo", "You may also have been RESUMED")]

    // THE BUTTONS UNDER A QUESTION ARE THE OWNER'S SETTING (plan 04 Task 11, entry [123]): the skills name both
    // and promise neither.
    [InlineData("supervisor", "\"❔ Explain the options\" and/or \"💬 Let's talk\", per the owner's settings")]
    [InlineData("solo", "\"❔ Explain the options\" and/or \"💬 Let's talk\", per the owner's settings")]
    public void TheSkill_CarriesTheOwnersRule(string role, string sentence)
    {
        Assert.Contains(sentence, Read_Protocol(role), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// "The app adds ONE button … Let's talk" was true of the fork's build and is not of a machine whose owner chose
    /// Explain, both, or none (<c>questions.appButtons</c>). A session told it will always find Let's talk would
    /// describe a button its owner's phone does not show.
    /// </summary>
    [Theory]
    [InlineData("supervisor")]
    [InlineData("solo")]
    public void NoSkill_PromisesASingleFixedAppButton(string role)
    {
        var protocol = Read_Protocol(role);

        Assert.DoesNotContain("adds ONE button", protocol, StringComparison.Ordinal);
        Assert.DoesNotContain("ONE button of its own", protocol, StringComparison.Ordinal);
    }

    static string Read_Protocol(string role)
    {
        var path = KitRepoFiles.Find_RoleProtocol(role)
            ?? throw new Exception($"kit/skills/{role}/SKILL.md was not found — REFUSING to pass about prose this test never read.");

        return File.ReadAllText(path);
    }
}
