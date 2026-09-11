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
    /// WHETHER A SUPERVISOR ENTRY REACHES THE PHONE — and since 2026-09-09 the answer is YES, for
    /// every entry the supervisor writes on the owner channel.
    ///
    /// <para>
    /// WHAT THIS USED TO DO, AND WHY IT IS GONE. It let through a question, an answer the owner was
    /// waiting for, a <c>BLOCKED ON OWNER</c>, a file, and the boot greeting — and suppressed
    /// everything else as "progress narration", on the owner's earlier words about a waterfall of
    /// messages. It worked exactly as designed and produced the opposite of what they wanted: their
    /// own quoted example of a message they NEEDED — *"La regola ora è completa…"* — was suppressed
    /// here and reached them five minutes late, through the silent-deadlock net, in raw Markdown.
    /// Meanwhile the noise they were actually drowning in came from the APP: a fifteen-line STATUS
    /// every half hour, a receipt sentence under every message, false stall alerts.
    /// </para>
    /// <para>
    /// THE OWNER'S RULING, 2026-09-09: *"If the supervisor writes to me, I must know it — that
    /// rings. Status, receipts and app bookkeeping do not ring."* So the brake on chatter is no
    /// longer a filter that guesses which of the supervisor's words matter; it is the SKILL (write
    /// to the owner only what they must know) plus the brevity nudge that already measures every
    /// entry. A filter cannot tell a thought from a status line, and the one it suppressed by
    /// mistake was the one that mattered.
    /// </para>
    /// <para>
    /// ONE EXCEPTION SURVIVES, and it is not narration filtering: the owner's own words quoted back
    /// at them (<see cref="Is_OwnerRestatement"/>). That says nothing they did not just type, and it
    /// spent their wait — the real answer that followed then read as narration.
    /// </para>
    /// </summary>
    public static bool Should_Push(string rawEntryText, bool ownerIsWaitingForAReply, string? subject = null)
    {
        // An entry with no body is nothing to read. Not a filter — a guard against sending an empty
        // message, which Telegram refuses anyway.
        if (string.IsNullOrWhiteSpace(rawEntryText))
            return false;

        return !Is_OwnerRestatement(rawEntryText);
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
    /// Added to EVERY question automatically, and it is now the ONLY button the app contributes.
    ///
    /// <para>
    /// THERE WERE TWO, AND THE SECOND ONE EARNED ITS REMOVAL. "❔ Explain the options" spent the
    /// buttons and asked the supervisor to explain and re-ask; "💬 Let's talk" left the question and
    /// its keyboard exactly where they were. Once a tap on "Let's talk" also closes its question —
    /// which is what the owner asked for, having tapped a mute button twelve times in one afternoon
    /// — the two are the same gesture with two labels, and offering both only makes the owner
    /// choose between synonyms before they can ask their real question.
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

    public static bool Carries_Question(string rawEntryText)
    {
        if (string.IsNullOrEmpty(rawEntryText))
            return false;

        return rawEntryText.Contains(QUESTION_MARKER, StringComparison.Ordinal)
            || rawEntryText.Contains(OPTION_MARKER, StringComparison.Ordinal);
    }
}
