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
/// the defaults while an owner's own bar went unread. Widened, the guard found twenty-two menu verbs
/// with no case in the tap handler, and Task 5 could only refuse to draw them.
/// </para>
/// <para>
/// EVERY VERB HAS A ROUTE NOW (plan 03 Task 5b, ruling R16; owner, 2026-09-23: <i>"I absolutely need to
/// be able to choose which command and in which order to have the command buttons under the pulse
/// message"</i>). A tap reaches a verb one of two ways: a DEDICATED case in the tap handler, or its
/// default arm, which hands the verb to <c>Try_RunOwnerCommand_Async</c> — the SAME chain a typed
/// command is dispatched by. This guard walks the whole menu and fails naming the verb that has
/// neither, and still holds <see cref="TopicCommandButtons.Has_TapRoute"/> to that fact in both
/// directions, so the predicate plan 04's settings picker reads can never claim more than a tap runs.
/// </para>
/// </summary>
public class EveryTopicButtonIsWiredTests
{
    const string ENGINE_RELATIVE_PATH = "AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs";

    /// <summary>Where the tap handler starts — the one switch a tap is dispatched by.</summary>
    const string TAP_HANDLER_SIGNATURE = "Task<bool> Try_HandleTopicCommandTap_Async(";

    /// <summary>Where the typed-command chain starts — the dispatch a typed command and a fallback tap share.</summary>
    const string SHARED_DISPATCH_SIGNATURE = "Task<bool> Try_RunOwnerCommand_Async(";

    /// <summary>The call the tap handler's default arm makes — without it the shared chain serves typed text only.</summary>
    const string SHARED_DISPATCH_CALL = "await Try_RunOwnerCommand_Async(";

    /// <summary>
    /// EVERY VERB AN OWNER MAY PUT ON A BAR HAS A TAP ROUTE: a case in the tap handler, or the typed
    /// chain the default arm hands it to. The verb is matched WHOLE against that chain, because the
    /// fallback hands it the text "/verb" and the lexer returns everything after the slash — so
    /// "tail sup" is served only by its dedicated case, and the chain's <c>command is "tail"</c> does
    /// not count for it.
    /// </summary>
    [Fact]
    public void EveryConfigurableVerb_HasATapRoute()
    {
        var engineSource = Read_EngineSource();
        var tapHandler = Read_MethodSource(engineSource, TAP_HANDLER_SIGNATURE);
        var sharedDispatch = Read_MethodSource(engineSource, SHARED_DISPATCH_SIGNATURE);

        Assert.True(
            tapHandler.Contains(SHARED_DISPATCH_CALL, StringComparison.Ordinal),
            "the tap handler never hands a verb to the typed-command chain, so every verb without a dedicated case "
            + "answers 'from an older version of the app'.");

        foreach (var command in Every_ConfigurableCommand())
        {
            Assert.True(
                Has_TapCase(tapHandler, command) || Is_DispatchedByTheLexer(sharedDispatch, engineSource, command),
                $"'/{command}' can be put on a bar but no tap can run it: it has no case in the tap handler and the "
                + "typed-command chain the default arm falls back to does not dispatch it either.");
        }
    }

    /// <summary>
    /// THE OTHER DIRECTION, and the half that makes a configurable bar safe: the predicate the builders
    /// (and plan 04's settings picker) read says "drawable" exactly when a tap route exists — asked of the
    /// BUILDERS rather than of the predicate alone, because the predicate is only a claim about what they
    /// do. A bar ELEMENT with a target no tap carries ("tail 1") is still refused: the payload has no
    /// field for it, and the parser would not read it back.
    /// </summary>
    [Fact]
    public void TheDrawableSet_IsExactlyTheVerbsATapCanRun()
    {
        var engineSource = Read_EngineSource();
        var tapHandler = Read_MethodSource(engineSource, TAP_HANDLER_SIGNATURE);
        var sharedDispatch = Read_MethodSource(engineSource, SHARED_DISPATCH_SIGNATURE);

        foreach (var command in Every_ConfigurableCommand().Append("tail 1").Append("log sup"))
        {
            var routed = Has_TapCase(tapHandler, command) || Is_DispatchedByTheLexer(sharedDispatch, engineSource, command);

            Assert.True(
                routed == TopicCommandButtons.Has_TapRoute(command),
                routed
                    ? $"'/{command}' has a tap route but TopicCommandButtons refuses to draw it — a working button no bar can show."
                    : $"'/{command}' has NO tap route but TopicCommandButtons would draw it — a button that answers 'from an older version of the app'.");

            var expectedButtons = routed ? 1 : 0;

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
            // A MULTI-WORD BAR VERB IS CHECKED BY ITS FIRST WORD here — the branch meant to serve it. That
            // is presence, not proof it is reached: the lexer returns the WHOLE remainder ("tail sup"), and
            // `command is "tail"` does not match it, so a typed "/tail sup" currently goes to the session as
            // chat. NOTICED 2026-09-23 (plan 03 Task 5b) and parked; the TAPPED "tail sup" has its own case.
            var lexedVerb = command.Split(' ')[0];

            Assert.True(
                Is_DispatchedByTheLexer(engineSource, engineSource, lexedVerb),
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
    /// ONE METHOD'S OWN BODY, not the whole engine. With the guard walking every menu verb, a
    /// <c>case "status":</c> in any other switch of a 16 000-line file would count as wiring — and under
    /// the two-direction test above it would DEMAND the verb be drawn, putting a dead button on the
    /// owner's phone on the strength of an unrelated line. The method ends at the first closing brace
    /// back at member indentation. Fails loudly when it cannot be found, for the reason
    /// <see cref="Read_EngineSource"/> does.
    /// </summary>
    static string Read_MethodSource(string engineSource, string signature)
    {
        var start = engineSource.IndexOf(signature, StringComparison.Ordinal);

        if (start < 0)
            throw new Exception($"'{signature}' is not in the engine source — this guard would measure nothing, so it fails instead.");

        var end = Regex.Match(engineSource[start..], "\r?\n    }\r?\n");

        if (!end.Success)
            throw new Exception($"Found '{signature}' but not the end of its body — this guard would measure the rest of the file, so it fails instead.");

        return engineSource.Substring(start, end.Index + end.Length);
    }

    /// <summary>
    /// THE FIVE SHAPES THE LEXER CHAIN ACTUALLY USES, each matched as written in
    /// <paramref name="dispatchSource"/>. Walking the whole menu reached verbs dispatched without a plain
    /// equality — `command is "tail" or "log"`, `command.StartsWith("imp", …)` for a verb whose argument
    /// is glued on, and `Is_Command(command, MODEL_COMMAND)` for a verb that takes a value — and matching
    /// only the first shape would have reported those three commands broken when they are not. The
    /// CONSTANT is looked up in the whole engine, because it is declared outside any one method. The
    /// fifth is the delivery-mode toggles, whose list left the chain for <see cref="DeliveryModeCommands"/>
    /// (plan 03 Task 5b) so the DND lift could read it too: the verb must be in that list AND the chain
    /// must ask it.
    /// </summary>
    static bool Is_DispatchedByTheLexer(string dispatchSource, string engineSource, string verb)
    {
        if (dispatchSource.Contains($"command == \"{verb}\"", StringComparison.Ordinal))
            return true;

        foreach (Match pattern in Regex.Matches(dispatchSource, "command is (\"[a-z_]+\"(?: or \"[a-z_]+\")*)"))
        {
            if (pattern.Groups[1].Value.Split(" or ").Contains($"\"{verb}\""))
                return true;
        }

        if (dispatchSource.Contains($"command.StartsWith(\"{verb}\"", StringComparison.Ordinal))
            return true;

        if (DeliveryModeCommands.Is_ModeCommand(verb) && dispatchSource.Contains("DeliveryModeCommands.Is_ModeCommand(command)", StringComparison.Ordinal))
            return true;

        foreach (Match constant in Regex.Matches(engineSource, $"const string ([A-Z_]+) = \"{Regex.Escape(verb)}\";"))
        {
            if (dispatchSource.Contains($"Is_Command(command, {constant.Groups[1].Value})", StringComparison.Ordinal))
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
