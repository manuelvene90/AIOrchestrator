using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// ONE COMPOSER PER MEMO KEY, TREE-WIDE — the property that actually prevents a divergent key, pinned
/// where it can be seen.
///
/// <para>
/// A memo key is written by one component and looked up by another: the baseline pass records what a
/// channel already contained, and the two sweeps ask whether they have seen a thing before. If those
/// two composed the key even slightly differently, no lookup would ever match and every offence would
/// be reported for ever — which looks exactly like the invisible-entry bug the memos exist to stop.
/// The key was spelled out inline in two places before `14234c3`, byte-identical, with nothing holding
/// them together (rev-10 F3).
/// </para>
/// <para>
/// WHY A SOURCE SCAN RATHER THAN A BEHAVIOUR TEST. The unit test beside this one compares the pass's
/// keys with the helpers the pass itself calls — it cannot see a SWEEP re-inlining its own key, which
/// is the failure that matters (rev-9 F1). Only the source can answer "is there exactly one composer".
/// </para>
/// <para>
/// IT REFUSES TO RUN IF IT CANNOT FIND THE SOURCE. A harness that cannot locate what it tests must fail
/// loudly rather than certify the absence of the thing it is looking for: `hook-behaviour-check.sh`
/// reported sixteen confident failures about code it never executed, and nothing-is-ALLOW is how a
/// scan passes vacuously for ever (decision 20).
/// </para>
/// </summary>
public class MemoKeyCompositionScanTests
{
    /// <summary>
    /// The shape a channel-scoped memo key has, and the thing no OTHER line may contain: an
    /// interpolation boundary either side of the separator. It appears exactly twice in the core lib
    /// today, and both are the helper bodies.
    /// </summary>
    const string COMPOSITION_MARK = "}|{";

    const string SHAPE_HELPER_FILE = "ChannelShape_Validator.cs";
    const string SCREEN_HELPER_FILE = "ChannelIndexSequence_Screen.cs";

    /// <summary>Below this, the walk found something that is not the library and the scan means nothing.</summary>
    const int PLAUSIBLE_SOURCE_FILE_FLOOR = 50;

    [Fact]
    public void TheOnlyComposersOfAMemoKeyAreTheTwoSharedBuilders()
    {
        var sourceFiles = Find_CoreLibSourceFiles();

        // THE HARNESS PROVES ITSELF FIRST. Every assertion below is about ABSENCE, so a scan that
        // found nothing would be the strongest possible pass and would mean nothing at all.
        Assert.True(
            sourceFiles.Count >= PLAUSIBLE_SOURCE_FILE_FLOOR,
            $"the scan found {sourceFiles.Count} source files — it is not reading AIOrchestratorCoreLib, so it can prove nothing");

        Assert.Contains(sourceFiles, file => Path.GetFileName(file) == SHAPE_HELPER_FILE);
        Assert.Contains(sourceFiles, file => Path.GetFileName(file) == SCREEN_HELPER_FILE);

        List<string> composingLines = [];

        foreach (var file in sourceFiles)
        {
            var lines = File.ReadAllLines(file);

            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(COMPOSITION_MARK))
                    composingLines.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
            }
        }

        // The two helper bodies, and nothing else anywhere in the library. The count is asserted as
        // well as the membership: without it, a third composer appearing beside them would pass.
        Assert.Equal(2, composingLines.Count);
        Assert.Contains(composingLines, line => line.StartsWith(SHAPE_HELPER_FILE) && line.Contains("headerLine"));
        Assert.Contains(composingLines, line => line.StartsWith(SCREEN_HELPER_FILE) && line.Contains("Build_DedupeKey"));
    }

    /// <summary>
    /// Walks up from the test binary to the repo root and takes every source file of the library. The
    /// suite runs from bin/Debug/net10.0, and the library's own source is not something a test can ask
    /// a type for — the same shape as <c>RoleCommandMarkerTests</c> reading the kit's markdown.
    /// </summary>
    static IReadOnlyList<string> Find_CoreLibSourceFiles()
    {
        var folder = AppContext.BaseDirectory;

        for (var depth = 0; depth < 8; depth++)
        {
            var candidate = Path.Combine(folder, "AIOrchestratorCoreLib");

            if (Directory.Exists(candidate))
            {
                return
                [
                    .. Directory
                        .GetFiles(candidate, "*.cs", SearchOption.AllDirectories)
                        .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                        .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                ];
            }

            var parent = Directory.GetParent(folder);

            if (parent == null)
                break;

            folder = parent.FullName;
        }

        return [];
    }
}
