using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// A BUTTON THAT RENDERS AND DOES NOTHING is the failure <see cref="TopicCommandButtons"/>'s own doc
/// comment warns about — "two renderings and one tap handler is three places to forget a command,
/// and the failure mode is silent" — and nothing was checking the third place.
///
/// It bit immediately. /refresh was added to the array on 2026-08-25, rendered correctly in both
/// keyboards, and its INLINE tap fell to the switch's default arm: the owner would have tapped the
/// button they had just asked for and been told it came from an older version of the app.
///
/// The engine is `internal sealed` with no InternalsVisibleTo, so its switch cannot be called from
/// here. This reads the SOURCE instead. That is a blunt instrument and it is the right one: the
/// property is "these two files agree about a list of verbs", which is a fact about the text.
/// </summary>
public class EveryTopicButtonIsWiredTests
{
    const string ENGINE_RELATIVE_PATH = "AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs";

    /// <summary>
    /// The INLINE keyboard's tap handler. A tap carries callback_data and no text, so it never meets
    /// the command lexer — it is dispatched by this switch alone.
    /// </summary>
    [Fact]
    public void EveryButton_HasACaseInTheTapHandler()
    {
        var engineSource = Read_EngineSource();

        foreach (var command in Every_RenderedCommand())
        {
            Assert.True(
                engineSource.Contains($"case \"{command}\":", StringComparison.Ordinal),
                $"the '/{command}' button renders but has no case in the tap handler's switch, so tapping "
                + "it falls to the default arm and tells the owner their own button is from an older build.");
        }
    }

    /// <summary>
    /// The SAME verbs also arrive as ordinary TEXT — the "/" menu sends the literal "/show", and so
    /// does the owner typing it — so that route needs the command LEXER to know the verb. Same list,
    /// different mechanism, and either one missing leaves half the command working, which is worse
    /// than none of it because it works when tested one way.
    /// </summary>
    [Fact]
    public void EveryButton_IsKnownToTheCommandLexer()
    {
        var engineSource = Read_EngineSource();

        foreach (var command in Every_RenderedCommand())
        {
            // A MULTI-WORD BAR VERB LEXES AS ITS FIRST WORD, and that is not a loophole — it is how a
            // typed command works. `Get_BotCommand_OrNull` reads the verb after the slash, so the
            // owner typing "/tail sup" arrives as command "tail" with "sup" still in the message
            // text, which the handler parses as its argument. Demanding the literal
            // `command == "tail sup"` here would only be satisfiable by dead code that can never be
            // true, and the branch that genuinely serves it would still be the one for "tail".
            var lexedVerb = command.Split(' ')[0];

            Assert.True(
                engineSource.Contains($"command == \"{lexedVerb}\"", StringComparison.Ordinal)
                || engineSource.Contains($"command is \"{lexedVerb}\"", StringComparison.Ordinal),
                $"'/{command}' can arrive as plain text (the \"/\" menu, or the owner typing it), but no "
                + $"branch dispatches the verb '{lexedVerb}' — "
                + "so the text is routed to the session as chat instead of running the command.");
        }
    }

    /// <summary>
    /// BOTH BARS, and the omission of the second one is why this guard reported everything wired
    /// while three buttons were not.
    ///
    /// <para>
    /// The guard walked <see cref="TopicCommandButtons.Commands"/> only. `GeneralCommands` — the
    /// General topic's own bar — was never walked, so `/summary`, `/resume` and `/dnd_all` sat with
    /// no case in the switch and a green suite above them from 2026-09-09 to 2026-09-10. The bar had
    /// also never been rendered, which is what hid it: nobody could tap a button that was not drawn,
    /// so the dead arm cost nothing until the bar landed. A guard that covers one of two lists is
    /// decision 20's harness again — it certified the absence of what it never read.
    /// </para>
    /// <para>
    /// CONCATENATED RATHER THAN A SECOND PAIR OF TESTS. The property is the same property for both
    /// bars, and two copies of it would be the next thing to fall out of step when a third bar
    /// appears.
    /// </para>
    /// </summary>
    static IEnumerable<string> Every_RenderedCommand()
    {
        return TopicCommandButtons.Commands.Concat(TopicCommandButtons.GeneralCommands).Distinct();
    }

    /// <summary>
    /// THE GUARD ON THE GUARD. Returns the source or FAILS — a harness that cannot find what it tests
    /// must refuse to run rather than certify the absence of the thing it never read.
    /// </summary>
    static string Read_EngineSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, ENGINE_RELATIVE_PATH.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            directory = directory.Parent;
        }

        throw new Exception(
            $"Could not locate '{ENGINE_RELATIVE_PATH}' walking up from '{AppContext.BaseDirectory}'. "
            + "This harness reads the engine's SOURCE, so a missing file means it measured nothing — "
            + "failing rather than reporting every button wired.");
    }
}
