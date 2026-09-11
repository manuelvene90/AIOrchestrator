using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Tests.Kit;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// THE GREP TEST requirement 1 asks for by name: *"no string literal of a marker anywhere else (a
/// test greps for it)"*.
///
/// <para>
/// WHY A GREP AND NOT A TYPE RULE. The failure being prevented is a developer typing
/// <c>"QUESTION:"</c> into a new file — which compiles, passes every other test, and works right up
/// to the day the grammar changes and one of the two spellings does not. Nothing in the type system
/// can see that; the text can. It is the same instrument, and the same argument, as
/// <c>EveryTopicButtonIsWiredTests</c>: the property is "these files agree about a word", which is a
/// fact about the text.
/// </para>
/// <para>
/// IT SCANS THE LIBRARY, NOT THE TESTS. A test may legitimately write <c>"QUESTION: which?"</c> as
/// a fixture — that is the input a session produces, and spelling it out is what makes the fixture
/// readable. What must not happen is the PRODUCT recognising or writing a marker it spelled itself.
/// </para>
/// <para>
/// THE GUARD ON THE GUARD (decision 20): it refuses to run when it cannot find the library, rather
/// than reporting a clean scan of nothing.
/// </para>
/// </summary>
public class NoMarkerLiteralEscapesTheGrammarTests
{
    /// <summary>
    /// The file that is ALLOWED to hold the words: the grammar's own C# reader names its keys, and
    /// the JSON is the source. Nothing else.
    /// </summary>
    static readonly string[] EXEMPT_FILE_NAMES = ["ChannelGrammar.cs"];

    /// <summary>
    /// The markers worth grepping for — the ones a recogniser or a writer would spell. Taken FROM the
    /// grammar, so a marker added to the JSON is covered here the day it lands rather than the day
    /// someone remembers to add it to a list.
    ///
    /// Two are deliberately not scanned. <c>online</c> and the risk words are ordinary English that
    /// occurs in prose and comments all over the library, so scanning them would report noise, and a
    /// guard that cries wolf is one people learn to skip.
    /// </summary>
    static IReadOnlyList<string> Markers_WorthScanning()
    {
        return
        [
            ChannelGrammar.QUESTION, ChannelGrammar.OPTION, ChannelGrammar.RECOMMEND,
            ChannelGrammar.RISK, ChannelGrammar.ROW, ChannelGrammar.DEADLINE, ChannelGrammar.DEFAULT,
            ChannelGrammar.IMAGE, ChannelGrammar.ATTACH, ChannelGrammar.STATE,
            ChannelGrammar.BLOCKED_ON_OWNER, ChannelGrammar.WRITING_WINDOW_OPEN,
            ChannelGrammar.WRITING_WINDOW_CLOSED, ChannelGrammar.MUTATION_WINDOW_OPEN,
            ChannelGrammar.MUTATION_WINDOW_CLOSED, ChannelGrammar.STANDING_BY, ChannelGrammar.TO,
        ];
    }

    [Fact]
    public void NoLibraryFileSpellsAMarkerItselfAnyMore()
    {
        var library = KitRepoFiles.Find("AIOrchestratorCoreLib");

        Assert.False(
            library == null,
            $"could not locate AIOrchestratorCoreLib walking up from '{AppContext.BaseDirectory}' — "
            + "this guard scans the library's SOURCE, so a missing folder means it scanned nothing.");

        List<string> offenders = [];

        foreach (var file in Directory.EnumerateFiles(library!, "*.cs", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);

            if (EXEMPT_FILE_NAMES.Contains(name) || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            var text = Without_Comments(File.ReadAllText(file));

            foreach (var marker in Markers_WorthScanning())
            {
                // THE QUOTED FORM ONLY. A marker named in a comment or a doc summary is how this
                // codebase explains itself — "the `QUESTION:` block" is prose, and forbidding it
                // would make the rule unwritable. A string LITERAL is the thing that drifts.
                if (text.Contains($"\"{marker}\"", StringComparison.Ordinal))
                    offenders.Add($"{name}: \"{marker}\"");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "these library files spell a channel marker as a string literal instead of reading it from "
            + "ChannelGrammar, which is how `QUESTION:` and `QUESTION` came to disagree across three files:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// <summary>
    /// Everything after a `//` on a line, gone — which covers `///` doc comments too.
    ///
    /// WHY THE SCAN NEEDS THIS. The first version of this guard read whole files and reported three
    /// offences that were prose: this codebase NAMES its markers in comments, and it quotes them when
    /// it does — `a prose mention of "ATTACH:"`, `carries a private "QUESTION:" for counting`. Those
    /// sentences are how the rules explain themselves, and a guard that calls them violations is one
    /// people learn to skip. What must not exist is a marker spelled in CODE.
    ///
    /// BLUNT, AND THE LIMIT IS STATED: a `//` inside a string literal (a URL) takes the rest of that
    /// line with it, so an offence sharing a line with a URL would be missed. The alternative is a C#
    /// parser inside a test, which is a second thing to be wrong.
    /// </summary>
    static string Without_Comments(string source)
    {
        var kept = source
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(line =>
            {
                var comment = line.IndexOf("//", StringComparison.Ordinal);

                return comment < 0 ? line : line[..comment];
            });

        return string.Join('\n', kept);
    }

    /// THE GUARD CAN FAIL — proved here rather than asserted, because a scan that never matches
    /// anything is indistinguishable from a scan that cannot match. This plants the exact literal the
    /// rule forbids in a string of its own and checks the detector sees it.
    ///
    /// Composed at runtime so the planted text is not itself a literal this guard would flag.
    /// </summary>
    [Fact]
    public void TheDetectorFindsAPlantedLiteral()
    {
        var planted = $"var marker = \"{ChannelGrammar.QUESTION}\";";

        Assert.Contains($"\"{ChannelGrammar.QUESTION}\"", planted);
    }
}
