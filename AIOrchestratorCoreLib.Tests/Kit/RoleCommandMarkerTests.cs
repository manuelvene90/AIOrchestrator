using System.Text.RegularExpressions;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Status;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// Ties the role commands to the matcher, because they live in different files and nothing joined
/// them. Three findings in one night were the docs drifting from the rule: they taught an entry
/// "containing exactly WRITING WINDOW OPEN" after the code required position, they reassured a
/// reviewer about protection that does not exist in a subject, and they told the supervisor to write
/// markers that set nothing. All three were found only because a reviewer read both files side by
/// side.
///
/// WHAT THIS CAN AND CANNOT DO, stated plainly so nobody trusts it further than it goes. It pins
/// PHRASES and SHAPES: a marker the docs name must be one the matcher accepts, and every marker the
/// matcher acts on must be taught somewhere. It cannot read prose — a true sentence about a false
/// capability, which is what the supervisor's line was, passes this test untouched. It would have
/// caught two of the three.
/// </summary>
public class RoleCommandMarkerTests
{
    /// <summary>
    /// All-caps backticked phrases in the role commands that are deliberately NOT protocol markers.
    /// Listed rather than pattern-matched away, so inventing a new one is a decision somebody makes
    /// on purpose instead of a typo that slips through as vocabulary.
    /// </summary>
    static readonly string[] NOT_MARKERS = ["PARALLEL UNITS", "UNPROVEN", "HOLD", "FROM", "AWAY MODE ON"];

    /// <summary>
    /// A phrase the docs teach must be one the matcher acts on. Catches a typo, a rename that
    /// updated one side, and a marker invented in prose that no code has ever read.
    /// </summary>
    [Fact]
    public void EveryMarkerLookingPhraseInTheRoleCommandsIsARealMarker()
    {
        var files = Find_RoleCommandFiles();

        Assert.True(files.Count >= 4, $"found {files.Count} role commands — the harness is not reading them");

        foreach (var file in files)
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file), "`([A-Z][A-Z]+(?: [A-Z]+)*)`"))
            {
                var phrase = match.Groups[1].Value;

                if (NOT_MARKERS.Contains(phrase))
                    continue;

                Assert.True(
                    MemberState_Resolver.ALL_MARKERS.Contains(phrase),
                    $"{Path.GetFileName(file)} teaches `{phrase}`, which the matcher does not act on");
            }
        }
    }

    /// <summary>
    /// And the other direction: a marker the code acts on but no role command teaches is vocabulary
    /// nobody will ever write. STANDING BY existed in code for exactly that long.
    /// </summary>
    [Fact]
    public void EveryMarkerTheMatcherActsOnIsTaughtSomewhere()
    {
        var taught = string.Concat(Find_RoleCommandFiles().Select(File.ReadAllText));

        foreach (var marker in MemberState_Resolver.ALL_MARKERS)
            Assert.True(taught.Contains(marker), $"no role command teaches `{marker}`");
    }

    /// <summary>
    /// THE ONE THAT MATTERS: the shape the docs promise must actually declare.
    ///
    /// The promise is specific — a marker may go in the subject ANYWHERE, "so a subject naming a
    /// result before its marker still counts" — so the fixture names a result first. That detail is
    /// the entire test. Written the obvious way, with the marker leading the subject, it passed
    /// under BOTH the token rule and the position rule it exists to distinguish, and reverting the
    /// matcher left it green: it proved that a marker declares, which nobody doubted, rather than
    /// that the DOCUMENTED shape does.
    /// </summary>
    [Fact]
    public void ASubjectShapedAsTheDocsPromiseActuallyDeclares()
    {
        foreach (var marker in MemberState_Resolver.ALL_MARKERS)
        {
            if (MemberState_Resolver.CLOSING_MARKERS.Contains(marker))
                continue;

            var state = MemberState_Resolver.Resolve(
            [
                Build(1, ChannelAuthors.Supervisor, "brief", "do the work"),
                Build(2, ChannelAuthors.Implementer, $"TASK 1 committed abc1234. {marker} — the details", "the body"),
            ]);

            Assert.True(
                state != MemberStates.AwaitingSupervisorReview,
                $"`{marker}` after a result in a subject changed nothing — the docs promise it declares");
        }
    }

    /// <summary>
    /// The closing markers have no state of their own: they must END the window they name. Same
    /// documented shape, and this is the direction where a miss pins a member forever.
    /// </summary>
    [Fact]
    public void AClosingMarkerShapedAsTheDocsPromiseClosesItsWindow()
    {
        foreach (var closing in MemberState_Resolver.CLOSING_MARKERS)
        {
            var opening = closing.Replace("CLOSED", "OPEN");

            Assert.Contains(opening, MemberState_Resolver.ALL_MARKERS);

            var state = MemberState_Resolver.Resolve(
            [
                Build(1, ChannelAuthors.Supervisor, "brief", "do the work"),
                Build(2, ChannelAuthors.Implementer, $"{opening} — Parser.cs", "starting"),
                Build(3, ChannelAuthors.Implementer, $"A3 COMPLETE · 529 tests · {closing}", "landed"),
            ]);

            Assert.True(
                state != MemberStates.WritingWindowOpen,
                $"`{closing}` after a result in a subject did not close the window `{opening}` opened");
        }
    }

    static IChannelEntry Build(int index, ChannelAuthors author, string subject, string body)
    {
        return ChannelEntry_Factory.Create(
            index, author, "2026-08-12", subject, body,
            $"## [{index}] FROM {author} — 2026-08-12 10:00 — {subject}\n{body}");
    }

    /// <summary>
    /// Walks up to the repo root. The suite runs from bin/Debug/net10.0 and `kit` is not a project,
    /// so there is nothing to ask for this path — the same shape as reading App.xaml as text.
    /// </summary>
    static IReadOnlyList<string> Find_RoleCommandFiles()
    {
        var folder = AppContext.BaseDirectory;

        for (var depth = 0; depth < 8; depth++)
        {
            var candidate = Path.Combine(folder, "kit", "commands");

            if (Directory.Exists(candidate))
                return [.. Directory.GetFiles(candidate, "*.md")];

            var parent = Directory.GetParent(folder);

            if (parent == null)
                break;

            folder = parent.FullName;
        }

        return [];
    }
}
