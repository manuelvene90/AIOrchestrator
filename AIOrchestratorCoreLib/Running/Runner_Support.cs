namespace AIOrchestratorCoreLib.Running;

/// <summary>
/// WHICH RUNNER CAN RUN WHICH ROLE IN THIS STAGE — one table, read by the launcher (what to spawn),
/// the watchdog (which slots have no pid file by design) and the dispatcher (which registrations
/// still mean anything). Three readers of one rule; when they disagreed the symptom was two
/// sessions answering one brief.
///
/// <para>
/// THE LIMIT WAS NEVER THE TRANSPORT, IT WAS THE TRIGGER, and the trigger is no longer single-channel.
/// A bridge-driven session is woken by every channel <see cref="TurnSource.TurnSources_Resolver"/> names
/// for its role — one for a member, the owner channel plus every open spoke for an orchestration
/// supervisor — so the supervisor is now supported on BOTH bridge-driven transports rather than only on
/// the one that happened to be added with it. What remains unsupported is the COMMUNICATOR, and for a
/// reason no amount of channel-watching answers: its input is the supervisor's transcript, not a channel
/// at all.
/// </para>
/// <para>
/// <see cref="SessionRunners.Bg"/> supports NOTHING here: the transport is not implemented in this
/// stage (its wake-up needs the messaging socket, deliberately out of scope), so a role configured
/// for it is launched in a terminal with a warning rather than silently doing nothing.
/// </para>
/// </summary>
public static class Runner_Support
{
    public static bool Supports(SessionRunners runner, SessionRoles role)
    {
        return runner switch
        {
            SessionRunners.Terminal => true,

            // Print and stream differ in cost and latency, never in what they can be woken by: the
            // dispatcher owns the trigger and hands both executors the same pending entries. A table
            // that let them differ would be a second answer to "which channels wake this session".
            SessionRunners.Print or SessionRunners.Stream => role is SessionRoles.Implementer or SessionRoles.Reviewer or SessionRoles.Solo or SessionRoles.General or SessionRoles.Supervisor,
            SessionRunners.Bg => false,
            _ => false,
        };
    }

    /// <summary>
    /// Runners whose sessions are driven by the bridge rather than by a shell: no pid file, no
    /// window, no watcher. The watchdog exempts these slots and the dispatcher owns them.
    /// </summary>
    public static bool Is_BridgeDriven(SessionRunners runner)
    {
        return runner is SessionRunners.Print or SessionRunners.Stream;
    }

    public static string Describe_Unsupported(SessionRunners runner, SessionRoles role)
    {
        var supported = string.Join(", ", SessionRole_Names.ALL.Where(known => Supports(runner, known)).Select(SessionRole_Names.Get_ConfigKey));
        var what = supported.Length == 0 ? "no role in this stage" : $"only {supported}";

        return $"role '{SessionRole_Names.Get_ConfigKey(role)}' is configured runner: {SessionRunner_Names.Get_Word(runner)}, which this stage supports for {what} — launched in a terminal instead";
    }
}
