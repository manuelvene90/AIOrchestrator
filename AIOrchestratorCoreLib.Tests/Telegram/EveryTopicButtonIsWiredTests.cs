using System.Text.RegularExpressions;
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
///
/// <para>
/// EVERY VERB AN OWNER MAY CONFIGURE, SINCE 2026-09-23 (plan 03 Task 5). The bars became
/// <c>pulse.buttons</c> and <c>general.buttons</c>, so walking the two shipped lists would certify only
/// the defaults while an owner's own bar went unread. Widened, the guard found TWENTY-TWO menu verbs
/// with no case in the tap handler — classic's own /pause and /progress among them. Those two were
/// master's buttons (a2c9a3d, 2026-09-09) whose cases the fork merge dropped, and were re-wired so
/// classic's bar can be drawn whole; the other TWENTY stay typed commands only. The builder refuses to
/// draw a verb with no tap route (<see cref="TopicCommandButtons.Has_TapRoute"/>), and this guard holds
/// that set to the switch in BOTH directions, so wiring one later is a case there and a word in the
/// set, and forgetting either is red.
/// </para>
/// </summary>
public class EveryTopicButtonIsWiredTests
{
    const string ENGINE_RELATIVE_PATH = "AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs";

    /// <summary>Where the tap handler starts — the one switch a tap is dispatched by.</summary>
    const string TAP_HANDLER_SIGNATURE = "Task<bool> Try_HandleTopicCommandTap_Async(";

    /// <summary>
    /// The INLINE keyboard's tap handler. A tap carries callback_data and no text, so it never meets
    /// the command lexer — it is dispatched by this switch alone. Every verb the builder is willing to
    /// draw must have its case.
    /// </summary>
    [Fact]
    public void EveryButton_HasACaseInTheTapHandler()
    {
        var tapHandler = Read_TapHandlerSource(Read_EngineSource());

        foreach (var command in Every_ConfigurableCommand().Where(TopicCommandButtons.Has_TapRoute))
        {
            Assert.True(
                Has_TapCase(tapHandler, command),
                $"the '/{command}' button renders but has no case in the tap handler's switch, so tapping "
                + "it falls to the default arm and tells the owner their own button is from an older build.");
        }
    }

    /// <summary>
    /// THE OTHER DIRECTION, and the half that makes a configurable bar safe: a verb with no case is
    /// never drawn, on either bar, whatever the owner configured. Asked of the BUILDERS rather than of
    /// the set alone, because the set is only a claim about what they do.
    ///
    /// It also catches the opposite drift — a case added to the switch and not to the set, which would
    /// leave a working command permanently refused from every bar.
    /// </summary>
    [Fact]
    public void AVerbWithNoCaseInTheTapHandler_IsNeverDrawn_AndAVerbWithOneIsNeverRefused()
    {
        var tapHandler = Read_TapHandlerSource(Read_EngineSource());

        foreach (var command in Every_ConfigurableCommand())
        {
            var hasCase = Has_TapCase(tapHandler, command);

            Assert.True(
                hasCase == TopicCommandButtons.Has_TapRoute(command),
                hasCase
                    ? $"'/{command}' has a case in the tap handler but TopicCommandButtons refuses to draw it — a working button no bar can show."
                    : $"'/{command}' has NO case in the tap handler but TopicCommandButtons would draw it — a button that answers 'from an older version of the app'.");

            var expectedButtons = hasCase ? 1 : 0;

            Assert.Equal(expectedButtons, TopicCommandButtons.Build_ForTopic([command], 7L, isHolding: false, heldCount: 0, holdToggleOnTheBar: false).Count);
            Assert.Equal(expectedButtons, TopicCommandButtons.Build_ForGeneral([command], 0L).Count);
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

        foreach (var command in Every_ConfigurableCommand())
        {
            // A MULTI-WORD BAR VERB LEXES AS ITS FIRST WORD, and that is not a loophole — it is how a
            // typed command works. `Get_BotCommand_OrNull` reads the verb after the slash, so the
            // owner typing "/tail sup" arrives as command "tail" with "sup" still in the message
            // text, which the handler parses as its argument. Demanding the literal
            // `command == "tail sup"` here would only be satisfiable by dead code that can never be
            // true, and the branch that genuinely serves it would still be the one for "tail".
            var lexedVerb = command.Split(' ')[0];

            Assert.True(
                Is_DispatchedByTheLexer(engineSource, lexedVerb),
                $"'/{command}' can arrive as plain text (the \"/\" menu, or the owner typing it), but no "
                + $"branch dispatches the verb '{lexedVerb}' — "
                + "so the text is routed to the session as chat instead of running the command.");
        }
    }

    /// <summary>
    /// EVERY VERB AN OWNER MAY PUT ON A BAR: the whole "/" menu, plus "tail sup", the one verb that
    /// carries its target (a tap has no text to carry one in). This is the set
    /// <c>SettingValidators.BOT_COMMANDS</c> accepts the first word of.
    ///
    /// <para>
    /// IT USED TO BE THE TWO SHIPPED LISTS, and before that ONE of them — which is why this guard once
    /// reported everything wired while three General buttons were not (2026-09-09 to 2026-09-10). A
    /// guard that covers part of what can be drawn is decision 20's harness: it certifies the absence
    /// of what it never read. With the bars configurable, "what can be drawn" is everything the
    /// validator lets through.
    /// </para>
    /// </summary>
    static IEnumerable<string> Every_ConfigurableCommand()
    {
        return BotCommandMenu.ALL.Select(command => command.Command).Append("tail sup").Distinct();
    }

    static bool Has_TapCase(string tapHandlerSource, string command)
    {
        return tapHandlerSource.Contains($"case \"{command}\":", StringComparison.Ordinal);
    }

    /// <summary>
    /// THE TAP HANDLER'S OWN BODY, not the whole engine. With the guard walking every menu verb, a
    /// <c>case "status":</c> in any other switch of a 13 000-line file would count as wiring — and under
    /// the two-direction test above it would DEMAND the verb be drawn, putting a dead button on the
    /// owner's phone on the strength of an unrelated line. The method ends at the first closing brace
    /// back at member indentation. Fails loudly when it cannot be found, for the reason
    /// <see cref="Read_EngineSource"/> does.
    /// </summary>
    static string Read_TapHandlerSource(string engineSource)
    {
        var start = engineSource.IndexOf(TAP_HANDLER_SIGNATURE, StringComparison.Ordinal);

        if (start < 0)
            throw new Exception($"'{TAP_HANDLER_SIGNATURE}' is not in the engine source — this guard would measure nothing, so it fails instead.");

        var end = Regex.Match(engineSource[start..], "\r?\n    }\r?\n");

        if (!end.Success)
            throw new Exception($"Found '{TAP_HANDLER_SIGNATURE}' but not the end of its body — this guard would measure the rest of the file, so it fails instead.");

        return engineSource.Substring(start, end.Index + end.Length);
    }

    /// <summary>
    /// THE FOUR SHAPES THE LEXER CHAIN ACTUALLY USES, each matched as written in the engine. Walking the
    /// whole menu reached verbs dispatched without a plain equality — `command is "tail" or "log"`,
    /// `command.StartsWith("imp", …)` for a verb whose argument is glued on, and
    /// `Is_Command(command, MODEL_COMMAND)` for a verb that takes a value — and matching only the first
    /// shape would have reported those three commands broken when they are not.
    /// </summary>
    static bool Is_DispatchedByTheLexer(string engineSource, string verb)
    {
        if (engineSource.Contains($"command == \"{verb}\"", StringComparison.Ordinal))
            return true;

        foreach (Match pattern in Regex.Matches(engineSource, "command is (\"[a-z_]+\"(?: or \"[a-z_]+\")*)"))
        {
            if (pattern.Groups[1].Value.Split(" or ").Contains($"\"{verb}\""))
                return true;
        }

        if (engineSource.Contains($"command.StartsWith(\"{verb}\"", StringComparison.Ordinal))
            return true;

        foreach (Match constant in Regex.Matches(engineSource, $"const string ([A-Z_]+) = \"{Regex.Escape(verb)}\";"))
        {
            if (engineSource.Contains($"Is_Command(command, {constant.Groups[1].Value})", StringComparison.Ordinal))
                return true;
        }

        return false;
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
