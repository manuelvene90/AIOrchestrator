using System.Text;
using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.TurnSource;

namespace AIOrchestratorCoreLib.Running;

/// <summary>
/// The stdin prompt of a resumed turn. It names the request, lists the entries that triggered it
/// (verbatim — one tool call saved, and the transcript then holds them), restates the one rule the
/// role command's bridge-driven paragraph states (the final message IS the entry), and — after a bridge
/// restart only — opens with the turns already executed, so a session whose transcript remembers
/// answering does not answer twice (CLAUDE.md decision 8, defended where the effect is).
///
/// <para>
/// A SESSION WITH ONE CHANNEL GETS EXACTLY THE PROMPT IT ALWAYS GOT. The multi-channel half — the
/// per-entry labels and the <c>TO:</c> protocol — appears only for a session that actually has more than
/// one source, which today is the orchestration supervisor and nothing else. Teaching an implementer an
/// addressing format it can only misuse would buy a cut-in-half report the first time it wrote a line
/// beginning "to:".
/// </para>
/// </summary>
public static class PrintTurnPrompt_Builder
{
    public const string ALREADY_EXECUTED_PREFIX = "Turns already executed by the bridge for this session:";

    /// <summary>Opens the block of entries that came from one channel. The session answers with <see cref="TurnReply_Splitter.TO_MARKER"/>.</summary>
    public const string SOURCE_LABEL_PREFIX = "--- from ";

    /// <summary>
    /// Opens the stdin prompt of a FRESH turn — a session that has just run its role command and
    /// holds no transcript (<see cref="ResumeModes.Fresh"/>). Measured 2026-09-08 on the one role
    /// that already ran fresh, the general supervisor: 9.2 model calls per turn, 7.8 of them reading
    /// channel files to find out what the bridge already knew was pending — each call paying the
    /// whole boot again. The entries travel with the prompt instead, exactly as a resumed turn gets
    /// them; the channel stays the history to consult for a fact, not the to-do list to rediscover.
    /// </summary>
    public const string FRESH_SESSION_PREAMBLE =
        "You are a FRESH session on an existing channel: your role command has just run and you hold no memory of earlier turns. " +
        "The entries below are your pending traffic — the bridge tracked them, so do not re-derive them from the channel; read the channel only for a fact you need from the history.";

    /// <summary>
    /// A member's boot sequence files an "<c>imp-1 online</c>" entry, and the bridge mirrors that
    /// subject to the owner's phone. Correct once per session in transcript mode; in fresh mode
    /// every turn is a session, and the phone would read "online" at every turn all morning.
    /// The general supervisor is the exception: its greeting per launch is an owner directive
    /// (2026-08-25, "I can't know when I can start writing on telegram"), so it keeps it.
    /// </summary>
    public const string FRESH_SESSION_NO_GREETING =
        "Do not file the online greeting your boot sequence describes — this channel already carries your earlier entries, and a greeting per turn is noise on the owner's phone.";

    /// <summary>
    /// MEASURED 2026-09-09 (2.1.263): stdin is APPENDED to the positional prompt in the same message,
    /// so this text landed inside the slash command's <c>$ARGUMENTS</c> — the fourth outcome the three
    /// below did not foresee (stdin ignored / both delivered / stdin prepended). The fresh turn's memory
    /// therefore travels in a FILE (<see cref="StatePack.StatePack_Builder"/>) and nothing goes on stdin;
    /// <see cref="Build_FreshSession"/> is no longer called by the executor and stays only until the next
    /// stage removes it with its tests. The live test that pinned the shape:
    /// <c>Print_SlashCommandPositional_PlusStdin_IsAppendedIntoArguments_AndBothExecute</c>.
    /// </summary>
    public const string FRESH_SESSION_ROLE_FALLBACK = "If your role command {0} has not run at the start of this message, invoke it now with the Skill tool before acting on anything below.";

    public static string Build_FreshSession(string requestId, string roleCommand, IReadOnlyList<PendingEntry> pending, IReadOnlyList<ITurnSource> sources, bool greetsOnBoot)
    {
        if (pending.Count == 0)
            throw new ArgumentException("A fresh-session prompt carries pending entries; a turn nobody triggered is the boot turn, which takes no stdin prompt", nameof(pending));

        var prompt = new StringBuilder();
        prompt.Append("[bridge turn ").Append(requestId).Append("]\n");
        prompt.Append(FRESH_SESSION_PREAMBLE);
        prompt.Append(' ').Append(string.Format(FRESH_SESSION_ROLE_FALLBACK, $"`{roleCommand}`"));
        if (!greetsOnBoot)
            prompt.Append(' ').Append(FRESH_SESSION_NO_GREETING);
        prompt.Append("\n\n");

        var multiSource = sources.Count > 1;
        if (multiSource)
            Append_MultiSourceTraffic(prompt, pending);
        else
            Append_SingleSourceTraffic(prompt, pending);

        prompt.Append(multiSource ? Describe_MultiSourceContract(sources) : SINGLE_SOURCE_CONTRACT);
        return prompt.ToString();
    }

    public static string Build_FollowUp(string requestId, IReadOnlyList<PendingEntry> pending, IReadOnlyList<int> alreadyExecutedTurns, IReadOnlyList<ITurnSource> sources)
    {
        var prompt = new StringBuilder();

        prompt.Append("[bridge turn ").Append(requestId).Append("]\n");

        if (alreadyExecutedTurns.Count > 0)
        {
            prompt.Append(ALREADY_EXECUTED_PREFIX).Append(' ')
                .Append(string.Join(", ", alreadyExecutedTurns))
                .Append(" — their work is done and their entries are in your channel; do not repeat it. This turn is a resume after a bridge restart.\n");
        }

        var multiSource = sources.Count > 1;

        if (pending.Count == 0)
            prompt.Append(BOOT_TURN).Append('\n');
        else if (multiSource)
            Append_MultiSourceTraffic(prompt, pending);
        else
            Append_SingleSourceTraffic(prompt, pending);

        prompt.Append(multiSource ? Describe_MultiSourceContract(sources) : SINGLE_SOURCE_CONTRACT);

        return prompt.ToString();
    }

    /// <summary>
    /// THE PROMPT OF A TURN NOBODY TRIGGERED. The dispatcher runs exactly one of these per session that
    /// owns an orchestration's owner channel, so it can greet before the owner has a topic to type into
    /// (<see cref="PrintTurnDispatcher.PrintTurnDispatcherModel"/> explains why that deadlock exists).
    ///
    /// <para>
    /// A stream session that boots on this turn never sees this line — its role command IS its whole
    /// first message and the executor stops there. It is reached by the two paths where the role command
    /// is not enough on its own: a stream process that is already alive (a boot turn retried after a
    /// failure), and the print fallback, whose positional role command has to be answered by SOMETHING on
    /// stdin. Saying "0 entries" there is how a session comes to file an entry about nothing.
    /// </para>
    /// </summary>
    public const string BOOT_TURN = "Nothing has been said to you yet — this is your boot turn. Follow the boot sequence of your role command: read your channels, then file your greeting.";

    /// <summary>
    /// The pending-traffic block and the contract, as free-standing text — for the state pack
    /// (<see cref="StatePack.StatePack_Builder"/>), which writes them into a file instead of a
    /// prompt. One renderer for both roads, so a fresh session and a resumed one read their entries
    /// in exactly the same shape; a second copy is how the two would come to disagree.
    /// </summary>
    public static string Describe_Traffic(IReadOnlyList<PendingEntry> pending, IReadOnlyList<ITurnSource> sources)
    {
        var text = new StringBuilder();

        if (sources.Count > 1)
            Append_MultiSourceTraffic(text, pending);
        else
            Append_SingleSourceTraffic(text, pending);

        return text.ToString();
    }

    public static string Describe_Contract(IReadOnlyList<ITurnSource> sources)
    {
        return sources.Count > 1 ? Describe_MultiSourceContract(sources) : SINGLE_SOURCE_CONTRACT;
    }

    const string SINGLE_SOURCE_CONTRACT =
        "Act on it per your role command. Your final message IS your channel entry — the bridge appends it under your author word with the header, the index and the time: first line the subject, then a blank line, then the body. A question ends the turn exactly as an answer does.\n";

    /// <summary>
    /// Said whenever an app note rides the turn (<see cref="PrintTurn_Trigger.Select_AgentNotes"/>): the
    /// session must know these are the app's words to it, not the owner's, and that they did not start
    /// this turn — or it answers a nudge on the owner's phone as though the owner had written it.
    /// </summary>
    public const string AGENT_NOTES_LINE =
        "Entries FROM app tagged [agent] are the app's notes to you, not the owner's words — they never start a turn, so they ride this one. Take them into account; never answer them as though the owner had spoken.\n\n";

    static void Append_AgentNotesLine_IfAny(StringBuilder prompt, IReadOnlyList<PendingEntry> pending)
    {
        if (pending.Any(item => PrintTurn_Trigger.Is_AgentNote(item.Entry)))
            prompt.Append(AGENT_NOTES_LINE);
    }

    static void Append_SingleSourceTraffic(StringBuilder prompt, IReadOnlyList<PendingEntry> pending)
    {
        prompt.Append("New traffic in your channel — ").Append(pending.Count).Append(pending.Count == 1 ? " entry" : " entries").Append(":\n\n");

        foreach (var item in pending)
            prompt.Append(item.Entry.RawText.Trim()).Append("\n\n");

        Append_AgentNotesLine_IfAny(prompt, pending);
    }

    /// <summary>
    /// Every entry under the name of the channel it arrived on, in the order the orderer settled
    /// (<see cref="PendingTraffic_Orderer"/>). Consecutive entries from one channel share a label rather
    /// than repeating it, so a member that filed three entries reads as one report and not three.
    /// </summary>
    static void Append_MultiSourceTraffic(StringBuilder prompt, IReadOnlyList<PendingEntry> pending)
    {
        var channelCount = pending.Select(item => item.Source.Key).Distinct().Count();

        prompt.Append("New traffic on ").Append(channelCount).Append(channelCount == 1 ? " channel, " : " channels, ")
            .Append(pending.Count).Append(pending.Count == 1 ? " entry" : " entries").Append(", oldest first:\n\n");

        string? openLabel = null;

        foreach (var item in pending)
        {
            if (item.Source.Key != openLabel)
            {
                prompt.Append(SOURCE_LABEL_PREFIX).Append(item.Source.Key).Append(" (").Append(Path.GetFileName(item.Source.ChannelFilePath)).Append(") ---\n\n");
                openLabel = item.Source.Key;
            }

            prompt.Append(item.Entry.RawText.Trim()).Append("\n\n");
        }

        Append_AgentNotesLine_IfAny(prompt, pending);
    }

    static string Describe_MultiSourceContract(IReadOnlyList<ITurnSource> sources)
    {
        var addressable = string.Join(", ", sources.Select(source => source.Key));
        var ownerKey = sources.FirstOrDefault(source => source.IsOwnerChannel)?.Key ?? TurnSource_Factory.OWNER_KEY;

        return
            "Act on it per your role command. Your final message IS your channel entries — the bridge appends each part under your author word with the header, the index and the time.\n" +
            $"ADDRESS EACH PART: a line reading `{TurnReply_Splitter.TO_MARKER} <channel>` on its own opens a block that runs to the next such line. The channels you can address this turn: {addressable}. " +
            $"Text before the first `{TurnReply_Splitter.TO_MARKER}` goes to `{ownerKey}`, and so does a block addressed to a channel that is not on that list — with a note saying where you meant it to go.\n" +
            "Inside a block: first line the subject, then a blank line, then the body. Answer a member in ITS channel — a verdict written anywhere else does not reach the session it is about. A question ends the turn exactly as an answer does.\n";
    }
}
