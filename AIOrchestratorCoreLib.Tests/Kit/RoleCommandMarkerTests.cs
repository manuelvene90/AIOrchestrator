using System.Text.RegularExpressions;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Status;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// Ties the role protocols to the matcher, because they live in different files and nothing joined
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
///
/// THE SENTENCE ABOVE WAS FALSE UNTIL 2026-09-17, in two ways, and both are fixed here rather than
/// softened, because a guard that states a rule it does not apply is worse than one that states
/// nothing: a reader stops looking.
///
/// <list type="number">
/// <item>
/// "EVERY marker the matcher acts on" walked a HAND-WRITTEN list. A marker was guarded only once
/// somebody remembered to add it, which is the property the rule exists to remove — and the two
/// newest markers in the system, <c>REROUTE:</c> and <c>FIXED:</c>, were not on it. The set is now
/// the GRAMMAR (<see cref="ChannelGrammar.All_Markers"/>) plus the two matched outside it, so the
/// list is a DENY list: a new marker is guarded the moment it is added to the grammar, and leaving
/// one out is a decision somebody has to make on purpose. That is the same argument
/// <see cref="NOT_MARKERS"/> already made for the other direction.
/// </item>
/// <item>
/// "somewhere" meant the SKILL.md alone. A role protocol is a folder now, and <c>DEADLINE:</c> is
/// taught in <c>supervisor/reference/owner-messages.md</c> and in no SKILL.md — so the old reading
/// called the system's own marker untaught, and a plan that put a rule in a reference file AND
/// added its marker to the list would have gone red for doing both correct things at once. Every
/// direction here now reads <see cref="KitRepoFiles.Find_AllRoleProtocolDocs"/>.
/// </item>
/// </list>
///
/// THE ONE CLAIM DROPPED ON THE WAY, because it was checked and found stale: the old text said
/// <c>TO:</c> is "a marker the APP writes when it splits a turn's reply, that no role has any
/// business writing", and that was the entire reason for keeping two sets. It is taught in three
/// SKILL.md files and in <c>supervisor/reference/stream-runner.md</c>, which tells the supervisor to
/// write exactly that line. With the exception gone the two sets collapse into one.
public class RoleCommandMarkerTests
{
    /// <summary>
    /// All-caps backticked phrases in the role protocols that are deliberately NOT protocol markers.
    /// Listed rather than pattern-matched away, so inventing a new one is a decision somebody makes
    /// on purpose instead of a typo that slips through as vocabulary.
    ///
    /// <para>
    /// The PLATFORM CODES join it derived from their own table rather than typed out here: they are
    /// backticked and capitalised, so this guard flags every one of them, and a hand-copied list of
    /// eighteen codes would be the second copy the whole <c>Platform_Abbreviations</c> class exists
    /// to avoid.
    /// </para>
    /// </summary>
    static readonly IReadOnlyList<string> NOT_MARKERS =
    [
        "PARALLEL UNITS", "UNPROVEN", "HOLD", "FROM", "AWAY MODE ON",
        .. AIOrchestratorCoreLib.Sessions.Platform_Abbreviations.ALL.Select(entry => entry.Code),
    ];

    /// <summary>
    /// The markers matched OUTSIDE the grammar file. Everything else a role writes or the app reads
    /// is a row of <c>kit/grammar/channel-grammar.json</c>, and reaches both directions below
    /// through <see cref="ChannelGrammar.All_Markers"/> without anyone maintaining a copy.
    ///
    /// <para>
    /// These two are const strings in their own readers, and each one was caught by this guard
    /// within a minute of a role protocol teaching it — which is what the union is for.
    /// <c>HANDOVER</c> is read by <c>HandoverEntry_Detector</c> to decide whether a solo may ask for
    /// a crew; <c>STATUS</c> is matched by <c>MirrorText_Formatter</c>. Neither declares a member
    /// state, which is why <c>MemberState_Resolver.ALL_MARKERS</c> is not the set either direction
    /// walks: that list answers "does this phrase declare a STATE", and only the shape cases below
    /// ask that question.
    /// </para>
    /// <para>
    /// Folding them into the grammar would be the better end state and it is not this guard's to do:
    /// the grammar is read by <c>channel-append.sh</c> too, and a marker the tool would then offer to
    /// write is a change to the tool's surface, not to a test.
    /// </para>
    /// </summary>
    static readonly IReadOnlyList<string> MARKERS_MATCHED_OUTSIDE_THE_GRAMMAR =
    [
        AIOrchestratorCoreLib.GeneralSupervision.HandoverEntry_Detector.HANDOVER_MARKER,
        AIOrchestratorCoreLib.Mirroring.MirrorText_Formatter.STATUS_SUBJECT_PREFIX,
    ];

    /// <summary>
    /// EVERY MARKER THE APP RECOGNISES — the grammar's own rows plus the two matched outside it.
    /// ONE set, read by both directions: the phrase a role may teach and the phrase a role must be
    /// taught are the same vocabulary, and the two-set version of this file spent a long docstring
    /// explaining an asymmetry that turned out not to exist.
    /// </summary>
    static IReadOnlyList<string> Recognised_Markers()
    {
        return [.. ChannelGrammar.All_Markers, .. MARKERS_MATCHED_OUTSIDE_THE_GRAMMAR];
    }

    /// <summary>
    /// A phrase the docs teach must be one the matcher acts on. Catches a typo, a rename that
    /// updated one side, and a marker invented in prose that no code has ever read.
    /// </summary>
    [Fact]
    public void EveryMarkerLookingPhraseInTheRoleCommandsIsARealMarker()
    {
        var files = Find_RoleCommandFiles();
        var recognised = Recognised_Markers();

        foreach (var file in files)
        {
            // THE COLON FORM TOO, since 2026-09-10. The pattern required a backtick immediately after
            // the capital letters, so `QUESTION:` — the form the roles actually teach and the app
            // actually matches — was invisible to this guard: it only ever saw the bare word. That
            // blind spot is what let three skills carry hand-written entry templates through E3,
            // every line of which is a marker the guard could not see.
            //
            // The trailing colon is trimmed before the lookup so both forms resolve against the same
            // set, which is the grammar's own rule (ChannelGrammar.Bare) applied to the docs.
            foreach (Match match in Regex.Matches(File.ReadAllText(file), "`([A-Z][A-Z]+(?: [A-Z]+)*):?`"))
            {
                var phrase = match.Groups[1].Value;

                if (NOT_MARKERS.Contains(phrase))
                    continue;

                // MATCHED IN EITHER FORM. The set holds the markers as the app spells them — some with
                // a colon, some without — and the docs legitimately write either, so a phrase counts
                // as real when it matches with or without one. What is still caught is a phrase that
                // matches NEITHER: a typo, a rename that updated one side, a marker invented in prose.
                Assert.True(
                    recognised.Contains(phrase)
                    || recognised.Contains($"{phrase}:")
                    || recognised.Any(marker => marker.TrimEnd(':') == phrase),
                    $"{Path.GetFileName(file)} teaches `{phrase}`, which the matcher does not act on");
            }
        }
    }

    /// <summary>
    /// And the other direction: a marker the code acts on but no role protocol teaches is vocabulary
    /// nobody will ever write. STANDING BY existed in code for exactly that long.
    ///
    /// <para>
    /// IT MUST BE TAUGHT INSIDE A CODE SPAN — backticks or a fenced block — and not merely appear in
    /// the text. The boot marker is the word <c>online</c>: a plain substring search over the prose
    /// passes on "tell the owner it's online" with every boot instruction in the kit deleted, which
    /// is an assertion that holds for a reason that is not the one it claims. Every marker in this
    /// system is taught in a code span today (measured 2026-09-17, all 24), so the stricter reading
    /// costs nothing and removes the one marker that was being asserted by accident.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryMarkerTheMatcherActsOnIsTaughtSomewhere()
    {
        var taught = string.Concat(Find_RoleCommandFiles().Select(file => Taught_Vocabulary(File.ReadAllText(file))));

        foreach (var marker in Recognised_Markers())
        {
            Assert.True(
                taught.Contains(marker, StringComparison.Ordinal),
                $"no role protocol teaches `{marker}` in a code span, so nobody will ever write it");
        }
    }

    /// <summary>
    /// The backticked spans of a protocol file — inline and fenced — which is where this kit teaches
    /// a phrase a session must write VERBATIM. Prose about a marker is not a teaching of it.
    /// </summary>
    static string Taught_Vocabulary(string markdown)
    {
        return string.Join('\n', CODE_SPANS.Matches(markdown).Select(match => match.Value));
    }

    static readonly Regex CODE_SPANS = new("```.*?```|`[^`\n]+`", RegexOptions.Singleline);

    /// <summary>
    /// AND THE STRICTNESS ITSELF IS PINNED, because it is the one part of the direction above that
    /// cannot go red on its own: reading the whole file instead of its code spans makes the guard
    /// WEAKER, and a weaker guard passes. So the reading is asserted here directly — prose that
    /// mentions a marker is not a teaching of it, a backticked one is, and a fenced block is.
    ///
    /// Without this case the boot marker `online` would go back to being satisfied by the sentence
    /// "so I can't know when it's online", which is a guard held up by an English adjective.
    /// </summary>
    [Fact]
    public void ProseAboutAMarkerIsNotATeachingOfIt()
    {
        Assert.DoesNotContain("STANDING BY", Taught_Vocabulary("the member declares STANDING BY when it is idle"));

        Assert.Contains("STANDING BY", Taught_Vocabulary("write `STANDING BY — waiting on rev-4`"));

        Assert.Contains("FIXED:", Taught_Vocabulary("answer it:\n\n```\nFIXED: 9f3c1de\n```\n"));
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
    /// EVERY FILE OF EVERY ROLE PROTOCOL — the SKILL.md and its `reference/` beside it. The suite
    /// runs from bin/Debug/net10.0 and `kit` is not a project, so there is nothing to ask for this
    /// path; <see cref="KitRepoFiles"/> walks up to the repo root, the same shape as reading App.xaml
    /// as text.
    ///
    /// <para>
    /// IT REFUSES RATHER THAN RETURNING WHAT IT FOUND (decision 20). Two ways to read nothing, and
    /// the second is the one that bit: no files at all, and — the failure this class had until
    /// 2026-09-17 — only the SKILL.md of each role, which turns "taught in a reference file" into
    /// "not taught" and makes every direction here quietly answer a different question.
    /// </para>
    /// </summary>
    static IReadOnlyList<string> Find_RoleCommandFiles()
    {
        var docs = KitRepoFiles.Find_AllRoleProtocolDocs();
        var roles = docs.Select(entry => entry.Role).Distinct().Count();

        Assert.True(roles >= 4, $"found {roles} role protocols — the harness is not reading them");

        Assert.Contains(
            docs,
            entry => entry.Path.Contains($"{Path.DirectorySeparatorChar}reference{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        return [.. docs.Select(entry => entry.Path)];
    }
}
