using System.Globalization;

namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// The owner's frequently-used commands, as permanent tappable buttons in a topic.
///
/// They appear in TWO places at once, and that is the whole reason this class exists:
///   1. an INLINE keyboard hanging off the topic's status-line message — one tap sends a
///      callback_data payload back to the app, with no message in the chat;
///   2. a persistent REPLY keyboard sitting above the input box — one tap sends the button's TEXT
///      as an ordinary message, exactly as if the owner had typed it.
///
/// Two renderings and one tap handler is three places to forget a command, and the failure mode is
/// silent: a button that renders and does nothing, or worse, a bar above the keyboard offering a
/// verb the lexer no longer knows. So the set lives HERE, once, and both renderings plus the parser
/// are derived from <see cref="BUTTONS"/> — adding another command is one line in one array.
///
/// The two renderings carry different strings for the same command, and neither is decoration:
///   - the inline label is read by a HUMAN on a phone, so it gets an emoji and stays short;
///   - the reply-keyboard text is read by the app's COMMAND LEXER, because Telegram sends a reply
///     button's text verbatim as a message. It must therefore be exactly "/show", "/merge",
///     "/test", "/screen" — an emoji there would arrive as part of the message and the lexer would
///     not recognise the command.
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
/// registered in the single-use registry. They are permanent furniture: the owner presses /show a
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

    /// <summary>What Telegram's command lexer needs in front of the verb, on both sides.</summary>
    const string COMMAND_MARKER = "/";

    /// <summary>
    /// Two per row. On a phone a four-across row shrinks each button to a thumb-missing sliver,
    /// and one-per-row eats a whole row of screen per command above the input box; two-across is
    /// the shape that stays readable without pushing the conversation off the top.
    /// </summary>
    const int REPLY_KEYBOARD_COLUMNS = 2;

    /// <summary>
    /// THE source of truth. Display order is the order the owner reaches for them: look at it,
    /// land it, mark it for testing, photograph it.
    ///
    /// Every label is emoji-then-slash-command: the emoji is what the eye finds on a crowded
    /// screen, the slash command is what makes the button's EFFECT unambiguous — a picture alone
    /// leaves the owner guessing which of two similar glyphs merges and which closes.
    /// 🧪 for /test is not a free choice: it is the same glyph the topic's own status line uses for
    /// the awaiting-testing state, so the button and the state it toggles read as one thing.
    /// </summary>
    static readonly (string Command, string Label)[] BUTTONS =
    [
        // /screen LEADS, on the owner's call: "the /screen command is crucial, it should be among
        // the main commands always available" (2026-08-24). It was already in all three surfaces —
        // this bar, the reply keyboard and the "/" menu — but it sat last, in the second row of a
        // two-by-two, which is the least reachable of the four on a phone.
        ("screen", "📸 /screen"),
        ("show",   "👁 /show"),
        ("merge",  "🔀 /merge"),
        ("test",   "🧪 /test"),
        // /pc AND /close REPLACE /refresh on the owner's call, 2026-09-07: *"Let's remove the
        // /refresh button from the pulse message, an place the /close and /pc command as buttons
        // instead."* /refresh survives as a typed command and keeps its entry in Telegram's "/"
        // menu - only the standing button went, so nothing it repaired became unreachable.
        //
        // They share the last row because both act on the SESSION rather than on its work: the
        // rows above look at it (/screen, /show) and act on the code (/merge, /test).
        //
        // 💻 for /pc is no more a free choice than 🧪 is for /test: it is the glyph the topic
        // NAME already carries while terminal presence is on, so the button and the state it
        // toggles read as one thing. /close is the only button here that ENDS anything, and it is
        // also the only one whose tap does not act on its own - it parks a request the owner
        // confirms.
        ("pc",     "💻 /pc"),
        ("close",  "🏁 /close"),
        // /pause AND /progress are the fourth row, on the owner's call, 2026-09-09: *"we need to
        // add a button pause ... it should also become a new button under the pulse button, and
        // since we need two buttons per row, let's also add another command that you think is
        // useful."*
        //
        // 💤 for /pause is the glyph the topic NAME carries while it is paused, the same rule 🧪
        // and 💻 already follow. /progress is the partner because it is the question the owner
        // asks most, it is READ-ONLY, and the command already existed — so the button costs a
        // wiring line rather than a feature. It is deliberately not a second destructive button
        // next to /close.
        ("pause",    "💤 /pause"),
        ("progress", "📊 /progress"),
    ];

    /// <summary>The commands offered, in display order: "screen", "show", "merge", "test", "pc", "close", "pause", "progress".</summary>
    public static IReadOnlyList<string> Commands { get; } = BUTTONS.Select(button => button.Command).ToArray();

    /// <summary>
    /// Membership test for the parser. Ordinal and case-SENSITIVE: a payload we did not build is
    /// not ours, and quietly accepting "cmd:SHOW:5" would mean accepting whatever else invented it.
    /// </summary>
    static readonly HashSet<string> KNOWN_COMMANDS = new(BUTTONS.Select(button => button.Command), StringComparer.Ordinal);

    /// <summary>
    /// Precomputed because the rows never vary by topic — the reply keyboard is per CHAT, not per
    /// message, so there is nothing to substitute into it.
    /// </summary>
    static readonly IReadOnlyList<IReadOnlyList<string>> REPLY_KEYBOARD_ROWS =
        Commands
            .Select(command => COMMAND_MARKER + command)
            .Chunk(REPLY_KEYBOARD_COLUMNS)
            .Select(row => (IReadOnlyList<string>)row)
            .ToArray();

    /// <summary>
    /// Inline-keyboard buttons for one topic: (callback_data, label), in <see cref="Commands"/>
    /// order.
    ///
    /// The payload carries the TOPIC because a tap arrives with no text to infer one from, and this
    /// app's entire routing is per topic. 0 is the General topic, which has no thread id — the same
    /// convention the hold button and the receipt registry use, so all three agree on what "no
    /// topic" is keyed as.
    ///
    /// Telegram caps callback_data at 64 BYTES and rejects an over-long one at SEND time, on the
    /// phone, where nothing here can see it. The worst case is "cmd:screen:" plus a 20-character
    /// long — 31 bytes — so the cap is not close, but it is pinned by a test because a future
    /// longer verb is exactly the change that would sail past review.
    /// </summary>
    public static IReadOnlyList<(string Data, string Label)> Build_ForTopic(long messageThreadId)
    {
        var topic = messageThreadId.ToString(CultureInfo.InvariantCulture);

        return BUTTONS
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
    /// </summary>
    public static (string Command, long MessageThreadId)? Parse_OrNull(string? callbackData)
    {
        if (string.IsNullOrWhiteSpace(callbackData))
            return null;

        if (!callbackData.StartsWith(PREFIX, StringComparison.Ordinal))
            return null;

        var body = callbackData[PREFIX.Length..];

        var separator = body.IndexOf(FIELD_SEPARATOR);

        // -1 is "cmd:show" — a verb with no topic. 0 is "cmd::5" — a topic with no verb. Neither is
        // something Build_ForTopic can produce, so neither gets a benefit of the doubt.
        if (separator <= 0)
            return null;

        var command = body[..separator];

        if (!KNOWN_COMMANDS.Contains(command))
            return null;

        // AllowLeadingSign and nothing else: the default NumberStyles.Integer would also swallow
        // surrounding whitespace, and "cmd:show: 5" is not a payload this class ever wrote.
        if (!long.TryParse(body[(separator + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var messageThreadId))
            return null;

        return (command, messageThreadId);
    }

    /// <summary>
    /// Rows for the persistent reply keyboard. Each button's TEXT is what Telegram sends as an
    /// ordinary message, so these are exactly "/show", "/merge", "/test", "/screen" — no emoji, no
    /// padding, nothing decorative. Whatever is in this string lands in the chat as the owner's
    /// message and has to survive the command lexer intact.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> Build_ReplyKeyboardRows() => REPLY_KEYBOARD_ROWS;
}
