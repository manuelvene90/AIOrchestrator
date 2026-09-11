using System.Globalization;

namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// The owner's frequently-used commands, as permanent tappable buttons on a topic's status line.
///
/// They render as an INLINE keyboard hanging off that message — one tap sends a callback_data
/// payload back to the app, leaving no message in the chat. The set lives HERE, once, and both the
/// rendering and the parser derive from <see cref="TOPIC_BUTTONS"/> and
/// <see cref="GENERAL_BUTTONS"/>, so adding a command is one line in one array and cannot leave a
/// button that renders and does nothing.
///
/// TWO BARS SINCE 2026-09-09, because the two kinds of topic answer different questions. An
/// orchestration topic is about ONE endeavour — what is waiting, what is left, what is it doing,
/// merge it, end it. The General topic is about ALL of them, so it gets the cross-cutting five
/// (/summary, /pending, /limits, /resume, /dnd_all) and none of the per-orchestration ones, which
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
    /// THE source of truth for an ORCHESTRATION topic's bar, in the owner's own display order
    /// (2026-09-09): *"[⏳ /pending] [📋 /left] / [👀 /tail sup] [📉 /limits] / [🔀 /merge]
    /// [🏁 /close]"*. Two per row, so the rows read as pairs: what is owed, what is happening, what
    /// to do about it.
    ///
    /// FOUR BUTTONS LEFT THIS BAR AND ALL FOUR SURVIVE AS TYPED COMMANDS — /screen, /show, /pc and
    /// /test. The owner's reason is the bar's purpose: it is the six things they reach for from a
    /// phone while an endeavour runs, and looking at a Windows desktop is not one of them when they
    /// are not at it. Nothing became unreachable: each keeps its entry in Telegram's "/" menu, which
    /// is the same trade /refresh took when it lost its button on 2026-09-07.
    ///
    /// Every label is emoji-then-slash-command: the emoji is what the eye finds on a crowded screen,
    /// the slash command is what makes the button's EFFECT unambiguous — a picture alone leaves the
    /// owner guessing which of two similar glyphs merges and which closes.
    /// ⏳ for /pending is not a free choice: it is the same glyph PULSE's own "waiting on you" field
    /// uses, so the button and the field it expands read as one thing.
    /// </summary>
    static readonly (string Command, string Label)[] TOPIC_BUTTONS =
    [
        ("pending",  "⏳ /pending"),
        ("left",     "📋 /left"),

        // "tail sup" IS THE VERB, SPACE INCLUDED, and that is a decision worth stating: /tail takes a
        // target ("/tail 1", "/tail sup") and a tap arrives with no text to carry one in. The payload
        // has exactly two fields — verb and topic — so the target rides inside the verb, and the tap
        // handler dispatches the whole string. The alternative was a third payload field, which would
        // have changed the shape every other button already round-trips through.
        ("tail sup", "👀 /tail sup"),
        ("limits",   "📉 /limits"),

        // The last row acts on the WORK and then ends it, which is why these two share a row: /merge
        // is the one that lands an endeavour and /close is the only button here that ENDS anything.
        // /close's tap does not act on its own either — it parks a request the owner confirms, so a
        // mistap cannot end an orchestration.
        ("merge",    "🔀 /merge"),
        ("close",    "🏁 /close"),
    ];

    /// <summary>
    /// THE source of truth for the GENERAL topic's bar (owner, 2026-09-09): /summary, /pending,
    /// /limits, /resume, /dnd_all.
    ///
    /// All five are cross-cutting on purpose — General has no session of its own, so a /merge or a
    /// /close there would have nothing to act on and a /tail nothing to read. These are the five
    /// questions the owner asks ABOUT the whole machine: what happened everywhere, who wants me,
    /// how close am I to a limit, wake everything up, and silence everything.
    ///
    /// 🌙 for /dnd_all is <see cref="TelegramDeliveryMode_Glyphs.DEFERRED"/>'s own character — the
    /// glyph the topics themselves wear while deferred, so the button and the state it puts them all
    /// into read as one thing.
    /// </summary>
    static readonly (string Command, string Label)[] GENERAL_BUTTONS =
    [
        ("summary", "📊 /summary"),
        ("pending", "⏳ /pending"),
        ("limits",  "📉 /limits"),
        ("resume",  "▶ /resume"),
        ("dnd_all", "🌙 /dnd_all"),
    ];

    /// <summary>
    /// The commands offered on an ORCHESTRATION topic, in display order: "pending", "left",
    /// "tail sup", "limits", "merge", "close".
    /// </summary>
    public static IReadOnlyList<string> Commands { get; } = TOPIC_BUTTONS.Select(button => button.Command).ToArray();

    /// <summary>
    /// The commands offered on the GENERAL topic, in display order: "summary", "pending", "limits",
    /// "resume", "dnd_all".
    ///
    /// A SEPARATE PROPERTY rather than an addition to <see cref="Commands"/>, and the distinction is
    /// load-bearing: EveryTopicButtonIsWiredTests walks <see cref="Commands"/> to prove each button
    /// has a case in the tap handler, and folding the two lists together would make that test demand
    /// one list of wiring for two different bars.
    /// </summary>
    public static IReadOnlyList<string> GeneralCommands { get; } = GENERAL_BUTTONS.Select(button => button.Command).ToArray();

    /// <summary>
    /// Membership test for the parser — BOTH bars, because a tap from either has to parse. Ordinal
    /// and case-SENSITIVE: a payload we did not build is not ours, and quietly accepting
    /// "cmd:SUMMARY:5" would mean accepting whatever else invented it.
    /// </summary>
    static readonly HashSet<string> KNOWN_COMMANDS = new(
        TOPIC_BUTTONS.Select(button => button.Command).Concat(GENERAL_BUTTONS.Select(button => button.Command)),
        StringComparer.Ordinal);

    /// <summary>
    /// Inline-keyboard buttons for one orchestration topic: (callback_data, label), in
    /// <see cref="Commands"/> order.
    ///
    /// The payload carries the TOPIC because a tap arrives with no text to infer one from, and this
    /// app's entire routing is per topic. 0 is the General topic, which has no thread id — the same
    /// convention the hold button and the receipt registry use, so all three agree on what "no
    /// topic" is keyed as.
    ///
    /// Telegram caps callback_data at 64 BYTES and rejects an over-long one at SEND time, on the
    /// phone, where nothing here can see it. The worst case is "cmd:tail sup:" plus a 20-character
    /// long — 33 bytes — so the cap is not close, but it is pinned by a test because a future
    /// longer verb is exactly the change that would sail past review.
    /// </summary>
    public static IReadOnlyList<(string Data, string Label)> Build_ForTopic(long messageThreadId)
    {
        return Build(TOPIC_BUTTONS, messageThreadId);
    }

    /// <summary>
    /// The bar plus THE HOLD TOGGLE — brief D, and the toggle is here because its old home is gone.
    ///
    /// <para>
    /// ⏸ Wait used to ride the ✓ receipt, which landed under whatever the owner had just sent:
    /// "clicking a button is faster than typing wait", and it was exactly where they were looking.
    /// Brief D replaces that receipt with a reaction, so there is no message to hang it on any
    /// more. It moves to the one message that is always present — PULSE — and NOT to a second home
    /// as well: one toggle in two places is CLAUDE.md decision 12, and the owner ruled the same way
    /// (2026-09-10). Typed WAIT/GO stay as the shortcut.
    /// </para>
    /// <para>
    /// THE LABEL CARRIES THE COUNT, which is what makes the bar's own repost-on-change rule bring
    /// it back under the owner's messages precisely while they are holding — the moment the button
    /// is worth reaching for. It is the same payload family the receipt used
    /// (<see cref="HoldButton_Data"/>), so the tap handler needs nothing new; only the label and
    /// which of the two actions it offers change with the state.
    /// </para>
    /// </summary>
    public static IReadOnlyList<(string Data, string Label)> Build_ForTopic(long messageThreadId, bool isHolding, int heldCount)
    {
        var toggle = isHolding
            ? (HoldButton_Data.Build(HoldButtonActions.Go, messageThreadId), Describe_ReleaseLabel(heldCount))
            : (HoldButton_Data.Build(HoldButtonActions.Hold, messageThreadId), HoldButton_Data.HOLD_LABEL);

        return [.. Build(TOPIC_BUTTONS, messageThreadId), toggle];
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
    /// Inline-keyboard buttons for the GENERAL topic, in <see cref="GeneralCommands"/> order.
    ///
    /// It takes the thread id rather than assuming 0, for the same reason
    /// <see cref="Build_ForTopic"/> does: which id General is keyed as belongs to the caller that
    /// holds the roster, and a builder that hard-coded one would be a second opinion on it.
    /// </summary>
    public static IReadOnlyList<(string Data, string Label)> Build_ForGeneral(long messageThreadId)
    {
        return Build(GENERAL_BUTTONS, messageThreadId);
    }

    static IReadOnlyList<(string Data, string Label)> Build((string Command, string Label)[] buttons, long messageThreadId)
    {
        var topic = messageThreadId.ToString(CultureInfo.InvariantCulture);

        return buttons
            .Select(button => ($"{PREFIX}{button.Command}{FIELD_SEPARATOR}{topic}", button.Label))
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
