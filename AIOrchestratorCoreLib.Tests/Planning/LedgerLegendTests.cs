using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning;

/// <summary>
/// JOINS THE COPIES OF THE LEDGER LEGEND, which nothing joined before and which had already drifted.
///
/// It was written down five times — two role commands, the parser's docstring, the supervisor ledger
/// hook, and the seed text — and two of those had four markers instead of five by the time anyone
/// counted. The mechanism is the part worth pinning: `- [-]` was added to the parser ADDITIVELY and
/// safely ("no existing ledger contains one, so every file parses exactly as it did"), which is true
/// of the parser and false of every copy of its list. The same additive marker took out two
/// independent regexes the same evening.
///
/// `PlanLedger_Markers` is now the one definition and the seed BUILDS from it, so that copy cannot
/// drift again by construction. These tests cover the two joins construction cannot make: the
/// parser's regex (a <c>GeneratedRegex</c> literal) and the bash hook (a different language).
/// </summary>
public class LedgerLegendTests
{
    /// <summary>
    /// Every marker the legend teaches is one the parser actually accepts. Catches a marker invented
    /// in prose that no code has ever read — the direction that would have a supervisor writing
    /// something the progress bar silently ignores.
    /// </summary>
    [Fact]
    public void EveryTaughtMarkerParses()
    {
        foreach (var (marker, meaning) in PlanLedger_Markers.ALL)
        {
            var progress = PlanLedger_Parser.Parse_OrNull($"- [{marker}] a task");

            Assert.True(progress != null, $"the legend teaches `- [{marker}]` ({meaning}) and the parser ignores it");
            Assert.Single(progress!.Lines);
            Assert.Equal(marker, progress.Lines[0].Marker);
        }
    }

    /// <summary>
    /// And the other direction, which is the one that actually broke: a marker the PARSER accepts
    /// must be taught. A sixth marker added to the regex and to nothing else is invisible to every
    /// human who has to write the ledger.
    ///
    /// It is asserted by exhausting the printable ASCII the regex could plausibly carry rather than
    /// by reading the pattern back, because a test that re-derives the regex from the regex proves
    /// nothing.
    ///
    /// `X` IS SKIPPED ON A PREMISE THIS TEST NOW VERIFIES FIRST (rev-7 L3). The skip was justified in
    /// prose — "the parser normalises it to `x`, so it is a spelling of a taught marker" — and the
    /// test never checked that. "X normalises to a taught marker" and "X is an untaught marker we
    /// chose to skip" were indistinguishable to it, which is decision 20's two-routes-to-one-green in
    /// a file whose own docstring cites decision 20. Take the normalisation out and this test — whose
    /// whole job is "no accepted marker goes untaught" — stayed green.
    /// </summary>
    [Fact]
    public void EveryParsedMarkerIsTaught()
    {
        var taught = PlanLedger_Markers.ALL.Select(entry => entry.Marker).ToHashSet();

        // THE PREMISE OF THE SKIP BELOW, asserted before it is relied on. If this ever fails, `[X]`
        // and `[x]` reach the owner as two spellings of done in one message.
        var upperCase = PlanLedger_Parser.Parse_OrNull("- [X] a task");

        Assert.True(upperCase != null, "the parser rejects `- [X]`, so the skip below hides a marker rather than a spelling");
        Assert.Equal("x", upperCase!.Lines[0].Marker);

        for (var character = ' '; character <= '~'; character++)
        {
            var marker = character.ToString();

            // Only `X` is skipped now. The old skip also listed `]`, which is dead: `]` is not in the
            // regex class, so the null check below already handles it — and listing it made the skip
            // look more load-bearing than it is.
            if (marker == "X")
                continue;

            if (PlanLedger_Parser.Parse_OrNull($"- [{marker}] a task") == null)
                continue;

            Assert.True(taught.Contains(marker), $"the parser accepts `- [{marker}]` and the legend does not teach it");
        }
    }

    /// <summary>
    /// THE LIST ITSELF, asserted literally, because four of the tests here loop over it (rev-7 L4).
    ///
    /// A test that reads its expected values from the same source production renders from can prove
    /// "this copy agrees with `ALL`" and never "`ALL` is right" — and an emptied `ALL` makes those
    /// loops iterate zero times and pass. `EveryParsedMarkerIsTaught` is a genuine oracle against
    /// that, because the parser's regex is an independent literal, so this is belt to its braces; the
    /// residual it closes is narrower and real: a MEANING edited in `ALL` was caught only by the bash
    /// hook test, leaving one cross-language test load-bearing alone.
    /// </summary>
    [Fact]
    public void TheMarkerListIsWhatEverythingElseAssumes()
    {
        Assert.Equal(
            [(" ", "open"), (">", "in progress"), ("x", "done"), ("!", "blocked"), ("?", "blocked on the owner"), ("-", "not doing")],
            PlanLedger_Markers.ALL);
    }

    /// <summary>
    /// THE SEEDED LEDGER TEACHES ALL FIVE. It taught four — it had lost `- [-]` entirely and narrowed
    /// `- [!]` to "blocked on owner", leaving a supervisor blocked on anything else with no marker to
    /// use. It builds from <see cref="PlanLedger_Markers"/> now, so this asserts the wiring rather
    /// than the wording.
    /// </summary>
    [Fact]
    public void TheLegendRendersEveryMarker()
    {
        var legend = PlanLedger_Markers.Describe_Legend();

        foreach (var (marker, meaning) in PlanLedger_Markers.ALL)
        {
            Assert.Contains($"`- [{marker}]`", legend);
            Assert.Contains(meaning, legend);
        }

        // "blocked", not "blocked on owner" — the narrowing that left the other three-quarters of
        // blocked unrepresentable.
        Assert.DoesNotContain("blocked on owner", legend);
    }

    /// <summary>
    /// THE SEED AS ACTUALLY WRITTEN, end to end, because the renderer being right does not prove the
    /// seed uses it — and "the shared thing exists and the caller ignores it" is precisely the shape
    /// that produced this whole task.
    ///
    /// It also pins the ownership sentence. The seed said "maintained by the SUPERVISOR" for EVERY
    /// orchestration, basic ones included, so a solo session read in the first line of its own ledger
    /// that the file belonged to a role its mode does not have. That text is permanent: nothing
    /// re-seeds the file, not even promotion, which is why it names both roles rather than being
    /// chosen from the mode at creation.
    /// </summary>
    [Fact]
    public void TheSeededLedgerTeachesEveryMarkerAndNamesBothRoles()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-seed-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var paths = SupervisionPaths_Factory.Create(tempRoot);
            Directory.CreateDirectory(paths.Get_OrchestrationFolder("crm-4"));

            PlanSeed_Writer.Ensure_Exists(paths, "crm-4", "crm");

            var seed = File.ReadAllText(paths.Get_PlanFile("crm-4"));

            foreach (var (marker, meaning) in PlanLedger_Markers.ALL)
                Assert.True(seed.Contains($"`- [{marker}]` {meaning}"), $"the seeded ledger does not teach `- [{marker}]` {meaning}");

            // Both modes named, so the sentence stays true after a promotion it will never be rewritten for.
            Assert.Contains("SOLO", seed);
            Assert.Contains("SUPERVISOR", seed);

            // And the seed's own first task line parses — a seeded ledger that the parser ignores
            // would show an empty bar from minute one, which is what the seed exists to prevent.
            var progress = PlanLedger_Parser.Parse_OrNull(seed);

            Assert.True(progress != null, "the seeded ledger does not parse");
            Assert.Equal(1, progress!.Total);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// THE BASH COPY, which is the only one that cannot import the C# definition. This is the join of
    /// last resort and it is the copy that drifted.
    ///
    /// It FAILS LOUDLY if it cannot find the hook. A harness that cannot find what it tests must
    /// refuse to run rather than certify the absence of the thing it is testing — the ledger hook's
    /// own sibling did exactly that once, reporting sixteen confident failures about code it never
    /// executed, and nothing-found reads as everything-fine.
    /// </summary>
    [Fact]
    public void TheBashHookTeachesEveryMarkerToo()
    {
        var hook = Find_LedgerHook_OrNull();

        Assert.True(hook != null, "could not locate kit/hooks/supervisor-ledger-check.sh — the harness is not reading the file it asserts about");

        var text = File.ReadAllText(hook!);

        foreach (var (marker, meaning) in PlanLedger_Markers.ALL)
            Assert.True(
                text.Contains($"- [{marker}] {meaning}"),
                $"the ledger hook does not teach `- [{marker}] {meaning}` — the bash copy has drifted from PlanLedger_Markers");
    }

    static string? Find_LedgerHook_OrNull()
    {
        var folder = AppContext.BaseDirectory;

        for (var depth = 0; depth < 8; depth++)
        {
            var candidate = Path.Combine(folder, "kit", "hooks", "supervisor-ledger-check.sh");

            if (File.Exists(candidate))
                return candidate;

            var parent = Directory.GetParent(folder);

            if (parent == null)
                break;

            folder = parent.FullName;
        }

        return null;
    }
}
