using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Bridge.Decisions;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// THE SKILLS AND THE CODE MUST AGREE ABOUT THE NUMBERS, and nothing was checking that.
///
/// <para>
/// Brief E2's whole subject is one ceiling with one home. The audit that produced it found the
/// supervisor's protocol carrying 229 imperative rules with 14 pairs that cannot both be obeyed, and
/// the numbers were part of it: the skill said option labels were "≤30 chars" while the app had
/// measured 28 since 2026-08-24, and this class's own subject — the line ceiling — was 5 in one C#
/// file and 6 in another. A number in prose drifts from a number in code silently, because nothing
/// fails.
/// </para>
/// <para>
/// IT READS THE SKILL TEXT, which is blunt and is the right instrument here: the property is "these
/// two files agree about a number", which is a fact about the text. Same shape as
/// <c>EveryTopicButtonIsWiredTests</c>, and the same guard-on-the-guard — a harness that cannot find
/// the file it checks REFUSES to run rather than certifying agreement it never read (decision 20).
/// </para>
/// <para>
/// DELIVERY IS NOT THIS TEST'S BUSINESS. Editing `kit/skills/` is not delivery — the app build copies
/// the kit to its output and `KitAssets_Installer` installs from THERE at startup (decision 17), so
/// a green run here says the branch source agrees, and says nothing about what any installed copy
/// holds.
/// </para>
/// </summary>
public class TheSkillsQuoteTheAppsOwnNumbersTests
{
    /// <summary>
    /// The brevity ceiling, in the file that teaches it. Both numbers, because 600 was right while
    /// the line count was wrong — a partial agreement is the one that hides.
    /// </summary>
    [Fact]
    public void TheSupervisorSkillQuotesTheBrevityCeiling()
    {
        var protocol = Read_RoleProtocol("supervisor");

        // ANCHORED ON THE SENTENCE, NOT ON THE DIGITS. The first version of this test asserted the
        // bare numbers and was VACUOUS: "26" occurs 34 times in this file (every 2026 date), so a
        // guard on a two-digit substring passes whatever the constant says — proved by mutating the
        // code and watching the test stay green. The number has to be checked where it is CLAIMED.
        Assert.Contains($"{Brevity_Policy.MAX_LINES} is the hard ceiling", protocol);
        Assert.Contains($"{Brevity_Policy.MAX_CHARACTERS} characters", protocol);
    }

    /// <summary>
    /// The option label width and the option COUNT, which is the pair the owner ruled on: two to
    /// four, 28 characters. The skill said "≤30 chars" until 2026-09-10 — a number the app has never
    /// used, so a supervisor obeying the page it was given wrote labels the app then renumbered.
    /// </summary>
    [Fact]
    public void TheSupervisorSkillQuotesTheOptionLimits()
    {
        var protocol = Read_RoleProtocol("supervisor");

        Assert.Contains($"{OptionButtons_Layout.READABLE_LABEL_WIDTH} characters each", protocol);
        Assert.Contains($"2 to {OwnerQuestion_Contract.MAXIMUM_OPTIONS} options", protocol);

        // And the number it used to carry is gone, or the page would contradict itself in two
        // places at once.
        Assert.DoesNotContain("30 chars", protocol);
    }

    /// <summary>
    /// THE WORKED EXAMPLE THE APP WOULD HAVE COACHED. `noted` is in the contract's receipt-openers,
    /// so the page teaching "one line is enough: `noted — …`" was teaching the one opening the app
    /// flags. The rule stays (a receipt in place of content is chatter); the example goes.
    ///
    /// Anchored on the example's own shape — the marker plus the em dash — rather than on the word
    /// "noted", which the surrounding prose now legitimately names while explaining why it is wrong.
    /// </summary>
    [Fact]
    public void TheSupervisorSkillNoLongerOffersAReceiptAsItsWorkedExample()
    {
        Assert.DoesNotContain("`noted — imp", Read_RoleProtocol("supervisor"));
    }

    /// <summary>
    /// EVERYTHING THE SUPERVISOR WRITES IS PUSHED since C removed the filter, and the page has to say
    /// so — it described three pushable kinds (a question, an answer, `BLOCKED ON OWNER`) and held
    /// back the rest, which stopped being true on 2026-09-09. A supervisor reading the stale version
    /// would believe a progress note was held when it in fact rings the owner's phone.
    /// </summary>
    [Fact]
    public void TheSupervisorSkillSaysEverythingItWritesReachesThePhone()
    {
        var protocol = Read_RoleProtocol("supervisor");

        Assert.Contains("EVERYTHING YOU WRITE", protocol);
        Assert.Contains("was removed", protocol);
    }

    /// <summary>
    /// THE GUARD ON THE GUARD. Returns the text or FAILS — a content test that found no content
    /// passes by finding nothing, which is decision 20's harness.
    /// </summary>
    static string Read_RoleProtocol(string role)
    {
        var path = KitRepoFiles.Find_RoleProtocol(role);

        Assert.False(
            path == null,
            $"could not locate kit/skills/{role}/SKILL.md walking up from '{AppContext.BaseDirectory}' — "
            + "this harness reads the skill's SOURCE, so a missing file means it measured nothing.");

        return File.ReadAllText(path!);
    }
}
