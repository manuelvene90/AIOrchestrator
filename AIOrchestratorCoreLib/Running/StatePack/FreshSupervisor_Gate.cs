using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.StatePack;

/// <summary>
/// MAY THIS SESSION BE STARTED WITH NO MEMORY OF ITS OWN? For a member, always: its channel is its
/// durable state by design (CLAUDE.md decision 8) and its brief is an entry in it. For a SUPERVISOR
/// or a SOLO — the two roles that own an endeavour — only once it has somewhere to have put its
/// conclusions.
///
/// <para>
/// WHY IT EXISTS. Every other section of the state pack is a fact read from disk and survives the
/// transcript: the brief is an entry, the ledger is PLAN.md, the code state is git. A CONCLUSION
/// survives nothing — *"we tried that route and it cannot work"* was reasoned once, acted on, and
/// written down nowhere. The failure is silent: a fresh supervisor with no conclusions file
/// re-proposes a dead end, an implementer spends a day on it, no test is red and no alert fires.
/// </para>
/// <para>
/// WHY THE APP AND NOT A HOOK (decision 21). A hook advises and a session can unwire it; the
/// enforcement that must actually hold belongs where the effect is. And it REFUSES rather than
/// warning after the fact, because an alert about memory that has already been discarded is a
/// post-mortem. The cost of refusing is that the session keeps its transcript for another turn,
/// which is exactly today's behaviour and therefore costs nothing that is not already being paid.
/// </para>
/// </summary>
public static class FreshSupervisor_Gate
{
    /// <summary>
    /// The subject of the one entry this gate files. Matched on read-back, which is how "have I
    /// already said this" is answered — see <see cref="Has_AlreadySaidIt"/>.
    /// </summary>
    public const string NOTICE_SUBJECT = "your conclusions file is empty, so you are still running on your transcript";

    static readonly HashSet<string> _said = [];
    static readonly Lock _lock = new();

    public static bool Allows(ISupervisionPaths paths, IPrintSessionState state, IOrchestrationLog log)
    {
        // THE TWO ROLES THAT OWN AN ENDEAVOUR. A member is never gated — its channel IS its state.
        // Nor is the GENERAL supervisor, which is the one role shipping `resume: fresh` by default
        // and is stateless across launches by owner directive: gating it would stop the only session
        // already running the regime this is all trying to reach.
        if (state.Role is not (SessionRoles.Supervisor or SessionRoles.Solo))
            return true;

        var file = StatePack_Locator.Get_ConclusionsFile_OrNull(paths, state.Role, state.OrchId, state.MemberId);

        if (file != null && Has_Content(file))
            return true;

        Say_Once(paths, state, log, file);

        return false;
    }

    /// <summary>
    /// Drops the in-process memory of what has been said — what a restart does to it, and what a test
    /// needs in order to prove that the ONCE survives one. The channel is the durable record; this
    /// set is only the cheap path that avoids re-reading it every turn.
    /// </summary>
    public static void Forget_EverythingItHasSaid()
    {
        lock (_lock)
            _said.Clear();
    }

    /// <summary>
    /// UNREADABLE COUNTS AS EMPTY, deliberately. A guard that cannot evaluate its predicate must not
    /// invent a permission it cannot justify (decision 21); refusing keeps today's behaviour, and the
    /// log line below names which predicate failed rather than saying "gate error".
    /// </summary>
    static bool Has_Content(string file)
    {
        try
        {
            return File.Exists(file) && File.ReadAllText(file).Trim().Length > 0;
        }
        catch
        {
            return false;
        }
    }

    static void Say_Once(ISupervisionPaths paths, IPrintSessionState state, IOrchestrationLog log, string? file)
    {
        // KEYED ON THE CHANNEL FILE, not on "<orchId>/<memberId>". The notice lives in a particular
        // file, and two orchestrations that share a logical id under different supervision roots are
        // different things — which is not a hypothetical: it is every test of this class, and it made
        // the first one to run spend the key for all the others.
        var channel = paths.Get_OwnerChannelFile(state.OrchId);
        var key = channel;

        lock (_lock)
        {
            if (_said.Contains(key))
                return;
        }

        // THE DURABLE ANSWER IS THE CHANNEL, not the set above. An in-process set is lost at every
        // bridge restart, and this notice would then be re-filed at every boot — a waterfall with a
        // longer period, which is still a waterfall (decision 14). The set is only the cheap path
        // that keeps the common case from re-reading a channel every turn.
        if (Has_AlreadySaidIt(channel))
        {
            Remember(key);
            return;
        }

        log.Log_Warning(state.OrchId, $"'{state.MemberId}': configured resume: fresh, but {file ?? "its conclusions file"} is missing or empty — kept on its transcript, because going fresh would drop everything it has concluded and nothing would say so");

        var filed = ChannelAppender.Append_AppEntry(
            channel,
            // AGENT, never Owner: the owner cannot act on this and the supervisor can, by writing the
            // file (decision 15). It rides the session's next turn as an app note.
            AppEntryAudiences.Agent,
            NOTICE_SUBJECT,
            $"This orchestration is configured `resume: fresh`, which would make each of your turns a new session with no memory of the last. "
            + $"The bridge hands a fresh session its brief, the ledger, the code state and the entries that woke it — but NOT what you have CONCLUDED. "
            + $"Write those to {file}: dead ends with their dates and the reason they cannot work, rulings you do not want re-litigated, owner decisions that are not in PLAN.md. "
            + "Until that file has something in it you keep your transcript, which costs tokens and is the safe side of the trade.",
            DateTime.Now);

        // SPENT ONLY ON A WRITE THAT HAPPENED — the discipline BudgetAlert_Planner exists to teach:
        // a token spent before a failed append loses the message for ever.
        if (filed)
            Remember(key);
    }

    static bool Has_AlreadySaidIt(string channelFilePath)
    {
        try
        {
            if (!File.Exists(channelFilePath))
                return false;

            // Live AND archive (decision 13): a compacted channel's live file is not the history, and
            // a count taken from it alone would re-file this notice after every compaction.
            return ChannelHistory_Counter.Read_AllEntries(channelFilePath)
                .Any(entry => entry.Author == ChannelAuthors.App && entry.Subject.Contains(NOTICE_SUBJECT, StringComparison.Ordinal));
        }
        catch
        {
            // Cannot tell => say nothing new. A channel that cannot be read is not evidence that the
            // notice is absent, and a duplicate here is noise in the one place noise is expensive.
            return true;
        }
    }

    static void Remember(string key)
    {
        lock (_lock)
            _said.Add(key);
    }
}
