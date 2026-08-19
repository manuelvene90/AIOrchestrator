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
    /// <summary>
    /// Backticked capitals that are NOT protocol markers. The PLATFORM CODES join it derived from
    /// their own table rather than typed out here: they are backticked and capitalised, so this
    /// guard flags every one of them, and a hand-copied list of eighteen codes would be the second
    /// copy the whole Platform_Abbreviations class exists to avoid.
    /// </summary>
    static readonly IReadOnlyList<string> NOT_MARKERS =
    [
        "PARALLEL UNITS", "UNPROVEN", "HOLD", "FROM", "AWAY MODE ON",
        .. AIOrchestratorCoreLib.Sessions.Platform_Abbreviations.ALL.Select(entry => entry.Code),
    ];

    /// <summary>
    /// EVERY protocol marker, which since 2026-08-13 is more than the state resolver's own.
    ///
    /// `ALL_MARKERS` means "phrases `MemberState_Resolver` resolves to a member STATE", and that was
    /// the whole vocabulary while the resolver was the only thing reading markers. It is not any
    /// more: `HANDOVER` is read by `HandoverEntry_Detector`, through the same matcher, to decide
    /// whether a solo may ask for a crew — a real marker, acted on by real code, that changes no
    /// member state.
    ///
    /// So the docs guard walks the UNION while the shape guard below keeps walking the resolver's own
    /// list, because "this phrase declares a state" is only true of the state ones. Adding `HANDOVER`
    /// to `ALL_MARKERS` instead would have made the shape guard demand that a handover entry change
    /// a member's state, which it neither does nor should.
    ///
    /// The guard CAUGHT this drift the moment solo.md taught the new marker — it did its job, and
    /// what needed extending was its model of what a marker is for.
    /// </summary>
    static readonly IReadOnlyList<string> PROTOCOL_MARKERS =
    [
        .. MemberState_Resolver.ALL_MARKERS,
        AIOrchestratorCoreLib.GeneralSupervision.HandoverEntry_Detector.HANDOVER_MARKER,

        // STATUS, matched by MirrorText_Formatter rather than by the state resolver — the same shape
        // as HANDOVER above, and found the same way: the guard fired the moment communicator.md
        // entered the repo on 2026-08-19. That file had taught `STATUS` for as long as it existed,
        // and this test could not see it, because it was the one role command that was never
        // version-controlled. The union was not wrong; it was reading five of the six roles.
        AIOrchestratorCoreLib.Mirroring.MirrorText_Formatter.STATUS_SUBJECT_PREFIX,
    ];

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
                    PROTOCOL_MARKERS.Contains(phrase),
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

        // THE UNION, so a marker acted on by something other than the state resolver cannot exist in
        // code that nobody is ever told to write. That is the failure this direction was built for —
        // STANDING BY sat unread in the matcher for exactly that long — and it applies to a promotion
        // marker no less than to a state one.
        foreach (var marker in PROTOCOL_MARKERS)
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
    ///
    /// STANDING BY IS EXCLUDED AND THIS TEST IS WHAT CAUGHT IT. Narrowing that marker to "leads the
    /// subject and stands alone" made the promise above false for it alone, and this went red before
    /// the role commands had been touched — which is the entire reason it exists. The commands now
    /// carve it out explicitly, and the case below asserts the shape they promise for it instead.
    /// </summary>
    [Fact]
    public void ASubjectShapedAsTheDocsPromiseActuallyDeclares()
    {
        foreach (var marker in MemberState_Resolver.ALL_MARKERS)
        {
            if (MemberState_Resolver.CLOSING_MARKERS.Contains(marker))
                continue;

            if (marker == MemberState_Resolver.STANDING_BY_MARKER)
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
    /// THE STANDING-BY PROMISE, which is the opposite one and therefore needs its own case: the docs
    /// say that marker must LEAD the subject and stand ALONE there, so the result-first shape must NOT
    /// declare — and the shape they show as declaring must.
    ///
    /// Both directions in one test on purpose. Asserting only that the mixed shape fails would stay
    /// green if the matcher stopped recognising the marker entirely, which is the way a narrowing rule
    /// dies quietly.
    /// </summary>
    [Fact]
    public void TheStandingByShapeTheDocsPromiseIsTheOnlyOneThatDeclares()
    {
        Assert.Equal(MemberStates.StandingBy, Resolve_MemberSubject("STANDING BY — waiting on rev-4's re-check"));

        Assert.Equal(MemberStates.AwaitingSupervisorReview, Resolve_MemberSubject("TASK 1 committed abc1234. STANDING BY — the details"));
        Assert.Equal(MemberStates.AwaitingSupervisorReview, Resolve_MemberSubject("STANDING BY — one correction: the wrong file is named"));
    }

    /// <summary>
    /// AND THE COMMANDS MUST SAY SO. The narrowing went red in the case above before a single doc was
    /// touched; nothing would have gone red if the docs had been left teaching "anywhere in the
    /// subject" for this marker, because that promise is prose to every other test here.
    /// </summary>
    [Fact]
    public void TheRoleCommandsTeachThatStandingByMustLeadItsSubject()
    {
        foreach (var file in Find_RoleCommandFiles())
        {
            var text = File.ReadAllText(file);

            if (!text.Contains(MemberState_Resolver.STANDING_BY_MARKER))
                continue;

            Assert.True(
                text.Contains("LEAD your subject") || text.Contains("LEADS the subject"),
                $"{Path.GetFileName(file)} teaches `STANDING BY` without saying it must lead the subject");
        }
    }

    static MemberStates Resolve_MemberSubject(string subject)
    {
        return MemberState_Resolver.Resolve(
        [
            Build(1, ChannelAuthors.Supervisor, "brief", "do the work"),
            Build(2, ChannelAuthors.Implementer, subject, "the body"),
        ]);
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
