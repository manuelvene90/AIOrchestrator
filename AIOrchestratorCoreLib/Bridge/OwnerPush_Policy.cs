using AIOrchestratorCoreLib.Status;
using AIOrchestratorCoreLib.Channels;

namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// Decides what actually reaches the owner's PHONE. Everything the supervisor writes still lands in
/// owner-channel.md and is readable in the app — this only governs the push.
///
/// The owner, after a transcript of running commentary: "I answer the sup a question, and then the
/// sup doesn't disturb me anymore unless it has another question. A brief every 30 minutes about
/// how the work is going is fine, but not the waterfall of messages I get now."
///
/// So the phone gets exactly three things:
///   - a QUESTION (it needs them to decide, and it stops the conversation until they do),
///   - the ANSWER to something they asked (they are waiting for it),
///   - a BLOCKED flag (work has stopped and only they can restart it).
/// Progress narration is not one of them. It is not lost; it is simply not a notification.
///
/// That is the <c>phone.push = filtered</c> arm (master's, and classic's). The fork's owner ruled the
/// other way on 2026-09-09 and both are now a per-machine choice — see <see cref="Decide"/>.
/// </summary>
public static class OwnerPush_Policy
{
    /// <summary>
    /// Written by the supervisor when it needs a decision — rendered as tappable buttons. The word
    /// itself lives with the rest of the channel vocabulary (<see cref="MemberState_Resolver.QUESTION_MARKER"/>);
    /// this name stays because this file's readers are about the owner's phone, not about member state.
    /// </summary>
    // FROM THE GRAMMAR, and `static readonly` rather than `const` because of it: the grammar is a
    // FILE both this app and the bash tool read, so its values arrive at runtime. A `const` would
    // have to be a literal here, which is the ninth copy E3 removes.
    public static readonly string QUESTION_MARKER = MemberState_Resolver.QUESTION_MARKER;
    public static readonly string OPTION_MARKER = ChannelGrammar.OPTION;

    /// <summary>Work has stopped and only the owner can restart it.</summary>
    public static readonly string BLOCKED_MARKER = ChannelGrammar.BLOCKED_ON_OWNER;

    /// <summary>A picture for the owner, uploaded as a photo. See <see cref="Carries_FileForTheOwner"/>.</summary>
    public static readonly string IMAGE_MARKER = ChannelGrammar.IMAGE;

    /// <summary>A file for the owner, uploaded as a document. See <see cref="Carries_FileForTheOwner"/>.</summary>
    public static readonly string ATTACH_MARKER = ChannelGrammar.ATTACH;

    /// <summary>
    /// The one-line greeting a session writes as it boots — "supervisor online — …", "solo online
    /// — …". A FOURTH thing the phone gets, and the newest, because the owner cannot use this system
    /// without it (2026-08-25): *"At the start of a new session I don't receive a message from the
    /// sup/solo telling me it's online and ready, so I don't know when I can start writing. Absurdly
    /// I receive a message from the impl saying it's online … I should receive Sup Online, or Solo
    /// Online, and I also want Rev1 Online. In short, I want to know that the sessions are ready."*
    ///
    /// It IS progress narration by shape — no question, no marker — so the filter below killed it,
    /// and the role commands made that worse by mandating an EMPTY body, which removes the last
    /// chance of a stray '?' rescuing it. A member's identical greeting reached the phone only
    /// because a spoke channel is not an owner channel and never meets this policy at all.
    ///
    /// It cannot become a waterfall: a session writes it exactly once, at boot.
    /// </summary>
    public static readonly string ONLINE_MARKER = ChannelGrammar.BOOT_ANNOUNCEMENT_WORD;

    /// <summary>
    /// Matched on the SUBJECT, not the raw text, so the word "online" in a sentence is not a
    /// greeting. The subject every role command mandates starts with the speaker and the marker:
    /// `supervisor online — <repo> — <folders>`, `solo online — …`, `rev-1 online`.
    /// </summary>
    public static bool Is_OnlineGreeting(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return false;

        var words = subject.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // The marker is the SECOND word at the latest — "supervisor online", "solo online",
        // "rev-1 online", "general supervisor online". Anything further in is prose.
        for (var i = 0; i < words.Length && i < 3; i++)
        {
            if (string.Equals(words[i].TrimEnd(',', '.', ':', ';'), ONLINE_MARKER, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The turn-end declaration the run-to-the-end hook accepts: "WAITING ON &lt;what&gt;" in the
    /// subject means the session is ending its turn on a machine — a build, a suite, a sub-agent.
    /// It is a STATUS LINE by definition, never the answer to anything.
    /// </summary>
    public const string WAITING_ON_MARKER = "WAITING ON";

    /// <summary>
    /// Whether the SUBJECT declares a turn end. Matched on the subject only: the hook also accepts
    /// the marker at the start of a body line, but sessions end nearly every entry — answers
    /// included — with a "WAITING ON …" line to satisfy it, so the body says nothing about what the
    /// entry IS. The boundary is the hook's own ("WAITING ONLY" contains "WAITING ON"): the marker
    /// must be followed by a non-letter or the end of the subject. The hook
    /// (kit/hooks/run-to-the-end-check.sh) is the other reader of this marker; the two must agree.
    ///
    /// <para>
    /// IT HAS TWO READERS. The ENGINE asks it before removing the orchestration from its awaiting-answer
    /// set: a turn-end declaration must NOT CONSUME the owner's answer credit. That credit is one-shot,
    /// and on 2026-09-10 a "WAITING ON the re-review — fix landed" written seconds before the real answer
    /// spent it three times in one topic; the answer that followed was narration by shape and never
    /// arrived. The owner re-typed their question each time. And <see cref="Decide"/> asks it under
    /// <c>phone.push = filtered</c>, so the open credit does not SEND such a line as if it were the
    /// answer; under <c>everything</c> it reaches the phone like any other entry.
    /// </para>
    /// </summary>
    public static bool Is_TurnEndDeclaration(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
            return false;

        var searchFrom = 0;

        while (true)
        {
            var start = subject.IndexOf(WAITING_ON_MARKER, searchFrom, StringComparison.Ordinal);

            if (start < 0)
                return false;

            var end = start + WAITING_ON_MARKER.Length;

            if (end >= subject.Length || !char.IsLetter(subject[end]))
                return true;

            searchFrom = end;
        }
    }

    /// <summary>
    /// WHAT HAPPENS TO ONE SUPERVISOR ENTRY ON AN OWNER CHANNEL, under the owner's <c>phone.push</c>
    /// (plan 03 Task 2). Two ways this machine has shipped, and both are now a choice rather than a build.
    ///
    /// <para>
    /// <see cref="PhonePushModes.Everything"/> — THE FORK'S RULING OF 2026-09-09: *"If the supervisor
    /// writes to me, I must know it — that rings. Status, receipts and app bookkeeping do not ring."*
    /// The filter below had let through a question, an awaited answer, a <c>BLOCKED ON OWNER</c>, a
    /// file and the boot greeting, and it worked exactly as designed while producing the opposite of
    /// what that owner wanted: their own quoted example of a message they NEEDED — *"La regola ora è
    /// completa…"* — was suppressed and reached them five minutes late, through the silent-deadlock net,
    /// in raw Markdown. Under this mode the brake on chatter is the SKILL plus the brevity nudge, and
    /// every entry is sent — byte-for-byte what <see cref="Should_Push"/> answered while it was the only
    /// build, so quiet's phone cannot move.
    /// </para>
    /// <para>
    /// <see cref="PhonePushModes.Filtered"/> — MASTER'S, rebuilt from the predicates this class kept
    /// through the fork: *"I answer the sup a question, and then the sup doesn't disturb me anymore
    /// unless it has another question."* A question (marked or in prose), a <c>BLOCKED ON OWNER</c>, a
    /// file for the owner, the boot greeting and THE answer are sent now; everything else is HELD for
    /// the turn-end digest — not rung, and not lost.
    /// </para>
    /// <para>
    /// TWO REFUSALS HOLD IN BOTH MODES, and neither is ever held. An EMPTY body is nothing to read, and
    /// a held one was once released five minutes later, RINGING, as a blank message about nothing. The
    /// owner's own words quoted back (<see cref="Is_OwnerRestatement"/>) say nothing they did not just
    /// type — holding them would put them in the digest.
    /// </para>
    /// <para>
    /// THE ANSWER IS NOT A STATUS LINE. Under <c>filtered</c> the owner's open credit sends an entry only
    /// when its subject is not a turn-end declaration (<see cref="Is_TurnEndDeclaration"/>): the engine
    /// already refuses to CONSUME the credit on one (decision 25), and without this half a "WAITING ON …"
    /// written seconds before the real answer would be SENT as if it were the thing they wait for.
    /// </para>
    /// </summary>
    public static OwnerPushDecisions Decide(PhonePushModes mode, string rawEntryText, bool ownerIsWaitingForAReply, string? subject)
    {
        // An entry with no body is nothing to read — and Telegram refuses an empty message anyway.
        if (string.IsNullOrWhiteSpace(rawEntryText))
            return OwnerPushDecisions.Drop;

        if (Is_OwnerRestatement(rawEntryText))
            return OwnerPushDecisions.Drop;

        return mode switch
        {
            PhonePushModes.Everything => OwnerPushDecisions.SendNow,
            PhonePushModes.Filtered => Is_OwedTheRingNow(rawEntryText, ownerIsWaitingForAReply, subject)
                ? OwnerPushDecisions.SendNow
                : OwnerPushDecisions.HoldForDigest,
            _ => throw new Exception($"Unhandled PhonePushModes: {mode}"),
        };
    }

    /// <summary>The filtered arm's "send now" — one union, each member a predicate this class already had.</summary>
    static bool Is_OwedTheRingNow(string rawEntryText, bool ownerIsWaitingForAReply, string? subject)
    {
        return Carries_Question(rawEntryText)
            || Asks_InProse(rawEntryText)
            || rawEntryText.Contains(BLOCKED_MARKER, StringComparison.OrdinalIgnoreCase)
            || Carries_FileForTheOwner(rawEntryText)
            || Is_OnlineGreeting(subject)
            || (ownerIsWaitingForAReply && !Is_TurnEndDeclaration(subject));
    }

    /// <summary>
    /// THE MODE A CHANNEL IS UNDER — the configured <c>phone.push</c>, except on General, which is always
    /// <see cref="PhonePushModes.Everything"/>.
    ///
    /// <para>
    /// D8 (coordinator, 2026-09-14): <c>phone.push</c> governs ORCHESTRATION owner channels only. The
    /// general supervisor's channel is an owner channel too, so the push block reaches it — and filtering
    /// it would stop the concierge's narration reaching the phone, which is most of what General is for.
    /// One place, so the mirror and the turn-end digest cannot disagree about which channels are exempt.
    /// </para>
    /// </summary>
    public static PhonePushModes Resolve_ModeForChannel(PhonePushModes configured, string orchId)
    {
        return orchId == ChannelDiscovery.GENERAL_ORCH_ID
            ? PhonePushModes.Everything
            : configured;
    }

    /// <summary>
    /// WHETHER AN ENTRY IS SENT UNDER <see cref="PhonePushModes.Everything"/> — ONE ARM OF TWO since
    /// plan 03, and nothing more: a thin call into <see cref="Decide"/>.
    ///
    /// <para>
    /// It was the whole policy while the fork's build pushed every supervisor entry (2026-09-09 to plan
    /// 03), when <paramref name="ownerIsWaitingForAReply"/> and <paramref name="subject"/> were carried
    /// unread for the day the filter came back. The engine no longer calls it — it switches on
    /// <see cref="Decide"/> under the resolved mode, and on 2026-09-14 no production code in this tree
    /// did — but the policy tests pin it as the everything arm, and deleting it is a wider change than
    /// restoring the filter.
    /// </para>
    /// </summary>
    public static bool Should_Push(string rawEntryText, bool ownerIsWaitingForAReply, string? subject = null)
    {
        return Decide(PhonePushModes.Everything, rawEntryText, ownerIsWaitingForAReply, subject) == OwnerPushDecisions.SendNow;
    }

    /// <summary>
    /// A FIFTH thing the phone gets: an entry carrying a file for the owner — a screenshot
    /// (<c>IMAGE:</c>) or a document (<c>ATTACH:</c>).
    ///
    /// <para>
    /// A FILE IS A DELIVERY, NOT NARRATION. The mockups, the CSV, the failing output: the owner has
    /// to LOOK at it, which is the whole reason it was produced, and an upload the phone never
    /// announces is an upload nobody opens. It also cannot become the waterfall this policy exists
    /// to prevent — a session writes a file for the owner rarely, and never on a loop.
    /// </para>
    /// <para>
    /// IT WAS ALREADY BROKEN FOR <c>IMAGE:</c>, silently, and that is why this is a fix rather than
    /// an addition: a screenshot sent as ordinary narration met the four rules above, matched none
    /// of them, and was suppressed with its photo. It only ever arrived when the owner happened to
    /// be waiting for a reply — which is how nobody noticed. <c>ATTACH:</c> (2026-09-07) would have
    /// inherited exactly that, so both markers are named here.
    /// </para>
    /// <para>
    /// MATCHED AT THE START OF A LINE, like the engine's own extractor, and not with
    /// <see cref="string.Contains(string, StringComparison)"/> the way <see cref="Carries_Question"/>
    /// is: only a column-0 marker line actually produces an upload, so a prose mention of "ATTACH:"
    /// must not push an entry that delivers nothing.
    /// </para>
    /// </summary>
    public static bool Carries_FileForTheOwner(string rawEntryText)
    {
        if (string.IsNullOrEmpty(rawEntryText))
            return false;

        foreach (var rawLine in rawEntryText.Split('\n'))
        {
            var line = rawLine.TrimEnd();

            foreach (var marker in new[] { IMAGE_MARKER, ATTACH_MARKER })
            {
                if (line.StartsWith(marker, StringComparison.Ordinal) && line.Length > marker.Length && line[marker.Length..].Trim().Length > 0)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A question asked WITHOUT the markers still has to reach the owner. Relying on the markers
    /// alone made this filter capable of swallowing a real question in prose — the supervisor asks,
    /// the owner never sees it, and both wait forever. So the rule is ASK-SHAPED MEANS PUSH, and
    /// the mark is read outside fenced blocks so a mockup or a snippet is not mistaken for an ask.
    ///
    /// ASK-SHAPED MEANS ENDING A LINE, NOT MERELY CONTAINING THE MARK. `Contains('?')` was the
    /// original rule and it was too loose in a way that only showed once the ❓ topic glyph was
    /// built on this same reader (2026-08-21). The owner, of one topic: *"after I put it in pc mode
    /// a ? icon appeared in the name for no reason"*, and minutes later, of another: *"you just put
    /// ? in the topic name"*. Neither entry asked anything. One said "sits at [?] until they
    /// answer", the other "Investigating the ? glyph" — the mark as a LEDGER MARKER and as a NOUN.
    ///
    /// That shape is not rare: every role command in this kit teaches the `- [ ]`/`- [>]`/`- [?]`
    /// ledger vocabulary, so sessions write `[?]` in ordinary prose constantly. Under the old rule
    /// each such entry pushed the phone AND pinned ❓ on the topic until the owner typed something —
    /// the glyph scan stops only at an OWNER entry, so a single stray mark stuck indefinitely.
    ///
    /// A prose question ends its line with the mark. Anything else has the explicit markers, which
    /// every role command already tells sessions to use when they actually need a decision.
    ///
    /// THE BIAS IS STILL TOWARDS PUSHING, and tightening it does not risk the deadlock the loose
    /// rule was protecting against: an entry this filter suppresses is REMEMBERED, and the engine
    /// releases it once the session and every member have been idle for minutes
    /// (Break_SilentDeadlock_Async). That net is not theoretical — strategy-lab-6's own log carries
    /// it firing on 2026-08-21: *"Everything went idle with an unsent supervisor entry — releasing
    /// it in case it was a question"*. This filter is the fast path; that is the guarantee.
    /// </summary>
    /// <summary>
    /// The session quoting the owner back to the owner — an entry whose whole body is
    /// <c>Owner: "…"</c> and nothing else. It reached the phone as "🔴 Sup: Owner: 'Che ne pensi?…'"
    /// (2026-09-07): a message from the session that says nothing the owner did not just type, and
    /// worse, it spent their wait, so the real answer that followed was narration again. Such an
    /// entry is never pushed, and never counts as the reply they were waiting for.
    ///
    /// Only the bare quotation is caught. A reply that OPENS by quoting them and goes on to answer
    /// has more than one line of body, and is a reply.
    /// </summary>
    public static bool Is_OwnerRestatement(string rawEntryText)
    {
        if (string.IsNullOrEmpty(rawEntryText))
            return false;

        var bodyLines = rawEntryText.Split('\n')
            .Select(line => line.TrimEnd())
            .Where(line => line.Length > 0)
            .SkipWhile(line => line.StartsWith("## ", StringComparison.Ordinal))
            .ToList();

        if (bodyLines.Count != 1)
            return false;

        return OwnerRestatement_Pattern.IsMatch(bodyLines[0].Trim());
    }

    static readonly System.Text.RegularExpressions.Regex OwnerRestatement_Pattern = new(
        "^(the\\s+)?owner(\\s+(said|says|asked|asks|wrote|writes))?\\s*:\\s*[\"\u201C\u201D'\u2018\u2019\u00AB\u00BB].*[\"\u201C\u201D'\u2018\u2019\u00AB\u00BB]\\s*$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static bool Asks_InProse(string rawEntryText)
    {
        if (string.IsNullOrEmpty(rawEntryText))
            return false;

        var withoutBlocks = Strip_FencedBlocks(rawEntryText);

        foreach (var line in withoutBlocks.Split('\n'))
        {
            // TrimEnd also takes the '\r' of a CRLF channel, and any trailing spaces.
            if (line.TrimEnd().EndsWith('?'))
                return true;
        }

        return false;
    }

    static string Strip_FencedBlocks(string text)
    {
        var parts = text.Split("```");
        var kept = new System.Text.StringBuilder();

        // Even indices are outside fences, odd indices are inside them.
        for (var i = 0; i < parts.Length; i += 2)
            kept.Append(parts[i]);

        return kept.ToString();
    }

    /// <summary>
    /// One of the two buttons the app may add under a question — which ones, and in what order, is
    /// <c>questions.appButtons</c> (<see cref="Configuration.SettingsCatalog.QuestionAppButton_Names.TALK"/>;
    /// shipped alone, ruling R14).
    ///
    /// <para>
    /// THERE WERE TWO, THEN ONE, AND NOW THE OWNER CHOOSES. The fork removed "❔ Explain the options"
    /// on 2026-09-09: once a tap on "Let's talk" also closed its question, it read the two as the same
    /// gesture with two labels. The owner did not (2026-09-24, entry [123]): <i>"I more often use the
    /// explain in more details feature. Can we have a setting that lets us decide what buttons we want
    /// under the questions?"</i> — so <see cref="EXPLAIN_LABEL"/> is back, and neither is imposed.
    /// </para>
    /// <para>
    /// A question on a phone is compressed to a couple of lines, so the owner regularly needs the
    /// reasoning behind it before they can choose — and without a button the only way to ask is to
    /// type, which defeats the point of tappable options.
    /// </para>
    /// </summary>
    public const string TALK_LABEL = "💬 Let's talk";

    /// <summary>
    /// What the SUPERVISOR actually receives when that button is tapped. It is deliberately fuller
    /// than the label: the button is one tap, the instruction behind it has to be unambiguous.
    ///
    /// <para>
    /// AND IT ENDS BY RE-ASKING, which is the reverse of what it said before. The old text ordered
    /// the supervisor NOT to ask again, because the question was still live on the phone with its
    /// buttons — a second copy would have been the waterfall this policy exists to prevent
    /// (decision 14). The tap now closes the question, so there is no live copy left: a decision
    /// nobody re-asks is a decision that silently never gets taken, which is exactly what happened
    /// on 2026-09-09 to an orphaned-processes question the owner tapped and nobody ever decided.
    /// </para>
    /// </summary>
    public const string TALK_REQUEST =
        "The owner wants to talk this decision through before choosing. Their question closed when "
        + "they tapped, so nothing is live on their phone right now. Explain it in prose, briefly: "
        + "what each option actually means in practice, what differs between them, what it costs to "
        + "get wrong, and which one you recommend and why. Answer whatever they ask next. Then, once "
        + "the discussion has settled, ask the question again with fresh QUESTION:/OPTION: lines — "
        + "otherwise the decision is left dangling.";

    /// <summary>
    /// What the question message is edited to on that tap — the owner's own words for it: *"the
    /// message says 'ok, tell me what you have in mind'"*.
    ///
    /// <para>
    /// IT IS ENGLISH, like every other string the app itself writes (owner's rule, 2026-09-09).
    /// Decision 11 governs what a ROLE writes to the owner — that is in the owner's language — not
    /// what is hardcoded here.
    /// </para>
    /// </summary>
    public const string TALK_ACKNOWLEDGEMENT = "💬 Ok — tell me what you have in mind.";

    /// <summary>
    /// MASTER'S BUTTON, RESTORED VERBATIM (<c>a58ef7e</c>, where it was <c>MORE_DETAIL_LABEL</c>) — the other
    /// word of <c>questions.appButtons</c>
    /// (<see cref="Configuration.SettingsCatalog.QuestionAppButton_Names.EXPLAIN"/>), and the one the owner uses
    /// more often (entry [123]). Master's reason for it stands: a question on a phone is compressed to a couple
    /// of lines, so the owner regularly needs the reasoning behind it before they can choose — and without a
    /// button the only way to ask is to type, which defeats the point of tappable options.
    /// </summary>
    public const string EXPLAIN_LABEL = "❔ Explain the options";

    /// <summary>
    /// What the SESSION receives when that button is tapped — master's <c>MORE_DETAIL_REQUEST</c>, word for word.
    /// Fuller than the label because the button is one tap and the instruction behind it has to be unambiguous,
    /// and it ends by re-asking so the decision is not left dangling. Unlike <see cref="TALK_REQUEST"/> it asks
    /// for the explanation and the re-ask at once rather than opening a discussion — which is exactly the
    /// difference the owner wants to keep.
    /// </summary>
    public const string EXPLAIN_REQUEST =
        "Explain this decision before I choose: what each option actually means in practice, what "
        + "differs between them, what it costs to get wrong, and which one you recommend and why. "
        + "Keep it short. Then ask the question again.";

    /// <summary>
    /// What the question message is edited to on that tap, in <see cref="TALK_ACKNOWLEDGEMENT"/>'s style. MASTER
    /// CLOSED THE QUESTION ON THIS TAP TOO — the button was an ordinary option there, so the tap consumed its
    /// group, took the question off the open set and routed the request — but it recorded the tap by stamping
    /// "✅ &lt;the request text&gt;" on the message, the answered-choice record, for a tap that chose nothing. This
    /// line replaces that stamp; the closing is master's. English, like every string the app itself writes.
    /// </summary>
    public const string EXPLAIN_ACKNOWLEDGEMENT = "❔ Ok — explaining the options.";

    public static bool Carries_Question(string rawEntryText)
    {
        if (string.IsNullOrEmpty(rawEntryText))
            return false;

        return rawEntryText.Contains(QUESTION_MARKER, StringComparison.Ordinal)
            || rawEntryText.Contains(OPTION_MARKER, StringComparison.Ordinal);
    }
}
