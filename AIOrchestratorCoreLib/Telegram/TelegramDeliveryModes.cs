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
    /// FOUR OF THOSE EIGHT ARE GONE rather than carried unused: mode, away, quiet and presence moved
    /// to PULSE's header on 2026-09-10, and keeping them here as ignored parameters would leave every
    /// caller still able to ask for a glyph this surface no longer draws.
    /// </para>
    /// </summary>
    /// <param name="OwnerReply">
    /// Whether someone is waiting on the owner. Both non-None values draw ❓ — see
    /// <see cref="TelegramDeliveryMode_Glyphs.REPLY_BLOCKING"/> for why the second character retired.
    /// </param>
    /// <param name="IsPausedForUsageLimit">The endeavour is waiting for a usage window, not stalled.</param>
    /// <param name="IsClosed">The orchestration is over and its topic has outlived it.</param>
    /// <param name="IsAwaitingTest">/test — the owner's own "finished, but I have not checked it".</param>
    /// <param name="IsDone">/done — the owner's own "I have checked it, leave the topic open".</param>
    public readonly record struct TopicNameFlags(
        OwnerReplyStates OwnerReply = OwnerReplyStates.None,
        bool IsPausedForUsageLimit = false,
        bool IsClosed = false,
        bool IsAwaitingTest = false,
        bool IsDone = false);

    /// <summary>
    /// The topic's name as the owner reads it in their topic list: `❓ ✅ crm bug`.
    ///
    /// <para>
    /// ❓ IS OUTERMOST, ahead of everything — the owner asked for it "at the beginning of the topic
    /// name, to concatenate with other possible icons". It is also the only glyph here that asks
    /// something OF them; the rest describe where the work stands.
    /// </para>
    /// <para>
    /// EXACTLY ONE STATE GLYPH FOLLOWS IT, never a row of them, and the order is most-final-first:
    /// 🏁 closed, then ✅ done, then 🧪 awaiting-test, then ⏸ paused. A closed endeavour is not also
    /// awaiting a test; a signed-off one is not also asking to be tested (the owner's own rule of
    /// 2026-08-21, kept); and a topic the owner has finished with does not need to say why the
    /// machine stopped. Each one REPLACES the ones below it for the same reason 💻 used to replace the
    /// mode glyph: stating one fact twice is what made this list too long to read.
    /// </para>
    /// </summary>
    public static string Compose_TopicName(string baseName, TopicNameFlags flags)
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
            { IsDone: true } => $"{DONE} ",
            { IsAwaitingTest: true } => $"{AWAITING_TEST} ",
            { IsPausedForUsageLimit: true } => $"{PAUSED_FOR_LIMIT} ",
            _ => "",
        };

        return $"{replyPrefix}{stateGlyph}{baseName}";
    }

    /// <summary>
    /// Strips every leading state glyph, so a decorated name never gets decorated twice. Loops,
    /// because a name can carry more than one.
    ///
    /// <para>
    /// IT STILL KNOWS THE GLYPHS THIS SURFACE NO LONGER DRAWS — 🌙 🔕 ✈ 🤐 💻 ⛔ — and that is the
    /// migration. Every topic in the owner's list was named by the build before this one, so the
    /// first rename after the change has to be able to take a moon off a name nothing will ever put
    /// a moon on again. Narrowing this list to what is currently drawn would strand the old glyph on
    /// the name for as long as the topic lives.
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
