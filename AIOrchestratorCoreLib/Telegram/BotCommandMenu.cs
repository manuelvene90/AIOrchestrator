namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// THE NATIVE COMMAND MENU — every command the bot offers, in the order the owner meets them.
///
/// <para>
/// OUT OF <c>BridgeEngineModel</c> (brief F2). It was thirty-two tuples and their reasoning inline
/// in a 13 000-line file, which is where the repo's own rule says a piece goes when a stage touches
/// it — and there is a second reason here: <c>setMyCommands</c> PRESERVES ORDER, so this list IS
/// the menu the owner scrolls, and a list nothing can assert is a piece of user interface with no
/// test. The engine now names it and sends it.
/// </para>
/// <para>
/// ORDERED BY USE, not by theme. It grew in themed clusters — every counting command together,
/// every mode command together — which reads well written down and badly on a phone, where the
/// menu is a scrolling list and the owner wants <c>/pending</c> and <c>/left</c> without reaching
/// for them. The order is the owner's own (2026-09-09).
/// </para>
/// <para>
/// THE HOST-GATED ONES ARE LAST, which is why the tail reads like an afterthought: window focus,
/// screenshots and tiling only do anything on a Windows desktop, and on the VPS they answer "not
/// available on this host yet" (brief B's host capability). A command that cannot work where the
/// bridge actually runs must not sit above one that always can.
/// </para>
/// <para>
/// <see cref="HOST_GATED"/> NAMES EXACTLY FOUR, and an earlier version of it named six — it also
/// swept in <c>/pc</c> and <c>/screens</c>, which sit in the same tail but are ordinary toggles
/// that run to completion anywhere. That was a SECOND COPY of a fact the engine owns, wrong on the
/// day it was written (CLAUDE.md decision 12), and it made this docstring assert something false
/// about two commands. The tail is "least used"; only part of it is "cannot work here".
/// </para>
/// <para>
/// ENGLISH, because these are strings the APP writes (owner, 2026-09-09: "if the strings are
/// hardcoded, English; the rule holds"). What the ROLES write to the owner follows the owner's
/// language; this does not.
/// </para>
/// <para>
/// EVERY DESCRIPTION AND EVERY COMMENT BELOW IS CARRIED VERBATIM from the engine. Several of them
/// record a specific incident where the menu text was WRONG in one scope and right in another —
/// the menu is what the owner reads BEFORE tapping, so a correction after the fact is not a fix.
/// Reorganised, never rewritten.
/// </para>
/// </summary>
public static class BotCommandMenu
{
    /// <summary>
    /// The commands the engine REFUSES when the host has no windowing — last in the menu, by rule.
    ///
    /// <para>
    /// A SECOND READING OF A FACT THE ENGINE OWNS, and it is here under protest: the authority is
    /// <c>BridgeEngineModel.Refuse_IfNoWindowing_Async</c>, whose four call sites are <c>/show</c>,
    /// <c>/screen</c>, <c>/organize</c> and <c>/organize_mains</c>. The engine is
    /// <c>internal sealed</c>, so a test cannot ask it; naming the gate and its call sites here is
    /// the breadcrumb that a fifth call site has to update this list too.
    /// </para>
    /// <para>
    /// <c>/pc</c> and <c>/screens</c> are DELIBERATELY ABSENT. They live in the same tail because
    /// they are rarely used, not because they fail: both are toggles that run to completion on the
    /// VPS. Listing them here was the first version of this constant and it was simply untrue.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> HOST_GATED =
    [
        "screen",
        "show",
        "organize",
        "organize_mains",
    ];

    /// <summary>
    /// The menu, in send order. <c>setMyCommands</c> preserves it, so this is what the owner sees.
    /// </summary>
    public static readonly IReadOnlyList<(string Command, string Description)> ALL =
    [
        ("pending", "Open questions awaiting me"),
        ("left", "Only what is still open — no done or dropped rows"),
        // NOT "what's LEFT" any more, since 2026-08-13: in a topic the command prints the
        // whole ledger, done and dropped rows included. Same class as the kit line that
        // told supervisors it would shorten a long ledger for them — text promising the
        // old behaviour, in the one place the owner reads BEFORE running the command.
        //
        // BOTH SCOPES, and the first attempt at this string got that wrong. "every row"
        // is true in a topic and false in General, where Build_ProgressReportText emits
        // one counts line per open orchestration and no rows at all. The phrasing it
        // replaced — "what's LEFT to do" — happened to be true in both, because a count
        // IS an answer to what is left. A correction has to be checked in every scope the
        // thing it corrects runs in, or it is the same defect with a newer date.
        ("progress", "This topic's task ledger, every row — in General, one line per orchestration"),
        ("tail", "What a headless session is doing right now (/tail 1, /tail sup)"),
        ("limits", "5-hour and weekly usage limits"),
        ("cost", "What this topic has cost, per session — in General, per orchestration"),
        ("merge", "Land this orchestration's work: merge, test, push, then clean up"),
        ("close", "End THIS orchestration — you confirm with a tap"),
        ("dnd", "Toggle 🌙 this topic — hold its messages for later (in General: everywhere)"),
        // "THIS topic" WAS A LIE IN GENERAL, and this is the worst instance of the class
        // the two entries above were fixed for: in General the BARE command takes the
        // app-wide path (`Apply_ModeCommand_Async` — `session == null` routes to
        // `Apply_AppWideMode_Async`, as that method's own docstring already said). So an
        // owner reading "THIS topic — drop its messages" in the pinned General topic and
        // tapping /mute silences EVERY orchestration — and Silenced DROPS rather than
        // defers, so traffic from every session is destroyed until they notice. The reply
        // does say "everywhere", but a correction after the fact is exactly what the
        // /progress fix rejected as sufficient: the menu is what they read BEFORE tapping.
        ("mute", "Toggle 🔕 this topic — drop its messages (in General: everywhere)"),
        ("summary", "What is going on across all orchestrations"),
        ("resume", "Wake EVERY session — use when the usage limit resets"),
        ("status", "What every session of this orchestration is doing"),
        ("tasks", "The FULL ledger of this orchestration, done lines included"),
        ("tokens", "Token and usage totals"),
        // Was "listed next to /limits because the owner reaches for them together". The owner's
        // reorder (2026-09-09) separates them — /limits is high in the list, this is not — so the
        // sentence no longer describes the file it sits in. The DISTINCTION it drew is worth
        // keeping: /limits is how full the ACCOUNT is, this is how full each SESSION's window is.
        ("context", "How full each session's context window is"),
        ("diff", "What the repo and worktrees ACTUALLY contain"),
        ("imp", "Latest traffic of an implementer (/imp 2)"),
        ("log", "The whole of a headless session's last turn (/log 1, /log sup)"),
        ("test", "Toggle 🧪 — finished, muted, and still to be tested before closing"),
        ("done", "Toggle ✅ — finished, muted, and kept open in case you come back"),
        ("refresh", "Re-sync this topic's NAME — use when a ❓ or a glyph is stuck on it"),
        ("switch", "Turn this into a full crew, or back into one session — send twice"),
        ("clear", "Wipe THIS topic's messages (the sessions keep running)"),
        ("pc", "Toggle 💻 THIS topic — I'm at its terminal, don't text or block"),
        ("dnd_all", "Toggle 🌙 everywhere"),
        ("mute_all", "Toggle 🔕 everywhere"),
        ("screens", "Toggle 📸 — the half-hourly status carries a picture of the terminal"),
        ("screen", "Photograph this orchestration's terminal and send it here"),
        ("show", "Bring this orchestration's session window to the front"),
        ("organize", "Tile this orchestration's terminals across the screen"),
        ("organize_mains", "Tile EVERY orchestration's main terminal — each sup and solo, once"),
    ];
}
