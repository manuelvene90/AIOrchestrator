using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.DiscoveredChannel;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// THE BASELINE PASS DECIDES; THE ENGINE ONLY APPLIES. This file exists because of what that split
/// makes expressible: the case rev-10 confirmed on the code and declined to race.
///
/// <para>
/// The pass runs at the top of the tick and the sweeps run further down it. A channel FILE appears
/// when a session writes its first entry, which nothing in the tick controls — so a channel can be
/// created between the two, be seen by the SWEEP first, and still be unknown to the pass. The first
/// version gated on a set only the pass wrote, so it then ran on that channel a tick later and filed
/// everything that had arrived in between as history: an entry on disk, its writer believing it
/// visible, and nothing that will ever report it (rev-10 F1).
/// </para>
/// <para>
/// No engine test can reach that state — it needs a file to appear between two statements of one
/// tick. Handing the decision a set that already contains the channel reaches it exactly, with no
/// race and no test-only hook in production.
/// </para>
/// </summary>
public class ChannelBaselinePassTests : IDisposable
{
    const string QUOTED_HEADER = "## [1] FROM supervisor — 2026-08-13 09:00 — quoted into the body above";
    const string MALFORMED_HEADER = "## [2b] FROM supervisor — 2026-08-13 09:12 — a non-numeric index";

    readonly string _tempRoot;
    readonly string _channelFile;

    public ChannelBaselinePassTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-baseline-pass-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        _channelFile = Path.Combine(_tempRoot, "channel.md");

        File.WriteAllText(
            _channelFile,
            "## [1] FROM supervisor — 2026-08-13 09:00 — brief\nbody\n\n"
            + "## [2] FROM implementer — 2026-08-13 09:05 — report\nbody\n\n"
            + $"{QUOTED_HEADER}\n\n"
            + "## [3] FROM supervisor — 2026-08-13 09:10 — accepted\n\n"
            + $"{MALFORMED_HEADER}\nbody\n");
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>A channel nothing has seen is baselined, with both memos' keys computed for it.</summary>
    [Fact]
    public void AnUnseenChannel_IsBaselined_WithBothMemosKeyed()
    {
        var baseline = Assert.Single(Build(seenByShapeSweep: false, seenByIndexSweep: false));

        Assert.Equal(_channelFile, baseline.ChannelFilePath);
        Assert.Contains(baseline.MalformedKeys, key => key.Contains(MALFORMED_HEADER));
        Assert.Contains(baseline.CrossingKeys, key => key.Contains(QUOTED_HEADER));
    }

    /// <summary>
    /// GUARD — rev-10 F1. The SHAPE sweep got there first, so the pass must not touch this channel:
    /// that sweep already absorbed the history at ITS first sight, and everything appearing afterwards
    /// is genuinely new. A pass that baselined it now would file those new offences as history and no
    /// route would ever report them.
    /// </summary>
    [Fact]
    public void AChannelTheSHAPESweepHasAlreadySeen_IsNotBaselined()
    {
        Assert.Empty(Build(seenByShapeSweep: true, seenByIndexSweep: false));
    }

    /// <summary>
    /// GUARD — the same for the index side, and the reason the gate is EITHER rather than BOTH: the two
    /// sweeps walk discovery separately, so a file created between them sits in one set only. Skipping
    /// on either is still correct, because the sweep that has not seen it keeps its own first-sight
    /// branch above its no-offence skip.
    /// </summary>
    [Fact]
    public void AChannelTheINDEXSweepHasAlreadySeen_IsNotBaselined()
    {
        Assert.Empty(Build(seenByShapeSweep: false, seenByIndexSweep: true));
    }

    /// <summary>
    /// THE PASS ASKS THE SHARED BUILDERS, AND THE FORMAT IS PINNED TO A LITERAL.
    ///
    /// <para>
    /// RENAMED, because the old name — `TheKeysAreTheSweepsOwnCompositions` — promised something this
    /// test cannot see. It compares the pass's keys with the same helpers the pass calls, so it pinned
    /// "the pass agrees with the helper", nearly a tautology; a SWEEP re-inlining its key would leave
    /// it green, which is the failure the name advertised (rev-9 F1). A guard-sounding name over a
    /// tautology is worse than no test.
    /// </para>
    /// <para>
    /// The literal assertions are what the rename leaves worth having: an edit to a helper's format
    /// now shows up as a difference instead of moving the expectation silently with it. The property
    /// the old name claimed — one composer tree-wide — is pinned by
    /// <see cref="MemoKeyCompositionScanTests"/> instead, where it can actually be seen.
    /// </para>
    /// </summary>
    [Fact]
    public void ThePassAsksTheSharedBuilders_AndTheirFormatIsPinned()
    {
        var baseline = Assert.Single(Build(seenByShapeSweep: false, seenByIndexSweep: false));

        Assert.Equal(
            ChannelShape_Validator.Build_MemoKey(_channelFile, MALFORMED_HEADER),
            Assert.Single(baseline.MalformedKeys));

        var crossing = Assert.Single(ChannelIndexSequence_Screen.Find_Crossings(
            ChannelIndexSequence_Screen.Read_Headers(archiveText: "", liveText: File.ReadAllText(_channelFile))));

        Assert.Equal(
            ChannelIndexSequence_Screen.Build_MemoKey(_channelFile, crossing),
            Assert.Single(baseline.CrossingKeys));

        // THE FORMATS THEMSELVES, as literals. Everything above tracks a helper edit; these do not.
        Assert.Equal($"{_channelFile}|{MALFORMED_HEADER}", ChannelShape_Validator.Build_MemoKey(_channelFile, MALFORMED_HEADER));
        Assert.Equal(
            $"{_channelFile}|{ChannelIndexSequence_Screen.Build_DedupeKey(crossing)}",
            ChannelIndexSequence_Screen.Build_MemoKey(_channelFile, crossing));
    }

    /// <summary>A channel with nothing wrong in it still baselines — with two empty key lists.</summary>
    [Fact]
    public void ACleanChannel_IsBaselined_WithNothingToRemember()
    {
        File.WriteAllText(_channelFile, "## [1] FROM supervisor — 2026-08-13 09:00 — brief\nbody\n");

        var baseline = Assert.Single(Build(seenByShapeSweep: false, seenByIndexSweep: false));

        Assert.Empty(baseline.MalformedKeys);
        Assert.Empty(baseline.CrossingKeys);
    }

    IReadOnlyList<ChannelBaseline> Build(bool seenByShapeSweep, bool seenByIndexSweep)
    {
        HashSet<string> shapeBaselined = seenByShapeSweep ? [_channelFile] : [];
        HashSet<string> indexBaselined = seenByIndexSweep ? [_channelFile] : [];

        return ChannelBaseline_Pass.Build_ForUnseenChannels(
            [DiscoveredChannel_Factory.Create_ForImplementer("orch-1", "imp-1", _channelFile)],
            shapeBaselined,
            indexBaselined);
    }
}
