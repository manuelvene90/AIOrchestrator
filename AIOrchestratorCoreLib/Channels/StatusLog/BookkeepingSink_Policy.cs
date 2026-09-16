using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.RoleRunnerConfig;

namespace AIOrchestratorCoreLib.Channels.StatusLog;

/// <summary>
/// THE SINK A SESSION ACTUALLY GETS, which is not always the one its config names.
///
/// <para>
/// A session only ever sees a note that left its channel through its STATE PACK or through the notes
/// riding its next turn, and both of those exist only where the app opens the turn: a bridge-driven
/// session (<c>PrintTurnExecutorModel</c> writes the pack before every turn), or a terminal session
/// the app tickets (<c>BridgeEngineModel.Sweep_WakeTickets_Async</c> writes it before the ticket —
/// 2026-09-15 plan 01, Task 11). A terminal session still on the fingerprint watcher has neither, and
/// its channel file is the whole of what it knows. So <see cref="BookkeepingSinks.Log"/> is REFUSED
/// there rather than honoured: the ledger advisory, the orphan report and the "your question was NOT
/// sent to the owner" notice would otherwise land in a file nothing reads, and the session would be
/// indistinguishable from one with nothing to be told.
/// </para>
/// <para>
/// THE QUESTION IS ASKED OF A SESSION, NOT OF A ROLE, and that is the whole of why this takes an
/// <see cref="IPrintSessionState"/>. config.json's <c>runners.&lt;role&gt;</c> block describes the
/// ROLE; whether the app opens THIS session's turns is <see cref="IPrintSessionState.DrivesTurns"/>,
/// a flag on the session's own file, and the two disagree in production. A member demoted to a
/// terminal keeps a role whose <c>runner</c> still reads <c>print</c>
/// (<c>OrchestrationLauncherModel.Demote_ToTerminal</c>): the dispatcher lets it go on the flag
/// (<c>PrintTurnDispatcherModel.Consider_Session</c>) and the ticket sweep never looks at it at all,
/// because the sweep only considers TERMINAL roles. Nobody writes that session a pack, and a policy
/// that read the role alone would have answered <see cref="BookkeepingSinks.Log"/> and made it deaf.
/// Both of those call sites already screen on the flag, in the same words ("DrivesTurns IS THE
/// INTERLOCK, not the config word"); this is the third reader of the same fact, not a new rule.
/// </para>
/// <para>
/// A POLICY AND NOT AN <c>if</c> AT THE ROUTER, for the reason every other policy in this repo gives:
/// the refusal is arguable, it has one line per reason, and it has to be testable without a session.
/// </para>
/// </summary>
public static class BookkeepingSink_Policy
{
    /// <summary>
    /// <paramref name="state"/> is the session's own state file, or null when it has none — and null
    /// REFUSES the log. "I could not find out whether this session is handed a pack" is not "it is
    /// safe": nothing writes a pack for a session the app has no state file for.
    /// </summary>
    public static BookkeepingSinks Resolve(IRoleRunnerConfig roleConfig, IPrintSessionState? state)
    {
        // THE POLICY ONLY EVER SUBTRACTS. A stated `channel` is never turned into a log by anything
        // here, whatever the session looks like — this key is the owner's off switch for the whole
        // one-wake-model bookkeeping series and it has to mean off.
        if (roleConfig.Bookkeeping == BookkeepingSinks.Channel)
            return BookkeepingSinks.Channel;

        if (state == null)
            return BookkeepingSinks.Channel;

        // The dispatcher opens this session's turns, so it writes it a pack at every one of them.
        if (state.DrivesTurns)
            return BookkeepingSinks.Log;

        // It does not, so the only thing that can still hand it a pack is the wake-ticket sweep — and
        // the sweep's own screen is this exact pair. The two conditions are written the same way on
        // purpose: if the sweep's screen ever changes, this one is wrong and the test that names the
        // three combinations is what says so.
        if (roleConfig.Runner == SessionRunners.Terminal && roleConfig.Wake == WakeModes.Ticket)
            return BookkeepingSinks.Log;

        return BookkeepingSinks.Channel;
    }

    /// <summary>
    /// The sentence the log writes when the policy overrides a stated sink — so the refusal is never
    /// silent — and NULL when nothing was refused, so a caller cannot report a refusal that did not
    /// happen. Each reason has its own sentence: "it is on the watcher" and "the app has no state
    /// file for it" are different faults with different fixes.
    /// </summary>
    public static string? Describe_Refusal_OrNull(SessionRoles role, IRoleRunnerConfig roleConfig, IPrintSessionState? state)
    {
        if (roleConfig.Bookkeeping == BookkeepingSinks.Channel || Resolve(roleConfig, state) == BookkeepingSinks.Log)
            return null;

        var name = SessionRole_Names.Get_ConfigKey(role);

        if (state == null)
            return $"'{name}' asks for bookkeeping in the status log, but the app has no state file for this session: "
                + "it is handed no state pack, so a note there would reach nobody. Bookkeeping stays in the channel.";

        return $"'{name}' asks for bookkeeping in the status log, but this session takes its own turns in a terminal on the "
            + "fingerprint watcher: it is handed no state pack, so a note there would reach nobody. Bookkeeping stays in "
            + "the channel until that role's `wake` is `ticket`.";
    }
}
