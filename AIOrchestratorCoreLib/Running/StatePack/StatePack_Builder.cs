using System.Text;
using AIOrchestratorCoreLib.Channels.ChannelEntry;

namespace AIOrchestratorCoreLib.Running.StatePack;

/// <summary>
/// The text of `pack.md` — what a FRESH session reads first instead of its channel. Pure: inputs in,
/// markdown out, so a test can pin every section without a file system.
///
/// <para>
/// WHY A FILE AND NOT STDIN. Measured 2026-09-09 (claude 2.1.263): text on stdin is APPENDED to the
/// positional prompt in the same message, so a slash command's <c>$ARGUMENTS</c> swallows it —
/// `solo` and `communicator` interpolate <c>$ARGUMENTS</c> into paths verbatim. A file leaves the
/// role command alone as the whole positional prompt, costs the same tokens (one Read), and can be
/// re-read mid-turn.
/// </para>
/// <para>
/// ORDER: stable material first, the trigger last. Identity and contract never change; the brief
/// and the last report change once per task; the pending entries change every turn. Each section
/// has its own cap and says so when it truncates, pointing at the entry to read for the rest —
/// EXCEPT the pending entries, which are the reason the turn exists and are never cut.
/// </para>
/// </summary>
public static class StatePack_Builder
{
    public const int BRIEF_CAP = 8_000;
    public const int LAST_OWN_CAP = 6_000;
    public const int LEDGER_CAP = 6_000;
    public const int PLAN_CAP = 24_000;
    public const int GIT_CAP = 3_000;
    public const int OWNER_TAIL_CAP = 8_000;
    public const int PROGRESS_CAP = 6_000;

    /// <summary>
    /// The conclusions section's budget. Generous next to C1.2's ≤ 2 KB because that cap is on the
    /// BLOCK a single turn writes, while this file ACCUMULATES: a dead end ruled out in week one is
    /// still true in week four, and is by then the thing nothing else on disk remembers.
    /// </summary>
    public const int CONCLUSIONS_CAP = 8_000;

    /// <summary>
    /// The standing notes' budget IN CHARACTERS, on top of the ceiling of five notes the screen
    /// already applies (<see cref="StatePackInputs_Reader.Select_StandingNotes"/>). Two ceilings and
    /// not one because they bound different things: five bounds how many notes a busy hour can put in
    /// front of a session, this bounds how long any one of them can be — an orphan report names every
    /// silent member, and nothing caps what a note's body says. The pack has a ≤ 6k-token budget and
    /// bookkeeping is the one section in it that grows with the traffic, so it is the one that may
    /// not be left open-ended. Over the cap the section SAYS SO, as every other section does.
    /// </summary>
    public const int STANDING_NOTES_CAP = 4_000;

    /// <summary>
    /// Says in the heading what <c>PrintTurnPrompt_Builder.AGENT_NOTES_LINE</c> says for the riding
    /// notes, and for the same reason: a session that mistakes an app note for the owner's words
    /// answers a nudge on the owner's phone.
    /// </summary>
    public const string STANDING_NOTES_HEADING = "## What the app has told you (not the owner's words — act on them, do not answer them)";

    public const string PROGRESS_HEADING = "## Your progress note — progress.md, what you saved while working (resume from here)";

    public const string CONCLUSIONS_HEADING =
        "## What you concluded earlier — state.md, YOUR OWN judgements (not facts the bridge read)";

    public const string TITLE_PREFIX = "# State pack — ";
    public const string OPENING =
        "You are a FRESH session on an existing channel: your role command has just run and you hold no memory of earlier turns. "
        + "This pack is what the bridge knows about your work. Read it whole, then act; read the channel itself only for a fact the pack lacks. "
        + "Do not file the online greeting your boot sequence describes — the channel already carries your earlier entries.";

    public const string PENDING_HEADING = "## What woke you — pending entries (act on these)";
    public const string UNAVAILABLE_HEADING = "## Sections the bridge could not fill";

    public static string Build(StatePackInputs inputs)
    {
        var text = new StringBuilder();

        text.Append(TITLE_PREFIX).Append(inputs.MemberId).Append(" of ").Append(inputs.OrchId)
            .Append(" — turn ").Append(inputs.RequestId).Append('\n').Append('\n');
        text.Append(OPENING).Append('\n').Append('\n');
        text.Append("## Your contract\n\n").Append(PrintTurnPrompt_Builder.Describe_Contract(inputs.Sources)).Append('\n');

        if (inputs.Brief != null)
            Append_Entry(text, "## Your brief", inputs.Brief, BRIEF_CAP);

        if (inputs.LastOwnEntry != null)
            Append_Entry(text, "## Your last report (your state as you left it)", inputs.LastOwnEntry, LAST_OWN_CAP);

        if (inputs.ProgressNote != null)
            Append_Block(text, PROGRESS_HEADING, inputs.ProgressNote, PROGRESS_CAP, StatePack_Locator.PROGRESS_FILE_NAME, keepTail: true);

        // NOT keepTail, and that is the opposite of the progress note on purpose. A dead end is
        // written once and stays true for ever, so recency does not make it more relevant — and the
        // OLDEST conclusions are the ones nothing else on disk still remembers, while the newest are
        // still half-visible in the last few channel entries. The truncation marker points at the
        // file, so the supervisor can see it has outgrown its budget and compact it: it is the only
        // reader who can judge which dead end is still live, and the app must not choose for it.
        if (inputs.Conclusions != null)
            Append_Block(text, CONCLUSIONS_HEADING, inputs.Conclusions, CONCLUSIONS_CAP, StatePack_Locator.CONCLUSIONS_FILE_NAME);

        if (inputs.PlanText != null)
            Append_Block(text, "## The ledger — PLAN.md", inputs.PlanText, PLAN_CAP, "PLAN.md");
        else if (inputs.LedgerLines.Count > 0)
            Append_Block(text, "## Your ledger lines (PLAN.md)", string.Join('\n', inputs.LedgerLines), LEDGER_CAP, "PLAN.md");

        if (inputs.GitLines.Count > 0)
            Append_Block(text, "## Code state (git, read by the bridge)", string.Join('\n', inputs.GitLines), GIT_CAP, "git status");

        if (inputs.OwnerTail.Count > 0)
        {
            var tail = string.Join("\n\n", inputs.OwnerTail.Select(entry => entry.RawText.Trim()));
            Append_Block(text, $"## Owner channel — the last {inputs.OwnerTail.Count} entries", tail, OWNER_TAIL_CAP, "owner-channel.md");
        }

        // LAST BEFORE THE TRIGGER, because these are the things the session must hold while it reads
        // what woke it — and keepTail for the progress note's reason: the newest notes are the ones
        // still standing, and a cap that dropped them would leave the session with the oldest.
        if (inputs.StandingNotes.Count > 0)
        {
            var notes = string.Join("\n\n", inputs.StandingNotes.Select(note => note.RawText.Trim()));

            Append_Block(text, STANDING_NOTES_HEADING, notes, STANDING_NOTES_CAP, Channels.StatusLog.StatusLog_Store.FILE_NAME, keepTail: true);
        }

        text.Append(PENDING_HEADING).Append('\n').Append('\n');
        text.Append(PrintTurnPrompt_Builder.Describe_Traffic(inputs.Pending, inputs.Sources));

        if (inputs.Unavailable.Count > 0)
        {
            text.Append('\n').Append(UNAVAILABLE_HEADING).Append('\n').Append('\n');

            foreach (var line in inputs.Unavailable)
                text.Append("- ").Append(line).Append('\n');
        }

        return text.ToString();
    }

    static void Append_Entry(StringBuilder text, string heading, IChannelEntry entry, int cap)
    {
        Append_Block(text, $"{heading} — entry [{entry.Index}], {entry.DateText}", entry.RawText.Trim(), cap, $"entry [{entry.Index}] in your channel");
    }

    /// <param name="keepTail">
    /// Keep the END of a body over its cap rather than its start — for the progress note, whose last
    /// lines are where the member got to while its first are the oldest steps.
    /// </param>
    static void Append_Block(StringBuilder text, string heading, string body, int cap, string whereToReadTheRest, bool keepTail = false)
    {
        text.Append(heading).Append('\n').Append('\n');

        if (body.Length <= cap)
            text.Append(body).Append('\n').Append('\n');
        else if (keepTail)
            text.Append(Describe_Truncation(body.Length - cap, whereToReadTheRest)).Append('\n').Append(body, body.Length - cap, cap).Append('\n').Append('\n');
        else
            text.Append(body, 0, cap).Append('\n').Append(Describe_Truncation(body.Length - cap, whereToReadTheRest)).Append('\n').Append('\n');
    }

    public static string Describe_Truncation(int droppedCharacters, string whereToReadTheRest)
    {
        return $"[… {droppedCharacters} characters truncated by the bridge — read {whereToReadTheRest} for the rest]";
    }
}
