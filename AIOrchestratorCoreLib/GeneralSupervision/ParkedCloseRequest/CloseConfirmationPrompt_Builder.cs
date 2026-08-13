namespace AIOrchestratorCoreLib.GeneralSupervision.ParkedCloseRequest;

/// <summary>
/// The text the owner taps on, for either kind of close.
///
/// It lives out here rather than in the engine because <c>BridgeEngineModel</c> is
/// <c>internal sealed</c> with no <c>InternalsVisibleTo</c>: anything decided in there is unreachable
/// from the suite, and this is the sentence that decides whether the owner understands what they are
/// ending. A prompt that says "orchestration" while the tap retires one member is the worst possible
/// version of this feature — it would be a guard that MISLEADS at the only moment it is read.
/// </summary>
public static class CloseConfirmationPrompt_Builder
{
    public static string Build(IParkedCloseRequest request, string? unresolvedLedger)
    {
        return request.Kind switch
        {
            ParkedCloseKinds.Orchestration => Build_ForOrchestration(request, unresolvedLedger),
            ParkedCloseKinds.Implementer => Build_ForImplementer(request),
            ParkedCloseKinds.Promotion => Build_ForPromotion(request),
            _ => throw new ArgumentOutOfRangeException(nameof(request), $"unhandled close kind '{request.Kind}'"),
        };
    }

    /// <summary>
    /// THE TWO WORDS THE OWNER ACTUALLY TAPS. They were hard-coded "✅ Close it" / "✋ Keep it open"
    /// while the prompt above them was already kind-aware, so a promotion asked "Turn 'X' into a full
    /// crew?" over a button reading CLOSE IT.
    ///
    /// That is worse than a cosmetic mismatch and worse than the wrong archive label this same class
    /// already guards against. Reading those buttons, the safe-looking tap is "Keep it open" — which
    /// records a DECLINED promotion, tells the solo the owner refused and not to ask again, and
    /// leaves nobody aware that the question asked was never the question answered.
    ///
    /// They live here, beside the prompt they belong to, for the reason this whole class exists: the
    /// engine is unreachable from the suite, and the sentence the owner reads at the one moment they
    /// decide is not something to leave untestable.
    /// </summary>
    public static (string Confirm, string Decline) Build_ButtonLabels(ParkedCloseKinds kind)
    {
        return kind switch
        {
            ParkedCloseKinds.Promotion => ("✅ Make it a crew", "✋ Keep one session"),
            ParkedCloseKinds.Orchestration => ("✅ Close it", "✋ Keep it open"),
            ParkedCloseKinds.Implementer => ("✅ Close it", "✋ Keep it open"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), $"unhandled close kind '{kind}'"),
        };
    }

    /// <summary>
    /// What the prompt is rewritten to say once they have tapped, so the topic carries the decision
    /// rather than the question. It was unconditionally "Close 'X'? … Closed — you confirmed", which
    /// left a promoted orchestration's topic permanently reading that it had been closed.
    ///
    /// An UNREADABLE request falls back to neutral wording rather than to the close wording. Guessing
    /// "closed" is how the record comes to say the opposite of what happened, and this method exists
    /// because that already happened once at the archive label.
    /// </summary>
    public static string Build_DecidedText(ParkedCloseKinds? kind, string orchId, bool confirmed)
    {
        return kind switch
        {
            ParkedCloseKinds.Promotion => confirmed
                ? $"⚙️ Turn '{orchId}' into a full crew?\n\n✅ Promoted — you confirmed. The supervisor is taking over this conversation."
                : $"⚙️ Turn '{orchId}' into a full crew?\n\n✋ Left as one session — you declined. It keeps working exactly as it was.",

            ParkedCloseKinds.Orchestration or ParkedCloseKinds.Implementer => confirmed
                ? $"⚠️ Close '{orchId}'?\n\n✅ Closed — you confirmed."
                : $"⚠️ Close '{orchId}'?\n\n✋ Kept open — you declined. Its sessions keep running.",

            _ => confirmed
                ? $"'{orchId}' — ✅ you confirmed."
                : $"'{orchId}' — ✋ you declined. Nothing was changed.",
        };
    }

    /// <summary>
    /// THE WHOLE ASK, verb included — "the close of 'imp-1'", "the promotion to a full crew" — for the
    /// held / declined / lapsed / failed notices that report what became of a request.
    ///
    /// It used to name only the OBJECT ("this orchestration", "the promotion to a full crew") and every
    /// caller stapled its own verb in front. That is fine while every request is a close and breaks the
    /// day one is not: the declined notice read *"You asked to close the promotion to a full crew"* and
    /// the lapse notice read *"close of the promotion to a full crew LAPSED"*. The branch here was
    /// right; the sentence around it was never a variable.
    ///
    /// So the verb comes from the same switch as the object. A caller cannot get it wrong because it no
    /// longer supplies it.
    /// </summary>
    public static string Describe_AskedFor(IParkedCloseRequest request)
    {
        return request.Kind switch
        {
            ParkedCloseKinds.Orchestration => "the close of this orchestration",
            ParkedCloseKinds.Implementer => $"the close of '{request.MemberId}'",
            ParkedCloseKinds.Promotion => "the promotion to a full crew",
            _ => "the request you filed",
        };
    }

    /// <summary>
    /// The same ask for the GENERAL channel, which tracks many orchestrations and cannot read "this
    /// orchestration" — it needs the id said out loud.
    ///
    /// Two audiences, one fact, deliberately two methods: the general supervisor reading "the close of
    /// this orchestration" has no way to know which, and the requester reading its own id in its own
    /// channel is being told something it already knows. Said here so the pair does not read as the
    /// duplication this branch has spent the evening removing.
    ///
    /// It replaces a ternary that asked `Kind == Orchestration` and put the MEMBER ID in the other
    /// arm — over a three-valued enum, with a promotion carrying no member id by construction. A
    /// declined promotion was reported to the general supervisor as the close of a member with no name.
    /// </summary>
    public static string Describe_AskedFor_ToGeneral(IParkedCloseRequest? request, string orchId)
    {
        return request?.Kind switch
        {
            ParkedCloseKinds.Orchestration => $"the close of '{orchId}'",
            ParkedCloseKinds.Implementer => $"the close of '{request!.MemberId}' in '{orchId}'",
            ParkedCloseKinds.Promotion => $"the promotion of '{orchId}' to a full crew",
            _ => $"a request against '{orchId}'",
        };
    }

    /// <summary>
    /// The two-word flash Telegram shows the instant the button is tapped, before anything has run.
    ///
    /// It was `Confirms ? "closing…" : "kept open"` — a kind-blind literal in the engine, and the THIRD
    /// owner-visible string on that tap after the buttons and the post-tap edit. The owner taps
    /// "✅ Make it a crew" and their phone flashes "closing…".
    ///
    /// A null kind means the request could not be read, and it says only that the tap arrived. Guessing
    /// the verb is the failure this whole family keeps producing.
    /// </summary>
    public static string Build_TapToast(ParkedCloseKinds? kind, bool confirms)
    {
        return kind switch
        {
            ParkedCloseKinds.Promotion => confirms ? "promoting…" : "kept as one session",
            ParkedCloseKinds.Orchestration or ParkedCloseKinds.Implementer => confirms ? "closing…" : "kept open",
            _ => confirms ? "working on it…" : "nothing changed",
        };
    }

    /// <summary>
    /// "nothing was closed" is a CLAIM, and on the stale-tap and unreadable-request paths it is one
    /// this code cannot make: those branches exist precisely because the request file is gone,
    /// expired or corrupt, so the kind is unknowable and reaching for it would be a guess.
    ///
    /// The neutral form is the honest one there, and it is the rule `Build_DecidedText`'s fallback
    /// already follows. Where the kind IS known, the specific form says what did not happen.
    /// </summary>
    public static string Describe_NothingDone(ParkedCloseKinds? kind)
    {
        return kind switch
        {
            ParkedCloseKinds.Promotion => "nothing was promoted",
            ParkedCloseKinds.Orchestration or ParkedCloseKinds.Implementer => "nothing was closed",
            _ => "nothing was done",
        };
    }

    // `Describe_Subject` was here: the verb-less object phrase, "this orchestration" / "'imp-2'" /
    // "the promotion to a full crew". `Describe_AskedFor` above replaces it outright rather than
    // sitting beside it. Its two production callers are the two sentences it broke, and no prompt body
    // ever called it — so keeping it would have left a second phrase-maker for one fact, alive on
    // tests alone, and it is the one that requires the caller to supply the verb that went wrong.

    static string Build_ForOrchestration(IParkedCloseRequest request, string? unresolvedLedger)
    {
        var unresolvedPart = unresolvedLedger == null ? "" : $"\n\n⚠️ {unresolvedLedger}";

        return
            $"⚠️ Close orchestration '{request.OrchId}'?\n\n"
            + $"Asked by: {request.Requester}\n"
            + $"Reason: {request.Reason}{unresolvedPart}\n\n"
            + "This ends every session in it and deletes this topic. It cannot be undone — the folder stays on disk as audit trail. Nothing happens unless you tap.";
    }

    /// <summary>
    /// THE ONE PROMPT THAT ASKS FOR MORE RATHER THAN LESS, so it names the cost first: this is the
    /// owner agreeing to spend a supervisor and an implementer indefinitely, in place of one session.
    ///
    /// It says what SURVIVES as plainly as what ends, because the fear a reader brings to a tap that
    /// kills their session is the wrong fear here — the conversation, the topic and the history all
    /// carry over untouched, and only the session itself is replaced.
    ///
    /// And it says ONE-WAY, because there is no demotion: the way back is closing the orchestration
    /// and starting a basic one, which loses this channel. That is exactly the sort of thing a person
    /// discovers afterwards unless the prompt says it.
    /// </summary>
    static string Build_ForPromotion(IParkedCloseRequest request)
    {
        return
            $"⚙️ Turn '{request.OrchId}' into a full crew?\n\n"
            + $"Asked by: {request.Requester}\n"
            + $"Reason: {request.Reason}\n\n"
            + "This spends a supervisor AND an implementer from now on, instead of the one session you have. "
            + "That session ends — but this conversation, this topic and everything already said carry over to the supervisor untouched. "
            + "Treat it as one-way: there is no going back to a single session without closing the orchestration. Nothing happens unless you tap.";
    }

    /// <summary>
    /// NO LEDGER LINE HERE, and that is a decision. The ledger belongs to the orchestration, so its
    /// unresolved count says nothing about whether THIS member is safe to retire — showing it beside
    /// a one-member close would read as "these lines die with it", which is false and would push the
    /// owner toward keeping sessions alive for a reason that does not apply.
    ///
    /// What the owner needs instead is the scope: the rest keeps running.
    /// </summary>
    static string Build_ForImplementer(IParkedCloseRequest request)
    {
        return
            $"⚠️ Close member '{request.MemberId}' in '{request.OrchId}'?\n\n"
            + $"Asked by: {request.Requester}\n"
            + $"Reason: {request.Reason}\n\n"
            + "This kills that one session's terminal and loses whatever it had not written down. The orchestration and every other member keep running, and its channel stays on disk as audit trail. Nothing happens unless you tap.";
    }
}
