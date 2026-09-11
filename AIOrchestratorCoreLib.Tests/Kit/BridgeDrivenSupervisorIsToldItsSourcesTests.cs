using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// THE RULES A BRIDGE-DRIVEN SUPERVISOR CANNOT WORK WITHOUT, pinned as text because text is what a
/// session is given. Stage 1c changed what wakes a supervisor — every channel it listens to, not the
/// owner's alone — and left four things the kit had to learn. They were written into
/// <c>reference/stream-runner.md</c> by 55c7f8f; nothing has ever asserted they are still there.
///
/// <para>
/// WHY THAT MATTERS MORE THAN THE USUAL DOC TEST. One of the four is a DELETION: the old "cat your
/// spokes at the end of every turn" patch, which was correct while a single source woke the session
/// and is now a whole turn spent re-reading files the bridge already quoted, ending in a second copy
/// of the same report. A deletion cannot be noticed by reading the file that no longer says it. This
/// is the only thing that would notice it coming back.
/// </para>
/// <para>
/// HONEST ABOUT ITS OWN STRENGTH, like <see cref="SoloIsToldToFanOutTests"/>: this proves the files
/// SAY it. Nothing in a test can make a model obey. That limit is the finding, not an oversight —
/// and the app enforces at the point of effect everywhere it can, which is why the addressing rule
/// has <c>TurnReply_Splitter</c> behind it and not just a paragraph.
/// </para>
/// </summary>
public class BridgeDrivenSupervisorIsToldItsSourcesTests
{
    /// <summary>
    /// (1) Woken by every channel, (2) several channels can ride one turn, and the prompt labels
    /// which is which. A supervisor that believes only the owner wakes it leaves every member
    /// awaiting review.
    /// </summary>
    [Fact]
    public void TheSupervisorIsToldEveryChannelWakesIt_AndThatOneTurnCanCarrySeveral()
    {
        var stream = Read("stream-runner.md");

        Assert.Contains("YOU ARE WOKEN BY EVERY CHANNEL YOU LISTEN TO", stream);
        Assert.Contains("A member writing in its spoke starts a turn for you", stream);
        Assert.Contains("One turn can carry several channels at once", stream);
        Assert.Contains("--- from imp-1 (channel.md) ---", stream);
    }

    /// <summary>
    /// (3) The addressing format, whole: the marker, what a block is, where unaddressed text goes,
    /// and the rule that a preamble becomes an entry of its own rather than being folded in. That
    /// last one is what a model does naturally, which is why it is named rather than implied.
    /// </summary>
    [Fact]
    public void TheAddressingFormatIsTaught_IncludingWhatHappensToTextNobodyAddressed()
    {
        var stream = Read("stream-runner.md");

        Assert.Contains("TO: <channel>", stream);
        Assert.Contains("first line the subject", stream);
        Assert.Contains("Text before the first `TO:` goes to the OWNER", stream);
        Assert.Contains("it becomes an entry OF ITS OWN", stream);

        // (4) And a verdict has to go where the app reads verdicts from, or the member it is about
        // stays awaiting review for ever and nudges the owner's phone every few minutes.
        Assert.Contains("ANSWER A MEMBER IN ITS OWN CHANNEL", stream);
    }

    /// <summary>
    /// THE PATCH IS GONE AND MUST STAY GONE — in the reference AND in the protocol beside it. The
    /// instruction is asserted absent by its own shape rather than by a phrase, so a reworded return
    /// ("re-read your spokes before you finish") is caught as readily as the original wording.
    /// </summary>
    [Fact]
    public void NoBridgeDrivenFile_TellsTheSupervisorToReReadItsSpokesAtTheEndOfATurn()
    {
        foreach (var file in new[] { "SKILL.md", "stream-runner.md", "print-runner.md" })
        {
            var text = Read(file);

            // The trigger it was conditioned on no longer exists, and the app's log line that named
            // it was deleted with it — a file still quoting it is describing a build nobody runs.
            Assert.DoesNotContain("woken by the OWNER channel only", text);
            Assert.DoesNotContain("THE LIMIT OF THIS MODE", text);

            foreach (var verb in new[] { "cat", "re-read", "read" })
            {
                foreach (var when in new[] { "at the end of a turn", "at the end of each turn", "at the end of every turn", "before you end your turn" })
                    Assert.DoesNotContain($"{verb} your spokes {when}", text, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Said as a prohibition in the one file that used to command it, so a session carrying the
        // old habit from anywhere else is stopped by the file it is told to read.
        Assert.Contains("Do NOT `cat` your spokes at the end of a turn", Read("stream-runner.md"));
    }

    /// <summary>
    /// PRINT IS TOLD THE SAME RULES, by reference rather than by a second copy — two copies of one
    /// rule is how the two come to disagree, and this rule belongs to the trigger, which both
    /// transports share.
    /// </summary>
    [Fact]
    public void ThePrintRunnerCarriesTheSameRules_ByPointingAtThem_NotByRestatingThem()
    {
        var print = Read("print-runner.md");

        Assert.Contains("`reference/stream-runner.md` §5 and §6 apply", print);
        Assert.Contains("Read them.", print);

        // And it does NOT restate the addressing format, which is what "by reference" means. A second
        // copy here would be the thing this asserts against, not evidence of thoroughness.
        Assert.DoesNotContain("opens a block that runs to the next such line", print);
    }

    /// <summary>
    /// THE BOOT TURN IS TAUGHT WHERE IT HAPPENS. The bridge now starts an orchestration's supervisor
    /// with its role command and nothing else, before anybody has said anything to it — so the file
    /// has to say that an empty first turn is the design and not a channel it failed to find.
    /// </summary>
    [Fact]
    public void TheBootTurnIsExplained_SoAnEmptyFirstTurnIsNotReadAsAFault()
    {
        var stream = Read("stream-runner.md");

        Assert.Contains("BOOT TURN", stream);
        Assert.Contains("the greeting is what creates the topic", stream);
        Assert.Contains("it arrives in the SAME turn as a second message", stream);
    }

    /// <summary>
    /// Refuses rather than returning a guess — a content test that located no content passes by
    /// finding nothing (decision 20).
    /// </summary>
    static string Read(string fileName)
    {
        var relative = fileName == "SKILL.md"
            ? Path.Combine("kit", "skills", "supervisor", "SKILL.md")
            : Path.Combine("kit", "skills", "supervisor", "reference", fileName);

        return KitRepoFiles.Find(relative) is string path
            ? File.ReadAllText(path)
            : throw new Exception($"{relative} was not found walking up from {AppContext.BaseDirectory} — REFUSING to assert about a file this harness never read.");
    }
}
