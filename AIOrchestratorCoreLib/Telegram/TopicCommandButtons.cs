using System.Globalization;

namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// The owner's frequently-used commands, as permanent tappable buttons on a topic's status line.
///
/// They render as an INLINE keyboard hanging off that message — one tap sends a callback_data
/// payload back to the app, leaving no message in the chat.
///
/// THE BARS ARE SETTINGS SINCE 2026-09-23 (plan 03 Task 5): <c>pulse.buttons</c> for an orchestration
/// topic, <c>general.buttons</c> for General, each a list of verbs resolved catalogue → preset →
/// config.json and handed to the builders below by the engine at the point of effect. This class
/// no longer decides WHICH buttons a bar carries; it decides what a verb's button IS (its payload,
/// through <see cref="CommandButton_Labels"/> its label), whether a tap can run it at all, how a
/// tap parses back, and — for a verb with no dedicated tap case — the text the tap is dispatched as
/// (<see cref="Build_TypedCommandText"/>, ruling R16). <see cref="Commands"/> and <see cref="GeneralCommands"/> stay as the SHIPPED lists
/// the settings catalogue reads its defaults from.
///
/// TWO BARS SINCE 2026-09-09, because the two kinds of topic answer different questions. An
/// orchestration topic is about ONE endeavour — what is waiting, what is left, what is it doing,
/// merge it, end it. The General topic is about ALL of them, so its shipped bar is the cross-cutting
/// five (/summary, /pending, /limits, /resume, /dnd_all) and none of the per-orchestration ones, which
/// would have no session to act on there.
///
/// THERE WAS A SECOND RENDERING, AND IT WAS REMOVED ON 2026-09-06: a persistent REPLY keyboard, the
/// bar of literal slash commands above the input box, installed by sending a carrier message and
/// deleting it again. IT NEVER WORKED. Telegram anchors a reply keyboard to the message that
/// delivered it, so deleting that message takes the bar with it — measured on the owner's phone,
/// send then delete, bar appears then vanishes on the same chat. The bar had only ever been SEEN
/// when the process was killed between the send and the delete, leaving the carrier alive by
/// accident; in normal operation the feature spent every launch sending a message, buzzing the
/// owner's phone, and ending with the chat exactly as it started. Keeping it working would have
/// cost a permanent junk line in General, for five commands already one tap away here and listed
/// in the "/" menu besides. Do not reintroduce it without re-measuring that premise first.
///
/// Callback data is prefixed "cmd:" so a tap cannot be confused with the other button families
/// already flying around this bridge: "hold:"/"go:" (<see cref="HoldButton_Data"/>),
/// "close-yes-{guid}"/"close-no-{guid}" (the close confirmation), and "opt-{n}" (the single-use
/// option registry). <see cref="Parse_OrNull"/> returns null for every one of those, because a tap
/// this class cannot read must fall THROUGH to the next handler untouched. Swallowing it as a
/// malformed command would eat the owner's answer to a question — the one tap in this system that
/// cannot be repeated.
///
/// Like the hold toggle, and unlike the question options, these buttons are deliberately NOT
/// registered in the single-use registry. They are permanent furniture: the owner presses /pending a
/// dozen times across a session, and expiring the button after the first press would leave dead
/// furniture sitting exactly where they were told to press.
/// </summary>
public static class TopicCommandButtons
{
    /// <summary>
    /// What marks a payload as ours. Distinct from "hold:", "go:", "close-yes-", "close-no-" and
    /// "opt-", and not a prefix of any of them — so the parse ORDER in the tap handler cannot
    /// decide the meaning of a payload.
    /// </summary>
    const string PREFIX = "cmd:";

    /// <summary>Splits the verb from the topic inside the payload body.</summary>
    const char FIELD_SEPARATOR = ':';

    /// <summary>
    /// The SHIPPED bar of an ORCHESTRATION topic — <c>pulse.buttons</c>' catalogue default — in the
    /// owner's own display order (2026-09-09): *"[⏳ /pending] [📋 /left] / [👀 /tail sup] [📉 /limits]
    /// / [🔀 /merge] [🏁 /close]"*. Two per row, so the rows read as pairs: what is owed, what is
    /// happening, what to do about it.
    ///
    /// FOUR BUTTONS LEFT THIS BAR AND ALL FOUR SURVIVE AS TYPED COMMANDS — /screen, /show, /pc and
    /// /test. The owner's reason is the bar's purpose: it is the six things they reach for from a
    /// phone while an endeavour runs, and looking at a Windows desktop is not one of them when they
    /// are not at it. Nothing became unreachable: each keeps its entry in Telegram's "/" menu, which
    /// is the same trade /refresh took when it lost its button on 2026-09-07. (Classic, master's
    /// preset, puts all four back — which is exactly what a setting is for.)
    ///
    /// "tail sup" IS THE VERB, SPACE INCLUDED, and that is a decision worth stating: /tail takes a
    /// target ("/tail 1", "/tail sup") and a tap arrives with no text to carry one in. The payload has
    /// exactly two fields — verb and topic — so the target rides inside the verb, and the tap handler
    /// dispatches the whole string. The alternative was a third payload field, which would have changed
    /// the shape every other button already round-trips through.
    ///
    /// The last row acts on the WORK and then ends it: /merge is the one that lands an endeavour and
    /// /close is the only button here that ENDS anything. /close's tap does not act on its own either —
    /// it parks a request the owner confirms, so a mistap cannot end an orchestration.
    /// </summary>
    public static IReadOnlyList<string> Commands { get; } = ["pending", "left", "tail sup", "limits", "merge", "close"];

    /// <summary>
    /// The SHIPPED bar of the GENERAL topic — <c>general.buttons</c>' catalogue default (owner,
    /// 2026-09-09): /summary, /pending, /limits, /resume, /dnd_all.
    ///
    /// All five are cross-cutting on purpose — General has no session of its own, so a /merge or a
    /// /close there would have nothing to act on and a /tail nothing to read. These are the five
    /// questions the owner asks ABOUT the whole machine: what happened everywhere, who wants me,
    /// how close am I to a limit, wake everything up, and silence everything.
    ///
    /// A SEPARATE PROPERTY rather than an addition to <see cref="Commands"/>: they are the defaults of
    /// two different settings, for two different bars.
    /// </summary>
    public static IReadOnlyList<string> GeneralCommands { get; } = ["summary", "pending", "limits", "resume", "dnd_all"];

    /// <summary>
    /// Membership test for the parser: IS THIS A COMMAND AT ALL — every verb of the "/" menu, the two
    /// shipped bars, and "tail sup". NOT "is this on a bar", which is what it used to be (the verbs the
    /// two private arrays named).
    ///
    /// <para>
    /// WHY IT WIDENED (plan 03 Task 5). Once <c>pulse.buttons</c> was configurable, a set derived from
    /// the shipped arrays meant a tap on a perfectly legal configured button parsed to null, fell through
    /// every handler, and NOTHING HAPPENED WITH NOTHING LOGGED — the exact failure
    /// <see cref="Parse_OrNull"/> exists to refuse. A payload for a real command now parses, and the tap
    /// handler, which knows which verbs it can run, answers one it cannot out loud.
    /// </para>
    /// <para>
    /// Ordinal and case-SENSITIVE: a payload we did not build is not ours, and quietly accepting
    /// "cmd:SUMMARY:5" would mean accepting whatever else invented it.
    /// </para>
    /// </summary>
    static readonly HashSet<string> KNOWN_COMMANDS = new(
        BotCommandMenu.ALL.Select(command => command.Command).Concat(Commands).Concat(GeneralCommands).Append("tail sup"),
        StringComparer.Ordinal);

    /// <summary>
    /// WHETHER A TAP ON THIS VERB'S BUTTON RUNS ANYTHING — the builders draw nothing else, and plan 04's
    /// settings picker reads this to offer the drawable set. True for EVERY verb of the "/" menu and for
    /// "tail sup" (plan 03 Task 5b, ruling R16).
    ///
    /// <para>
    /// IT USED TO BE A HAND-KEPT SET OF SIXTEEN, "exactly the cases of the tap handler", and Task 5's
    /// widened wiring guard found the other twenty menu verbs refused from every bar — so the owner could
    /// order their bar but not choose what was on it (2026-09-23: <i>"I absolutely need to be able to
    /// choose which command and in which order"</i>). A tap on a verb with no dedicated case now goes to
    /// the engine's default arm, which dispatches <see cref="Build_TypedCommandText"/> through the same
    /// chain the typed message takes, so every command the menu offers is a command a tap can run.
    /// EveryTopicButtonIsWiredTests proves that per verb against the engine's source, in both directions.
    /// </para>
    /// <para>
    /// WHAT IS STILL REFUSED is a bar ELEMENT whose target no payload can carry — "tail 1", "log sup".
    /// The validator accepts them (their first word is a menu verb), but a payload has two fields, verb
    /// and topic, and only "tail sup" rides inside its verb; drawn, the tap would not parse back and would
    /// do nothing with nothing logged. So the refusal and its one log line
    /// (<see cref="Describe_VerbsWithoutTapRoute_OrNull"/>) stay, for those.
    /// </para>
    /// </summary>
    public static bool Has_TapRoute(string verb)
    {
        return KNOWN_COMMANDS.Contains(verb);
    }

    /// <summary>
    /// THE TEXT A TAP IS DISPATCHED AS when its verb has no dedicated case: exactly what the owner would
    /// have typed — "/cost", "/model" — so the engine lexes it with the same lexer and runs it through the
    /// same chain, and a button can never behave differently from the command it names (ruling R16). The
    /// topic is not in the text because it is not in a typed command either: it is the thread the message
    /// (here, the button) sat in.
    /// </summary>
    public static string Build_TypedCommandText(string verb)
    {
        return "/" + verb;
    }

    /// <summary>
    /// Whether this payload is a bar tap on a DELIVERY-MODE toggle (🌙 /dnd, 🔕 /mute, and their app-wide
    /// pair). The inbound loop lifts app-wide DND for anything the owner taps — the rule reads "the owner
    /// texting or tapping ANYTHING (except a mode command)", and the exception was never applied to taps:
    /// a tapped 🌙 /dnd_all while everything was deferred was lifted by its own batch and then toggled
    /// straight back ON, so that button could never turn DND off, while the typed /dnd_all could
    /// (found 2026-09-23 making every command tappable, plan 03 Task 5b). Null and foreign payloads are
    /// false — they are the owner speaking, which is what the lift is for.
    /// </summary>
    public static bool Is_DeliveryModeTap(string? callbackData)
    {
        return Parse_OrNull(callbackData) is { } parsed && DeliveryModeCommands.Is_ModeCommand(parsed.Command);
    }

    /// <summary>
    /// Inline-keyboard buttons for one orchestration topic: (callback_data, label) for each configured
    /// verb a tap can run (every menu verb, and "tail sup"), in the configured order, then THE HOLD TOGGLE when
    /// <paramref name="holdToggleOnTheBar"/> puts it here.
    ///
    /// <para>
    /// The payload carries the TOPIC because a tap arrives with no text to infer one from, and this
    /// app's entire routing is per topic. 0 is the General topic, which has no thread id — the same
    /// convention the hold button and the receipt registry use, so all three agree on what "no topic"
    /// is keyed as.
    /// </para>
    /// <para>
    /// THE TOGGLE'S PLACE IS ONE VALUE, decided by the caller (<c>pulse.holdToggle</c>, with D10's
    /// fallback — <see cref="HoldTogglePlacement_Resolver"/>) and handed to BOTH homes: this bar and the
    /// receipt (<see cref="ReceiptButtons_Builder"/>). One toggle in two places is CLAUDE.md decision 12,
    /// and the owner ruled the same way (2026-09-10). ⏸ Wait rode the ✓ receipt in master, which landed
    /// exactly where the owner was looking; brief D replaced that receipt with a reaction and moved the
    /// toggle to the one message always present — PULSE. Both phones are real, so the place is a setting.
    /// </para>
    /// <para>
    /// THE TOGGLE'S LABEL CARRIES THE COUNT, which is what makes the bar's own repost-on-change rule
    /// bring it back under the owner's messages precisely while they are holding — the moment the
    /// button is worth reaching for. It is the same payload family the receipt uses
    /// (<see cref="HoldButton_Data"/>), so the tap handler needs nothing new.
    /// </para>
    /// <para>
    /// Telegram caps callback_data at 64 BYTES and rejects an over-long one at SEND time, on the
    /// phone, where nothing here can see it. The worst case is "cmd:" plus the longest verb the parser
    /// accepts plus a 20-character long — 39 bytes for "organize_mains" — pinned by
    /// ConfigurableCommandButtonsTests, because a configurable list puts that ceiling in the owner's hands.
    /// </para>
    /// </summary>
    public static IReadOnlyList<(string Data, string Label)> Build_ForTopic(
        IReadOnlyList<string> verbs, long messageThreadId, bool isHolding, int heldCount, bool holdToggleOnTheBar)
    {
        var buttons = Build(verbs, messageThreadId);

        if (!holdToggleOnTheBar)
            return buttons;

        var toggle = isHolding
            ? (HoldButton_Data.Build(HoldButtonActions.Go, messageThreadId), Describe_ReleaseLabel(heldCount))
            : (HoldButton_Data.Build(HoldButtonActions.Hold, messageThreadId), HoldButton_Data.HOLD_LABEL);

        return [.. buttons, toggle];
    }

    /// <summary>
    /// "▶ GO" while nothing is queued, "⏸ 3 held · ▶ GO" once something is — the owner's own
    /// wording (2026-09-09). The count is the only thing on the bar that tells them the hold is
    /// actually catching messages rather than merely switched on.
    /// </summary>
    public static string Describe_ReleaseLabel(int heldCount)
    {
        return heldCount <= 0
            ? HoldButton_Data.GO_LABEL
            : $"⏸ {heldCount.ToString(CultureInfo.InvariantCulture)} held · {HoldButton_Data.GO_LABEL}";
    }

    /// <summary>
    /// Inline-keyboard buttons for the GENERAL topic: each configured verb a tap can run, in the
    /// configured order.
    ///
    /// <para>
    /// AN EMPTY LIST IS AN EMPTY RESULT, and that means NO BAR (D4): classic's <c>general.buttons</c> is
    /// empty, and the client sends a message with no buttons as one with no <c>reply_markup</c> at all,
    /// because Telegram will not take an empty <c>inline_keyboard</c>.
    /// </para>
    /// <para>
    /// It takes the thread id rather than assuming 0, for the same reason <see cref="Build_ForTopic"/>
    /// does: which id General is keyed as belongs to the caller that holds the roster, and a builder that
    /// hard-coded one would be a second opinion on it.
    /// </para>
    /// </summary>
    public static IReadOnlyList<(string Data, string Label)> Build_ForGeneral(IReadOnlyList<string> verbs, long messageThreadId)
    {
        return Build(verbs, messageThreadId);
    }

    /// <summary>
    /// The one line the engine logs for a bar that names elements no tap can run, or null when every one
    /// has a route. Since plan 03 Task 5b every menu verb has one, so what this still names is an element
    /// whose target no payload carries ("tail 1"). Plain words and the setting's own key, because the reader is the owner scanning
    /// orchestrator.log.jsonl for why a button they configured is missing — never a Telegram message
    /// (decision 15: they cannot act on it from the phone).
    /// </summary>
    public static string? Describe_VerbsWithoutTapRoute_OrNull(string settingPath, IReadOnlyList<string> verbs)
    {
        var refused = verbs.Where(verb => !Has_TapRoute(verb)).ToArray();

        if (refused.Length == 0)
            return null;

        var named = string.Join(", ", refused.Select(verb => $"'{verb}'"));
        var pronoun = refused.Length == 1 ? "it" : "them";

        return $"{settingPath} names {named} — no tap can run {pronoun}, so {(refused.Length == 1 ? "it is" : "they are")} left off the bar (type the command instead)";
    }

    static IReadOnlyList<(string Data, string Label)> Build(IReadOnlyList<string> verbs, long messageThreadId)
    {
        var topic = messageThreadId.ToString(CultureInfo.InvariantCulture);

        return verbs
            .Where(Has_TapRoute)
            .Select(verb => ($"{PREFIX}{verb}{FIELD_SEPARATOR}{topic}", CommandButton_Labels.For(verb)))
            .ToArray();
    }

    /// <summary>
    /// Null when the data is not ours, so the caller falls through to the next handler.
    ///
    /// The thread id is carried back VERBATIM, including negative values. Telegram's real thread ids
    /// are positive, so a negative one can only come from something that is not this app — but
    /// rejecting it here would be the wrong lie: null means "not ours, try the next handler", and
    /// every other handler will not recognise it either, so the owner taps and NOTHING happens with
    /// nothing logged. Handing the router a payload that is plainly ours, with the id it actually
    /// contains, lets the one component that holds the topic roster say so out loud. Deciding
    /// whether a topic exists was never this parser's job; its job is the shape.
    ///
    /// The parameter is nullable although the contract writes it plain — a tap payload arrives from
    /// the wire, and a parser whose whole purpose is "return null for anything unexpected" must not
    /// be the thing that throws on the most ordinary unexpected value there is.
    ///
    /// A VERB MAY CONTAIN A SPACE ("tail sup"), which this shape allows without a change: the body is
    /// split at the FIRST colon, so anything but a colon is legal inside a verb. What is still
    /// refused is whitespace in the TOPIC field, because Build never wrote one.
    /// </summary>
    public static (string Command, long MessageThreadId)? Parse_OrNull(string? callbackData)
    {
        if (string.IsNullOrWhiteSpace(callbackData))
            return null;

        if (!callbackData.StartsWith(PREFIX, StringComparison.Ordinal))
            return null;

        var body = callbackData[PREFIX.Length..];

        var separator = body.IndexOf(FIELD_SEPARATOR);

        // -1 is "cmd:pending" — a verb with no topic. 0 is "cmd::5" — a topic with no verb. Neither
        // is something Build can produce, so neither gets a benefit of the doubt.
        if (separator <= 0)
            return null;

        var command = body[..separator];

        if (!KNOWN_COMMANDS.Contains(command))
            return null;

        // AllowLeadingSign and nothing else: the default NumberStyles.Integer would also swallow
        // surrounding whitespace, and "cmd:pending: 5" is not a payload this class ever wrote.
        if (!long.TryParse(body[(separator + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var messageThreadId))
            return null;

        return (command, messageThreadId);
    }
}
