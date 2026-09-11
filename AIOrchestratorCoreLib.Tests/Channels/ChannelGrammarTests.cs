using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Bridge.Decisions;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Tests.Kit;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// THE TWO COPIES ARE ONE FILE, and this is what keeps them so.
///
/// <para>
/// The grammar has two readers by construction — <c>ChannelGrammar</c> parses an embedded resource,
/// and <c>kit/bin/channel-append.sh</c> reads the kit file with <c>jq</c> — because the writer is
/// bash and the recognisers are .NET. Two readers of one file is the design; two FILES would be the
/// drift it exists to end, and nothing but a test can tell them apart.
/// </para>
/// </summary>
public class ChannelGrammarTests
{
    /// <summary>
    /// EMBEDDED == ON DISK, byte for byte. The embedded copy is what the app recognises with and the
    /// kit copy is what the tool writes with, so a divergence is precisely the `QUESTION:` versus
    /// `QUESTION` failure of 2026-09 — one side writing what the other does not accept — arriving by
    /// a new door.
    ///
    /// Compared with line endings normalised and nothing else: git may hand the working copy CRLF on
    /// one machine, and that is not a grammar difference. Any other byte is.
    /// </summary>
    [Fact]
    public void TheEmbeddedGrammarIsTheKitsGrammar()
    {
        var onDisk = KitRepoFiles.Find(ChannelGrammar.KIT_RELATIVE_PATH.Replace('/', Path.DirectorySeparatorChar));

        Assert.False(
            onDisk == null,
            $"could not locate {ChannelGrammar.KIT_RELATIVE_PATH} walking up from '{AppContext.BaseDirectory}' — "
            + "this test compares the shipped grammar with the embedded one, so a missing file means it compared nothing.");

        Assert.Equal(
            Normalise(File.ReadAllText(onDisk!)),
            Normalise(ChannelGrammar.Embedded_Json));
    }

    /// <summary>
    /// THE CEILINGS AGREE WITH THE CODE THAT ENFORCES THEM. Brief E2 gave the app one ceiling with
    /// one home in <c>Brevity_Policy</c>; the grammar carries them too, because the TOOL refuses
    /// before the write and cannot read a C# constant. Same rule, one moment earlier — and this
    /// asserts the two moments cannot disagree.
    ///
    /// It is an equality, not a copy: whichever number moves, this fails until both do.
    /// </summary>
    [Fact]
    public void TheGrammarsCeilingsAreTheAppsCeilings()
    {
        Assert.Equal(Brevity_Policy.MAX_LINES, ChannelGrammar.Max_Lines);
        Assert.Equal(Brevity_Policy.MAX_CHARACTERS, ChannelGrammar.Max_Characters);
        Assert.Equal(OwnerQuestion_Contract.MAXIMUM_OPTIONS, ChannelGrammar.Max_Options);
        Assert.Equal(OptionButtons_Layout.READABLE_LABEL_WIDTH, ChannelGrammar.Option_LabelWidth);
    }

    /// <summary>
    /// THE HEADER PATTERN IS THE PARSER'S PATTERN. `ChannelEntry_Parser` compiles its regex with a
    /// source generator, so it cannot take the string from the grammar at runtime — which means the
    /// two could drift, and a header the tool writes would stop being an entry the app can read. The
    /// pattern is asserted equal here instead, which is the cheapest thing that cannot silently rot.
    /// </summary>
    [Fact]
    public void TheGrammarsHeaderPatternIsTheOneTheParserCompiles()
    {
        Assert.Equal(ChannelEntry_Parser.HEADER_PATTERN, ChannelGrammar.Header_Pattern);
    }

    /// <summary>
    /// EVERY MARKER IS NON-EMPTY. An empty marker matches every line, so a grammar that answered ""
    /// would make each recogniser accept everything — the loudest possible failure, arriving as
    /// silence. ChannelGrammar throws rather than returning empty; this proves it for every key the
    /// app actually asks for.
    /// </summary>
    [Fact]
    public void EveryMarkerTheAppReadsIsPresentAndNotEmpty()
    {
        string[] markers =
        [
            ChannelGrammar.QUESTION, ChannelGrammar.OPTION, ChannelGrammar.RECOMMEND, ChannelGrammar.RISK,
            ChannelGrammar.ROW, ChannelGrammar.DEADLINE, ChannelGrammar.DEFAULT, ChannelGrammar.IMAGE,
            ChannelGrammar.ATTACH, ChannelGrammar.STATE, ChannelGrammar.BLOCKED_ON_OWNER,
            ChannelGrammar.ANSWERED, ChannelGrammar.STANDING_BY, ChannelGrammar.WRITING_WINDOW_OPEN,
            ChannelGrammar.WRITING_WINDOW_CLOSED, ChannelGrammar.MUTATION_WINDOW_OPEN,
            ChannelGrammar.MUTATION_WINDOW_CLOSED, ChannelGrammar.BOOT_ANNOUNCEMENT_WORD,
            ChannelGrammar.TO, ChannelGrammar.WORKTREE,
        ];

        Assert.All(markers, marker => Assert.False(string.IsNullOrWhiteSpace(marker)));

        // And they are DISTINCT, or two recognisers would fire on one line.
        Assert.Equal(markers.Length, markers.Distinct().Count());
    }

    /// <summary>
    /// A KEY THAT IS NOT THERE THROWS, and says what it is for. The alternative — an empty string —
    /// is a recogniser that matches every line, which is decision 20's harness one layer down: a
    /// reader that cannot find what it needs must refuse rather than invent.
    /// </summary>
    [Fact]
    public void AMarkerThatIsNotInTheGrammarThrows()
    {
        var thrown = Assert.Throws<Exception>(() => ChannelGrammar.Marker("no_such_marker"));

        Assert.Contains("markers.no_such_marker", thrown.Message);
    }

    /// <summary>
    /// THE BARE FORM IS DERIVED, NOT STORED — which is the whole lesson of the drift. `QUESTION:` and
    /// `QUESTION` were two constants in three files; now there is one, and the colonless form is
    /// computed from it, so they cannot come apart again.
    /// </summary>
    [Fact]
    public void TheBareFormOfAMarkerComesFromTheMarker()
    {
        Assert.Equal("QUESTION", ChannelGrammar.Bare(ChannelGrammar.QUESTION));
        Assert.Equal("OPTION", ChannelGrammar.Bare(ChannelGrammar.OPTION));

        // A marker with no colon is returned untouched, so callers need not know which kind they hold.
        Assert.Equal(ChannelGrammar.BLOCKED_ON_OWNER, ChannelGrammar.Bare(ChannelGrammar.BLOCKED_ON_OWNER));
    }

    /// <summary>Requirement 3's vocabulary: the types the tool may declare and the parsers may read.</summary>
    [Fact]
    public void TheTypesIncludeTheOnesTheStatePackAndTheDigestSelectBy()
    {
        Assert.Contains("brief", ChannelGrammar.Types);
        Assert.Contains("review", ChannelGrammar.Types);
        Assert.Contains("question", ChannelGrammar.Types);
        Assert.Contains("state", ChannelGrammar.Types);
        Assert.Contains("report", ChannelGrammar.Types);
    }

    static string Normalise(string json)
    {
        return json.Replace("\r\n", "\n").TrimEnd();
    }
}
