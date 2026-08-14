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
    /// What the prompt is REPLACED with once the tap has been acted on — the last thing the owner is
    /// told about a close, and for a while the only thing that was untrue.
    ///
    /// It used to be written before the close was attempted, so "✅ Closed — you confirmed" appeared
    /// whether or not anything closed. Two paths made it a lie: an unreadable request (nothing is
    /// touched) and a throw partway through (marked closed, sessions alive). The first heals — the
    /// request stays parked and the owner is asked again — so the sentence has to tell them the tap
    /// did not take, or the fresh prompt arrives contradicting a success still on their screen.
    ///
    /// THE SECOND DOES NOT HEAL, and it is why this maps outcomes rather than a boolean. Nothing will
    /// re-offer that request, so whatever this says is final. It must claim NEITHER success nor
    /// failure and point at the one place that can answer, because "we do not know" rendered as either
    /// is how the owner ends up believing live sessions are dead.
    ///
    /// IT IS ALSO THE ONLY IMPLEMENTATION OF THIS SENTENCE. A second one — `Build_DecidedText`, which
    /// took a boolean and was written by the same-day promotion work — sat beside it for one merge.
    /// Two builders of one owner-facing sentence is the case decision 12 names outright: they drift,
    /// and the one the tests exercise stops being the one the engine calls. The promotion wording came
    /// into this switch and that method went; PromotionPromptWordingTests now asserts against what the
    /// engine actually sends.
    /// </summary>
    /// <param name="request">
    /// What the tap was about, or null when it could not be read — the ONE case where the wording
    /// cannot name a member or a kind, because nobody can say whether there was one.
    /// </param>
    public static string Describe_Decision(string orchId, IParkedCloseRequest? request, CloseTapOutcomes outcome)
    {
        var isMember = request?.Kind == ParkedCloseKinds.Implementer;
        var isPromotion = request?.Kind == ParkedCloseKinds.Promotion;

        // THE HEADER MUST NAME WHAT THE PROMPT NAMED. Every sentence used to open "Close '{orchId}'?"
        // whatever had been tapped, so retiring one member reported itself under the orchestration's
        // name — the exact failure this class's own header calls the worst version of this feature —
        // and a promotion, which closes nothing at all, was announced as a close.
        //
        // An UNREADABLE request gets the neutral header rather than the close one. Guessing "close"
        // is how a record comes to say the opposite of what happened.
        var header =
            isPromotion ? $"⚙️ Turn '{orchId}' into a full crew?"
            : isMember ? $"⚠️ Close member '{request!.MemberId}' in '{orchId}'?"
            : request == null ? $"'{orchId}'"
            : $"⚠️ Close '{orchId}'?";

        // WHAT MIGHT STILL BE RUNNING is the member, or the whole orchestration. Saying "sessions" for
        // a one-member close tells the owner something far worse than what happened.
        var survivors = isMember ? $"'{request!.MemberId}' may still be running" : "its sessions may still be running";

        var line = outcome switch
        {
            CloseTapOutcomes.Declined =>
                isPromotion ? "✋ Left as one session — you declined. It keeps working exactly as it was."
                : isMember ? "✋ Kept open — you declined. That session keeps running."
                : request == null ? "✋ You declined. Nothing was changed."
                : "✋ Kept open — you declined. Its sessions keep running.",

            CloseTapOutcomes.Closed =>
                isPromotion ? "✅ Promoted — you confirmed. The supervisor is taking over this conversation."
                : request == null ? "✅ You confirmed."
                : "✅ Closed — you confirmed.",

            // NO PROMISE THAT MAY NOT BE KEPT. This said "you will be asked again shortly", which is
            // true only while the file stays readable — a persistently unreadable one is archived and
            // reported to the REQUESTER, and the owner is never asked and never told. The condition is
            // now stated rather than dropped.
            //
            // AND IT NAMES NO VERB. It read "NOT closed", which was safe while every parked request
            // was a close and became a guess the day one was a PROMOTION: this outcome exists only
            // when the file could not be read, so the kind is exactly the thing nobody can know, and
            // a solo that asked to be promoted was told its close had not happened.
            CloseTapOutcomes.NotAttempted =>
                "⚠️ NOT done — the request could not be read, so nothing was changed. You will be asked again if it can be read on the next sweep.",

            // THE OUTCOME THIS WHOLE CHANGE EXISTS FOR, and it has to be ACTIONABLE as well as honest.
            // It said "check the app" and sent the owner to a card that reads closed and dimmed, with
            // the close button disabled and "Show session" hidden — the one control that would reach a
            // session still running. It now says what is unusual about this outcome instead: the close
            // is recorded, nothing will ask again, and the error is where errors actually land.
            CloseTapOutcomes.Uncertain => isPromotion
                ? "⚠️ The promotion did not complete. It is recorded as done, the crew may not be running, and you will NOT be asked again. The error is in the General topic."
                : $"⚠️ Close did not complete. It is recorded as closed, {survivors}, and you will NOT be asked again. The error is in the General topic.",

            _ => throw new ArgumentOutOfRangeException(nameof(outcome), $"unhandled close outcome '{outcome}'"),
        };

        return $"{header}\n\n{line}";
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
    /// The neutral form is the honest one there, and it is the rule `Describe_Decision`'s null-request fallback
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
