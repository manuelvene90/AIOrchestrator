using AIOrchestratorCoreLib.Configuration.PhoneSettings;

namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// How a topic's outbound traffic is treated. The distinction is the whole point: DEFERRED keeps
/// everything and replays it later (the owner is away), SILENCED throws it away (the owner is
/// reading the same content live in the terminal and does not want it twice).
/// </summary>
public enum TelegramDeliveryModes
{
    /// <summary>Messages are texted as they happen.</summary>
    Normal,

    /// <summary>Do-Not-Disturb: nothing is texted and NOTHING IS LOST — it arrives on the next Normal tick.</summary>
    Deferred,

    /// <summary>Dropped outright while it lasts; the channel files remain the record.</summary>
    Silenced,
}

/// <summary>
/// THE TWO GLYPH NAMESPACES, and which surface each one belongs to — the owner's ruling of
/// 2026-09-10.
///
/// <para>
/// THE TOPIC NAME CARRIES WHAT IS ABOUT THE WORK OR ABOUT THE OWNER: ❓ (waiting on the owner),
/// ⏸ (paused for a usage limit), 🏁 (closed), plus the two the owner sets BY HAND — 🧪 (/test) and
/// ✅ (/done). Those five answer "what is the state of this endeavour", which is the question a
/// topic list is read to answer.
/// </para>
/// <para>
/// PULSE'S HEADER CARRIES EVERY MODE GLYPH: 🌙 🔕 ✈ 🤐 💻. These say how the app is DELIVERING right
/// now, and two of them — ✈ away and 🤐 quiet — are app-wide, so on a name they renamed every topic
/// at once. Every rename is an `editForumTopic` call and a service message in the topic, so a
/// machine-wide state change wrote a line into every one of the owner's threads to tell them
/// something they had just done themselves. In PULSE's header the same fact costs one silent edit of
/// a message that was being edited anyway.
/// </para>
/// <para>
/// THAT RULING IS THE SHIPPED DEFAULT, NOT THE ONLY WAY, since plan 03 (Task 7, the catalogue's
/// <c>topic.modeGlyphs</c>). Master drew the five on the NAME, where the topic list shows them without
/// opening anything, and classic — master's phone — states <see cref="ModeGlyphPlacements.Name"/>.
/// Either way they are drawn in EXACTLY ONE of the two places, by ONE implementation
/// (<see cref="Compose_ModeGlyphs"/>): the name draws them only under <c>name</c>, the header only
/// under <c>pulseHeader</c>.
/// </para>
/// <para>
/// ⛔ IS GONE, FOLDED INTO ❓. It was split from it on 2026-08-19 to distinguish "waiting on you" from
/// "waiting on you AND stopped", and the owner retired the distinction on 2026-09-10: for them the
/// two mean the same thing, which is that they have to do something. The constant stays only so
/// <see cref="Strip_Glyph"/> can still remove it from names decorated by an older build.
/// </para>
/// <para>
/// THE CONSTANTS ALL STAY HERE, on both sides of the move, so the two surfaces cannot come to
/// disagree about what a muted topic looks like.
/// </para>
/// </summary>
public static class TelegramDeliveryMode_Glyphs
{
    public const string DEFERRED = "🌙";
    public const string SILENCED = "🔕";

    /// <summary>
    /// FINISHED, BUT NOT YET TESTED BY THE OWNER — so do not close it. Their own workflow, made
    /// visible: they were muting a completed endeavour and then remembering, unaided, which of the
    /// muted ones still needed testing (2026-08-19).
    ///
    /// It REPLACES the silenced glyph rather than sitting beside it. /test IS mute — the delivery
    /// mode really is Silenced underneath — so drawing 🔕 🧪 together would state one fact twice,
    /// the same reasoning that makes TERMINAL replace the mode glyph rather than accompany it.
    /// </summary>
    public const string AWAITING_TEST = "🧪";

    /// <summary>
    /// FINISHED, AND DELIBERATELY NOT CLOSED. The last step of the owner's own workflow, asked for
    /// on 2026-08-21: *"a new /done command that works like test and mute, but with a different icon
    /// that lets me remember the topic is finished, but I still don't want to close the topic in
    /// case I have something else to do later."*
    ///
    /// 🧪 says "I have not checked this yet"; this says "I have, and it is done" — the topic stays
    /// open only as somewhere to come back to. Closing is the other thing, and it is destructive by
    /// comparison: it stops the tailers, kills the terminals and closes the Telegram topic.
    ///
    /// ✅ IS THE OWNER'S CHOICE over the 📦 first proposed, and their reason is the one that counts:
    /// *"for now it's used only in conversations, not in topic titles, so I won't get confused"*.
    /// The app does write ✅ in message BODIES — an answered question, a passed check — but the
    /// topic-list vocabulary is a separate namespace, and inside it this character is unused.
    /// Not 🏁 — which in 2026-08 meant a LEDGER RECAP and since 2026-09-10 means a CLOSED
    /// orchestration (<see cref="CLOSED"/>). The reason has outlived the constant it named: either
    /// way, 🏁 and ✅ would be two different finished-somethings in one thread. A line finishing is
    /// `LedgerTransition_Wording.FINISHED_GLYPH` ✔, and the recap now takes 🎯.
    ///
    /// It REPLACES the mode glyph for the same reason 🧪 does: /done is mute underneath, so drawing
    /// 🔕 ✅ together would state one fact twice.
    /// </summary>
    public const string DONE = "✅";

    /// <summary>
    /// SOMEBODY IS WAITING ON THE OWNER, and the endeavour is still moving meanwhile.
    /// </summary>
    public const string REPLY_WANTED = "❓";

    /// <summary>
    /// RETIRED 2026-09-10 — folded into <see cref="REPLY_WANTED"/> on the owner's ruling: *"⛔ is
    /// folded into ❓ (same meaning for the owner)"*.
    ///
    /// It was split off on 2026-08-19, when they asked to see from the topic list "if some topic
    /// needs me for a response, whether blocking or not". A year of using it answered the question:
    /// both states mean they have to do something, and which one it is does not change what they do
    /// next. The distinction still EXISTS in <see cref="OwnerReplyStates"/>, because it is a real
    /// difference the app acts on; it just no longer earns its own character in the topic list.
    ///
    /// THE CONSTANT STAYS so <see cref="Strip_Glyph"/> can remove it from a name an older build
    /// decorated. Dropping it would leave every currently-blocked topic wearing a ⛔ that no rename
    /// could ever take off.
    /// </summary>
    public const string REPLY_BLOCKING = "⛔";

    /// <summary>
    /// PAUSED — the owner paused this orchestration: outbound is HELD and the session is dormant
    /// until they lift it. Nothing is being delivered and nobody is working, which is why it wins
    /// the title over every glyph that describes a live topic.
    ///
    /// 💤 AND NOT ⏸, for two reasons. ⏸ is already the hold BUTTON's label, and one symbol meaning
    /// both "tap me" and "this is the state" is the conflation the topic-list vocabulary exists to
    /// avoid. And ⏸ is a SINGLE UTF-16 unit — the exact shape that breaks the hand-written
    /// <see cref="Leading_GlyphLength"/> table, where every emoji glyph here is TWO units: a glyph
    /// whose length is guessed wrong chops the wrong number of units off the name, silently. 💤 is
    /// a surrogate pair like the rest, so it measures like the rest.
    /// </summary>
    public const string PAUSED_BY_OWNER = "💤";

    /// <summary>
    /// PAUSED FOR A USAGE LIMIT — the endeavour has not stopped, it is waiting for a window to
    /// reset, and there is nothing for the owner to do but know.
    ///
    /// It is on the NAME rather than only in PULSE because it is the state most likely to be
    /// mistaken for a stall: a topic that has gone quiet reads as broken, and the false alerts of
    /// 2026-09-09 were exactly this state being reported as "waiting on your reply". One character
    /// in the topic list answers it without opening anything.
    ///
    /// The character is the one the app already uses for a hold (`⏸ Wait`, `⏸ DISPATCH PAUSED`), so
    /// paused means paused everywhere.
    /// </summary>
    public const string PAUSED_FOR_LIMIT = "⏸";

    /// <summary>
    /// CLOSED — the orchestration is over. Its topic is normally DELETED on close, so this is what
    /// the owner sees in the window between the close and a delete that has not happened yet, or
    /// will never happen: Telegram refuses to delete some topics, and a closed endeavour still
    /// wearing its working name is one the owner cannot tell from a live one.
    ///
    /// 🏁 KEEPS THIS MEANING and the ledger recap gave the character up for it (owner, 2026-09-10) —
    /// see <c>LedgerTransition_Wording.RECAP_GLYPH</c>. Two different finished-somethings sharing one
    /// symbol is the collision <see cref="DONE"/>'s own summary refused when it was chosen.
    /// </summary>
    public const string CLOSED = "🏁";

    /// <summary>
    /// Away mode's own glyph — app-wide. Deliberately NOT the moon: that already means Deferred,
    /// and two different states sharing a symbol in the topic list is worse than no symbol.
    /// </summary>
    public const string AWAY = "✈";

    /// <summary>
    /// STATUS SCREENSHOTS ARE ON — a delivery setting, so since 2026-09-10 it lives in the GENERAL
    /// DASHBOARD's header rather than in the General topic's name, which is the same move the five
    /// mode glyphs made off the orchestration topics' names.
    ///
    /// It was the one glyph point 2 missed: the rule said every mode glyph leaves the name, and this
    /// is a mode glyph on General's name, written by a second composition site that the rule never
    /// visited. The dashboard is General's PULSE — the one message the app already keeps current
    /// there — so its header is where this belongs.
    ///
    /// IT MOVED HOUSE FROM `BridgeEngineModel`, where it was a private const, because it now has
    /// three readers: the dashboard that draws it, the name sync that must no longer draw it, and
    /// <see cref="Strip_Glyph"/>, which has to be able to take it off a name an older build wrote.
    /// </summary>
    public const string STATUS_SCREENSHOTS = "📸";

    /// <summary>
    /// QUIET — this ONE orchestration has stopped asking after 3 unanswered messages. Per topic on
    /// purpose: the owner may be quiet here simply because they are working in another topic.
    /// </summary>
    public const string QUIET = "🤐";

    /// <summary>
    /// TERMINAL — the owner is in THIS orchestration's terminal, so nothing is pushed and nothing
    /// blocks on a tap. It REPLACES the mode glyph rather than sitting beside it: terminal already
    /// silences the topic, and drawing 💻 🔕 together would restate one fact twice on the title bar
    /// — the presence/delivery conflation this mode exists to remove, rendered.
    /// </summary>
    public const string TERMINAL = "💻";

    /// <summary>
    /// EVERYTHING THE TOPIC NAME SHOWS, as one named value.
    ///
    /// <para>
    /// A RECORD BECAUSE THE OLD SIGNATURE WAS A TRAP the compiler could not see. `Decorate_TopicName`
    /// took eight positional arguments of which four were `bool` — away, quiet, awaiting-test, done —
    /// and the one production call site passed them in a row. Any two of them could be swapped with
    /// everything still compiling, and the engine that calls it is `internal sealed`, so the suite
    /// could not see the swap either. That is the trap `TopicStatusFields` records for its own
    /// timestamps, in the class next door.
    /// </para>
    /// <para>
    /// FOUR OF THOSE EIGHT LEFT on 2026-09-10, when mode, away, quiet and presence moved to PULSE's
    /// header, and CAME BACK in plan 03 (Task 7): <c>topic.modeGlyphs = name</c> draws them here again,
    /// so the engine must hand them in. They are drawn only under that placement — under the shipped
    /// <c>pulseHeader</c> they are carried and ignored, which is the price of one record whichever the
    /// owner chose, and <see cref="Compose_TopicName"/>'s placement argument is what decides.
    /// </para>
    /// </summary>
    /// <param name="OwnerReply">
    /// Whether someone is waiting on the owner. Both non-None values draw ❓ — see
    /// <see cref="TelegramDeliveryMode_Glyphs.REPLY_BLOCKING"/> for why the second character retired.
    /// </param>
    /// <param name="IsPausedByOwner">
    /// /pause — the owner walked away from this endeavour without closing it. A DIFFERENT FACT from
    /// <paramref name="IsPausedForUsageLimit"/>, which is a limit the app detected: this one says
    /// nobody is working because the owner said so, that one says nobody is working because the
    /// account is full. They used to compete for one slot; two facts get two fields.
    /// </param>
    /// <param name="IsPausedForUsageLimit">The endeavour is waiting for a usage window, not stalled.</param>
    /// <param name="IsClosed">The orchestration is over and its topic has outlived it.</param>
    /// <param name="IsAwaitingTest">/test — the owner's own "finished, but I have not checked it".</param>
    /// <param name="IsDone">/done — the owner's own "I have checked it, leave the topic open".</param>
    /// <param name="Mode">The topic's EFFECTIVE delivery mode — 🌙 or 🔕, drawn only under <c>name</c>.</param>
    /// <param name="IsAway">Away mode, app-wide — ✈, drawn only under <c>name</c>.</param>
    /// <param name="IsQuiet">This orchestration stopped asking — 🤐, drawn only under <c>name</c>.</param>
    /// <param name="Presence">The owner in THIS orchestration's terminal — 💻, drawn only under <c>name</c>.</param>
    public readonly record struct TopicNameFlags(
        OwnerReplyStates OwnerReply = OwnerReplyStates.None,
        bool IsPausedByOwner = false,
        bool IsPausedForUsageLimit = false,
        bool IsClosed = false,
        bool IsAwaitingTest = false,
        bool IsDone = false,
        TelegramDeliveryModes Mode = TelegramDeliveryModes.Normal,
        bool IsAway = false,
        bool IsQuiet = false,
        OwnerPresenceModes Presence = OwnerPresenceModes.Remote);

    /// <summary>
    /// The topic's name as the owner reads it in their topic list: `❓ ✅ crm bug` — or, under
    /// <c>topic.modeGlyphs = name</c>, `❓ ✈ 🔕 ✅ crm bug`.
    ///
    /// <para>
    /// ❓ IS OUTERMOST, ahead of everything — the owner asked for it "at the beginning of the topic
    /// name, to concatenate with other possible icons". It is also the only glyph here that asks
    /// something OF them; the rest describe where the work stands.
    /// </para>
    /// <para>
    /// EXACTLY ONE STATE GLYPH FOLLOWS IT, never a row of them, and the order is most-final-first:
    /// 🏁 closed, then 💤 paused by the owner, then ✅ done, then 🧪 awaiting-test, then ⏸ paused for a
    /// usage limit. A closed endeavour is not also
    /// awaiting a test; a signed-off one is not also asking to be tested (the owner's own rule of
    /// 2026-08-21, kept); and a topic the owner has finished with does not need to say why the
    /// machine stopped. Each one REPLACES the ones below it for the same reason 💻 used to replace the
    /// mode glyph: stating one fact twice is what made this list too long to read.
    /// </para>
    /// <para>
    /// THE MODE GLYPHS GO BETWEEN THEM, and only under <see cref="ModeGlyphPlacements.Name"/>: ❓ stays
    /// outermost because it is the one that asks something of the owner, and master drew ✈ ahead of
    /// 🧪 and ✅, so the state glyph stays beside the name. What they say and in what precedence is
    /// <see cref="Compose_ModeGlyphs"/>'s, the same call PULSE's header makes — never a second copy.
    /// </para>
    /// </summary>
    /// <param name="modeGlyphs">
    /// <c>topic.modeGlyphs</c> as the engine resolved it at the point of effect — the engine always
    /// passes it. NULL IS THE CATALOGUE'S SHIPPED VALUE, READ FROM THE CATALOGUE, the convention
    /// <c>TopicStatusLine_Builder.Build</c> keeps for its own settings, rather than a member named here:
    /// a literal default would be a second copy of the catalogue's row (CLAUDE.md decision 12), and
    /// either value is somebody's right answer (classic states <c>name</c>, the shipped default is
    /// <c>pulseHeader</c>). A name composed under the wrong placement is a rename, and a service
    /// message, in every open topic — so the one production caller never leaves it to this default.
    /// </param>
    public static string Compose_TopicName(string baseName, TopicNameFlags flags, ModeGlyphPlacements? modeGlyphs = null)
    {
        var replyPrefix = flags.OwnerReply switch
        {
            // BOTH ASKING STATES DRAW THE SAME CHARACTER. The enum keeps the distinction because the
            // app acts on it; the topic list stopped spending a glyph on it (owner, 2026-09-10).
            OwnerReplyStates.Blocking => $"{REPLY_WANTED} ",
            OwnerReplyStates.Wanted => $"{REPLY_WANTED} ",
            OwnerReplyStates.None => "",
            _ => throw new Exception($"Unhandled OwnerReplyStates: {flags.OwnerReply}"),
        };

        var stateGlyph = flags switch
        {
            { IsClosed: true } => $"{CLOSED} ",

            // 💤 SITS BELOW 🏁 AND ABOVE THE TWO FINISHED MARKS. Closing is the end of an endeavour
            // and pause is not, so the chequered flag still leads; but ✅ and 🧪 both describe work
            // that is OVER, and this describes work that is merely asleep and expected back — a
            // topic the owner will come looking for. And it outranks ⏸ for the reason decision 15
            // gives: this is a state they chose and can lift, that one is one they can do nothing
            // about.
            { IsPausedByOwner: true } => $"{PAUSED_BY_OWNER} ",
            { IsDone: true } => $"{DONE} ",
            { IsAwaitingTest: true } => $"{AWAITING_TEST} ",
            { IsPausedForUsageLimit: true } => $"{PAUSED_FOR_LIMIT} ",
            _ => "",
        };

        var placement = modeGlyphs ?? PhoneSettings_Json.Parse(configRoot: null, presetTree: null).TopicModeGlyphs;

        // A DRAWN ✅ OR 🧪 REPLACES THE DELIVERY GLYPH — ruling R23 (fix round 1), restoring what
        // `DONE` and `AWAITING_TEST` above say and master drew: /done and /test ARE mute underneath,
        // so `🔕 ✅` states one fact twice. "Drawn" is the point: one outranked by 🏁 or 💤 replaces
        // nothing. 💤 does not replace either — its own summary claims the title's STATE slot, not
        // the delivery glyph.
        var stateReplacesDeliveryGlyph = stateGlyph == $"{DONE} " || stateGlyph == $"{AWAITING_TEST} ";

        var modePrefix = placement switch
        {
            // A CLOSED TOPIC DRAWS NO MODE GLYPH (fix round 1). The orchestration is over — nothing is
            // delivered in it and nobody sits at its terminal — and under `topic.onClose = close` the
            // topic stays in the list, where a delivery glyph would be renamed by every app-wide toggle
            // into a thread that has finished. Its final name is `🏁 name`, which the engine then stops
            // syncing (IOrchestrationSession.TelegramTopicFinalNameUtc).
            ModeGlyphPlacements.Name when flags.IsClosed => "",
            ModeGlyphPlacements.Name => Compose_ModeGlyphs(flags.Mode, flags.IsAway, flags.IsQuiet, flags.Presence, stateReplacesDeliveryGlyph),
            ModeGlyphPlacements.PulseHeader => "",
            _ => throw new Exception($"Unhandled ModeGlyphPlacements: {placement}"),
        };

        return $"{replyPrefix}{modePrefix}{stateGlyph}{baseName}";
    }

    /// <summary>
    /// THE FIVE MODE GLYPHS, COMPOSED ONCE — `✈ 💻 ` — for whichever surface <c>topic.modeGlyphs</c>
    /// puts them on: PULSE's header (<c>TopicStatusLine_Builder</c>) or the topic name
    /// (<see cref="Compose_TopicName"/>). Empty when there is nothing to say; otherwise every glyph is
    /// followed by one space, so a caller prepends it to whatever it decorates.
    ///
    /// <para>
    /// ONE IMPLEMENTATION FOR BOTH SURFACES (CLAUDE.md decision 12). The precedence below lived in
    /// `Build_HeaderLine` from 2026-09-10 until plan 03, and before that on the name; a second copy for
    /// the <c>name</c> placement would let the two drift, and the owner would see a topic say one thing
    /// in the list and another on its header the day they switched.
    /// </para>
    /// <para>
    /// THE PRECEDENCE IS THE TOPIC NAME'S, MOVED VERBATIM, because it was right and because changing
    /// it in the same commit as the move would make a behaviour change look like a relocation. AWAY
    /// SUPERSEDES QUIET: away already means every orchestration has stopped asking, so both together
    /// state one fact twice. TERMINAL REPLACES THE DELIVERY GLYPH: sitting in the terminal is what
    /// silences the topic, so 💻 🔕 says the same thing in two characters. Away still shows beside
    /// terminal — it is about the owner's PHONE, which is a different fact from where they are
    /// sitting for this one endeavour.
    /// </para>
    /// </summary>
    /// <param name="deliveryGlyphReplacedByState">
    /// The surface draws a state glyph that already says the delivery mode — the name's ✅ or 🧪, which
    /// are mute underneath (ruling R23) — so 🌙/🔕 is left off, exactly as 💻 leaves it off below. Only
    /// the delivery glyph: away, quiet and terminal are other facts. PULSE's header draws no state
    /// glyph and always passes false. REQUIRED, so neither caller can forget which surface it is.
    /// </param>
    public static string Compose_ModeGlyphs(TelegramDeliveryModes mode, bool isAway, bool isQuiet, OwnerPresenceModes presence, bool deliveryGlyphReplacedByState)
    {
        var presenceOrMode = presence == OwnerPresenceModes.Terminal
            ? $"{TERMINAL} "
            : deliveryGlyphReplacedByState
                ? ""
                : mode switch
                {
                    TelegramDeliveryModes.Normal => "",
                    TelegramDeliveryModes.Deferred => $"{DEFERRED} ",
                    TelegramDeliveryModes.Silenced => $"{SILENCED} ",
                    _ => throw new Exception($"Unhandled TelegramDeliveryModes: {mode}"),
                };

        var ownerAttention = (isAway, isQuiet) switch
        {
            (true, _) => $"{AWAY} ",
            (false, true) => $"{QUIET} ",
            _ => "",
        };

        return $"{ownerAttention}{presenceOrMode}";
    }

    /// <summary>
    /// Strips every leading state glyph, so a decorated name never gets decorated twice. Loops,
    /// because a name can carry more than one.
    ///
    /// <para>
    /// IT KNOWS EVERY GLYPH EITHER PLACEMENT CAN DRAW, and the ones nothing draws any more — 🌙 🔕 ✈ 🤐
    /// 💻 ⛔ 📸 — and that is the migration. Every topic in the owner's list was named by an earlier
    /// build, or under the other <c>topic.modeGlyphs</c> placement, so the first rename after a change
    /// has to be able to take a moon off a name nothing will put a moon on again. Narrowing this list to
    /// what the current placement draws would strand the old glyph on the name for as long as the topic
    /// lives.
    /// </para>
    /// </summary>
    public static string Strip_Glyph(string topicName)
    {
        var stripped = topicName.Trim();

        while (Starts_WithAnyGlyph(stripped))
            stripped = stripped[Leading_GlyphLength(stripped)..].Trim();

        return stripped;
    }

    static bool Starts_WithAnyGlyph(string topicName)
    {
        return topicName.StartsWith(DEFERRED, StringComparison.Ordinal)
            || topicName.StartsWith(DONE, StringComparison.Ordinal)
            || topicName.StartsWith(REPLY_WANTED, StringComparison.Ordinal)
            || topicName.StartsWith(REPLY_BLOCKING, StringComparison.Ordinal)
            || topicName.StartsWith(AWAITING_TEST, StringComparison.Ordinal)
            || topicName.StartsWith(SILENCED, StringComparison.Ordinal)
            || topicName.StartsWith(PAUSED_BY_OWNER, StringComparison.Ordinal)
            || topicName.StartsWith(PAUSED_FOR_LIMIT, StringComparison.Ordinal)
            || topicName.StartsWith(CLOSED, StringComparison.Ordinal)
            || topicName.StartsWith(STATUS_SCREENSHOTS, StringComparison.Ordinal)
            || topicName.StartsWith(AWAY, StringComparison.Ordinal)
            || topicName.StartsWith(QUIET, StringComparison.Ordinal)
            || topicName.StartsWith(TERMINAL, StringComparison.Ordinal);
    }

    /// <summary>Glyphs differ in UTF-16 length (✈ is one unit, the emoji are two) — measure, don't assume.</summary>
    static int Leading_GlyphLength(string topicName)
    {
        if (topicName.StartsWith(AWAY, StringComparison.Ordinal))
            return AWAY.Length;

        if (topicName.StartsWith(QUIET, StringComparison.Ordinal))
            return QUIET.Length;

        if (topicName.StartsWith(TERMINAL, StringComparison.Ordinal))
            return TERMINAL.Length;

        if (topicName.StartsWith(DONE, StringComparison.Ordinal))
            return DONE.Length;

        if (topicName.StartsWith(REPLY_WANTED, StringComparison.Ordinal))
            return REPLY_WANTED.Length;

        if (topicName.StartsWith(REPLY_BLOCKING, StringComparison.Ordinal))
            return REPLY_BLOCKING.Length;

        if (topicName.StartsWith(AWAITING_TEST, StringComparison.Ordinal))
            return AWAITING_TEST.Length;

        if (topicName.StartsWith(PAUSED_BY_OWNER, StringComparison.Ordinal))
            return PAUSED_BY_OWNER.Length;

        if (topicName.StartsWith(PAUSED_FOR_LIMIT, StringComparison.Ordinal))
            return PAUSED_FOR_LIMIT.Length;

        if (topicName.StartsWith(CLOSED, StringComparison.Ordinal))
            return CLOSED.Length;

        if (topicName.StartsWith(STATUS_SCREENSHOTS, StringComparison.Ordinal))
            return STATUS_SCREENSHOTS.Length;

        if (topicName.StartsWith(SILENCED, StringComparison.Ordinal))
            return SILENCED.Length;

        return DEFERRED.Length;
    }
}
