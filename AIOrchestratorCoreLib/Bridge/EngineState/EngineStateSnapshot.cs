namespace AIOrchestratorCoreLib.Bridge.EngineState;

/// <summary>
/// One inline decision button the owner has been offered and has not yet consumed.
///
/// <para>
/// <b>Data</b> is the literal <c>callback_data</c> Telegram hands back on a tap, and it is a NONCE
/// plus an option index (see <see cref="Telegram.CallbackToken"/>) rather than the old
/// <c>opt-{counter}</c>. A counter restarts at zero with the process, so after a restart the app
/// would have handed the same payload to a different decision — a stale button on a phone that had
/// been scrolled past for hours could then answer a question asked minutes ago. The nonce is
/// unguessable and never reissued; the index is what makes it readable without a lookup table.
/// </para>
/// <para>
/// <b>ExpiresUtc</b> is the reason this record exists at all rather than a tuple: a button with no
/// deadline is a decision that can be taken at any point in the future by whoever is holding the
/// phone. Past it the tap is refused and logged, never silently ignored.
/// </para>
/// </summary>
public sealed record PendingButtonRecord
{
    public required string Data { get; init; }

    /// <summary>Null is the General topic — the same convention the receipt registry uses.</summary>
    public long? ThreadId { get; init; }

    /// <summary>What the SESSION receives on a tap. Never the shortened button label.</summary>
    public required string OptionText { get; init; }

    /// <summary>Siblings of one question share it; the first tap consumes the whole group.</summary>
    public long GroupId { get; init; }

    /// <summary>The question as it was sent, so an answered message can be rewritten to record it.</summary>
    public required string QuestionText { get; init; }

    public DateTime ExpiresUtc { get; init; }

    /// <summary>A tap on this option starts the second gesture instead of answering.</summary>
    public bool IsHighRisk { get; init; }

    /// <summary>
    /// A tap on this option TAKES NO DECISION: it asks the supervisor to explain and discuss, and
    /// the question closes without a choice being recorded. It consumes the group exactly like an
    /// answer does — see <c>Handle_CallbackTap_Async</c> — and what differs is only the record left
    /// on the message: an acknowledgement instead of "✅ &lt;the option&gt;".
    ///
    /// <para>
    /// IT USED TO KEEP THE KEYBOARD LIVE (<c>keepsGroupOpen</c>), and that is what this replaces.
    /// A tap that changed nothing on the message read as a button that does nothing: the owner
    /// tapped "Let's talk" twelve times in one afternoon on <c>fincanva-5</c>, four of them inside a
    /// minute, because the only feedback was Telegram's transient toast. Keeping the question open
    /// also left the app holding a live question the owner had visibly stopped answering.
    /// </para>
    /// </summary>
    public bool AnswersNothing { get; init; }
}

/// <summary>
/// A question on the owner's phone that has not been answered yet.
///
/// <para>
/// <b>DeadlineUtc / DefaultOptionIndex</b> are what turn "the owner never replied" from a silent
/// stall into a recorded decision: at half the window the message is EDITED with a reminder (never
/// a second message — decision 14), and at the deadline the default is applied and written into the
/// channel as an app entry, so the catch-up burst shows what was decided in their absence.
/// </para>
/// <para>
/// <b>A high-risk question has no default and never gets one.</b> It expires as a DENY with reason
/// timeout and stays listed in /pending. Defaulting a push or a deploy because a phone was in a
/// pocket is the one outcome this whole stage exists to make impossible.
/// </para>
/// </summary>
public sealed record OpenQuestionRecord
{
    public long MessageId { get; init; }
    public required string OrchId { get; init; }
    public required string Text { get; init; }
    public DateTime AskedUtc { get; init; }
    public long ButtonGroupId { get; init; }
    public DateTime? DeadlineUtc { get; init; }
    public int? DefaultOptionIndex { get; init; }
    public bool IsHighRisk { get; init; }

    /// <summary>Set once the half-window reminder edit has been applied, so it happens once.</summary>
    public bool ReminderSent { get; init; }

}

/// <summary>
/// A high-risk decision that has been TAPPED and is waiting for the owner to type back the code
/// shown in the message — the read-back half of the second gesture.
///
/// <para>
/// The code is displayed to the owner on purpose: this is a read-back/hear-back confirmation, the
/// defence against a tap taken by an unlocked phone in someone else's hand, not a shared secret. It
/// is nonetheless kept out of the orchestrator log and out of every channel file, because a code
/// sitting in an append-only file is a code that outlives its window.
/// </para>
/// </summary>
public sealed record PendingConfirmationRecord
{
    public required string Code { get; init; }
    public long? ThreadId { get; init; }

    /// <summary>The question message, so the outcome is edited into it rather than posted anew.</summary>
    public long? MessageId { get; init; }

    public required string OrchId { get; init; }

    /// <summary>The option the owner tapped — delivered to the session only once the code lands.</summary>
    public required string OptionText { get; init; }

    public required string QuestionText { get; init; }
    public DateTime ExpiresUtc { get; init; }
}

/// <summary>
/// A close / member-close / promotion confirmation that is on the owner's phone right now, written
/// down so the state file cannot be read as "nothing is pending" while one is.
///
/// <para>
/// DIAGNOSTIC, NOT OPERATIVE — and the difference is the whole record. The durable state of this
/// family is the PARKED REQUEST FILE (<c>.requests/awaiting-owner/&lt;id&gt;.json</c>); a restart
/// re-asks with fresh buttons, deliberately, because a prompt nobody can see any more is
/// indistinguishable from an owner who has not answered. Nothing here is read back into the live
/// registry, so the tap behaves after a restart exactly as it did before this record existed.
/// </para>
/// <para>
/// SO IT CARRIES NO CALLBACK PAYLOAD, on purpose. A ticket in the file is a keyboard somebody
/// eventually restores, and restoring it would silently swap which of the two mechanisms is the
/// truth on a restart — a change to BEHAVIOUR, which is the owner's to make and not a refactor's.
/// What is here is what a human reading the file at 2 a.m. needs: which request, which
/// orchestration, what it asks, when it was asked, and when it stops being tappable.
/// </para>
/// <para>
/// ONE RECORD PER PARKED REQUEST, not per button. The live registry keys two entries — confirm and
/// decline — off one prompt; a reader counting rows would otherwise see two decisions where the
/// owner sees one question.
/// </para>
/// </summary>
public sealed record CloseConfirmationRecord
{
    /// <summary>The identity: the durable record's own path, which is also the sweep's key.</summary>
    public required string ParkedPath { get; init; }

    public required string OrchId { get; init; }

    /// <summary>
    /// <c>Orchestration</c> / <c>Implementer</c> / <c>Promotion</c>, as TEXT rather than the enum.
    /// A kind added by a newer build survives a rollback's round trip instead of being dropped.
    /// </summary>
    public required string Kind { get; init; }

    /// <summary>The member being retired; null for anything else.</summary>
    public string? MemberId { get; init; }

    /// <summary>Who asked, as the request itself recorded it.</summary>
    public string? Requester { get; init; }

    /// <summary>When the prompt was put on the phone by the host that is writing this.</summary>
    public DateTime AskedUtc { get; init; }

    /// <summary>
    /// When the tap stops being honoured — the parked file's own write time plus
    /// <see cref="GeneralSupervision.CloseConfirmation_Parking.EXPIRY_HOURS"/>. Null when the file
    /// could not be stat'ed: absent is an answer, a guessed deadline is not.
    /// </summary>
    public DateTime? ExpiresUtc { get; init; }

    /// <summary>The Telegram message the buttons are attached to, when one was recorded.</summary>
    public long? PromptMessageId { get; init; }
}

/// <summary>
/// Everything the bridge would otherwise FORGET when the process ends: the decisions it is holding,
/// the things it has already said once, and the counters that stop it saying them again.
///
/// <para>
/// WHY A SNAPSHOT RATHER THAN A LIVE OBJECT. The engine keeps its state in the dictionaries it has
/// always kept it in — this is a change of RESIDENCE, not of logic. The snapshot is built from them
/// at each decision point and read back into them once at construction, so no code path has to ask
/// a store a question it used to answer from a field, and every existing behaviour test keeps
/// driving exactly the code it drove before.
/// </para>
/// <para>
/// WHAT IS DELIBERATELY NOT HERE: mirror offsets and the Telegram update cursor (they have their own
/// file, <see cref="BridgeState_Store"/>, rewritten on every tick that moved a cursor — a different
/// write cadence and a different failure), and every cache the app rebuilds by looking at the world: first-sighted
/// channels, topic-name stamps, receipt message ids, status-line texts, away trackers. Those are
/// re-derived within a tick or two of a restart. What is here is what NOTHING can re-derive: a
/// decision the owner was asked to take, and the memory that they were already asked.
/// </para>
/// <para>
/// ONE OWNER DECISION IS HERE AS A RECORD AND NOT AS A TICKET, AND IT IS THE ONE THAT LOOKED LIKE A
/// BUG. The close / member-close / promote confirmations keep their durable record as a PARKED
/// REQUEST FILE (<c>.requests/awaiting-owner/&lt;id&gt;.json</c>, see <c>CloseConfirmation_Parking</c>):
/// the request survives, the prompt does not, and the next sweep asks again with fresh buttons. So
/// while one of those is outstanding this snapshot holds NO pending button — which on 2026-09-06 was
/// read on the VPS as a save that had failed. It had not, and now the file says so itself:
/// <see cref="CloseConfirmations"/> lists them (see that type for why it carries no payload). The
/// two mechanisms are still two, deliberately — merging them would change which one is the truth on
/// a restart, and that is a decision about behaviour rather than about storage.
/// </para>
/// </summary>
public sealed record EngineStateSnapshot
{
    /// <summary>Orchestrations whose owner has spoken and whose answer has not been pushed yet.</summary>
    public IReadOnlyList<string> OwnerAwaitingAnswer { get; init; } = [];

    /// <summary>Member channel → the opaque identity of the thing it was last nudged about.</summary>
    public IReadOnlyDictionary<string, string> NudgedAboutEntry { get; init; } = new Dictionary<string, string>();

    public IReadOnlyList<PendingButtonRecord> PendingButtons { get; init; } = [];
    public IReadOnlyList<OpenQuestionRecord> OpenQuestions { get; init; } = [];
    public IReadOnlyList<PendingConfirmationRecord> PendingConfirmations { get; init; } = [];

    /// <summary>
    /// Close / member-close / promotion prompts live on the phone. WRITTEN AND NEVER RESTORED — see
    /// <see cref="CloseConfirmationRecord"/>: the engine reads them once, only to tell a first ask
    /// apart from a re-ask after a restart in its journal, and never to answer a tap with.
    /// </summary>
    public IReadOnlyList<CloseConfirmationRecord> CloseConfirmations { get; init; } = [];

    /// <summary>Watchdog slot key → consecutive respawns, so a crash loop is not un-counted by a restart.</summary>
    public IReadOnlyDictionary<string, int> ConsecutiveRespawns { get; init; } = new Dictionary<string, int>();

    /// <summary>
    /// The button group counter. Persisted so a restart cannot reissue a group id that a live
    /// keyboard on the phone still refers to — which would let one tap consume another question's
    /// siblings.
    /// </summary>
    public long ButtonGroupSequence { get; init; }

    /// <summary>Null when the dispatcher is running. See <see cref="Limits.DispatchPause_Gate"/>.</summary>
    public DateTime? DispatchPausedUntilUtc { get; init; }

    /// <summary>Why it paused, in the words the owner was told — so the resume can name the same thing.</summary>
    public string? DispatchPauseReason { get; init; }

    public static EngineStateSnapshot Empty => new();
}
