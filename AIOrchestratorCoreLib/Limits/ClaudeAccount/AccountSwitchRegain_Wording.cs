namespace AIOrchestratorCoreLib.Limits.ClaudeAccount;

/// <summary>
/// THE WAKE-UP EVERY SESSION GETS WHEN THE CLAUDE ACCOUNT CHANGES — the REGAIN step (owner, 2026-09-30,
/// ai-orchestrator-32 entries [57] and [62]).
///
/// <para>
/// WHY IT EXISTS. The owner runs several accounts and logs in to another when one runs out. On the new
/// account a conversation keeps every visible message, tool call and tool result, but the THINKING
/// behind them is not carried over — the owner observed it on 2026-09-30, and Anthropic documents
/// thinking blocks as signed and, for some models, bound to the account that produced them.
/// A session resumed that way (a supervisor or solo respawned with <c>claude --resume</c>, or any
/// session still running when the login changed) remembers WHAT it wrote but not WHY, nor what it had
/// decided to do next. The ordinary GO AHEAD says "pick up exactly where you left off", which is the
/// one instruction such a session cannot follow safely: it will act on a sense of its plan that is now
/// a reconstruction. The owner's words: <i>"make sure that when we do /resume the sessions are advised
/// to regain all the context that was lost before restarting to make sure not to lose work or not to
/// lose decisions that were made."</i>
/// </para>
/// <para>
/// THE OWNER CHOSE "RESUME + REBUILD" (entry [62]) over restarting fresh: a resumed conversation keeps
/// every message and tool result, a fresh one only what is on disk. So the conversation is kept and this
/// entry makes the session rebuild the part that was lost before it acts.
/// </para>
/// <para>
/// THE STEPS LIVE HERE, ONCE, and the role skills point at this entry rather than repeating them —
/// several copies of one rule is how they drift apart.
/// </para>
/// </summary>
public static class AccountSwitchRegain_Wording
{
    /// <summary>
    /// Starts with "GO AHEAD" on purpose: every role already treats a GO AHEAD from the app as its cue to
    /// act, so a session that knows nothing of this entry still reads it as "move" — and then finds the
    /// steps in its body.
    /// </summary>
    public const string SUBJECT = "GO AHEAD — REGAIN after an account switch";

    /// <summary>The marker a session writes when it has rebuilt its context. Read as a SUBJECT prefix.</summary>
    public const string REGAINED_MARKER = "REGAINED";

    public static string Build_Body(DateTime switchedAtLocal)
    {
        return $"The Claude account was switched at {switchedAtLocal:HH:mm} (a usage limit, or the owner changed login). "
            + "On the new account YOUR EARLIER REASONING IS GONE: your messages and tool results are still in your "
            + "conversation, the thinking behind them is not. Do not trust your sense of what you were about to do — "
            + "rebuild it before any edit, commit, merge, push, request file or message.\n\n"
            + "1. REBUILD. Re-read this channel from your last brief or HANDOVER (not only from your last entry), "
            + "PLAN.md (the ledger AND the OWNER REQUESTS table), your notes file (state.md / progress.md) if you keep "
            + "one, and your tree: `git status`, `git log --oneline -10`, `git stash list`, `git worktree list`.\n"
            + "2. VERIFY WHAT WAS IN FLIGHT. Every commit, merge, push, request file or channel entry you were in the "
            + "middle of: check whether it landed (a request is confirmed by a FROM app entry). Never redo a step from "
            + "memory — a half-done one is finished or reported, not repeated. Background jobs and monitors did not "
            + "survive: re-arm your monitor.\n"
            + $"3. POST ONE ENTRY whose subject starts {REGAINED_MARKER}: where the work stands, your next step, and "
            + "the owner's decisions still in force. If a decision cannot be rebuilt from what is on disk, ask — never "
            + "guess it.\n\n"
            + "Then carry on. If you were idle with nothing in flight, the entry is one line and you go back to waiting.";
    }
}
