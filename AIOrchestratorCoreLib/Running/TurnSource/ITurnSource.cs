namespace AIOrchestratorCoreLib.Running.TurnSource;

/// <summary>
/// ONE CHANNEL A BRIDGE-DRIVEN SESSION IS WOKEN BY — and, because they are the same thing, the
/// channel its answer to that traffic is written back into.
///
/// <para>
/// A member has exactly one: its own spoke. An orchestration supervisor has N+1 — the owner channel
/// plus every open member's spoke — which is what the terminal supervisor's watcher always fingerprinted
/// (<c>kit/commands/supervisor.md</c>: <c>imp-*/channel.md</c>, <c>rev-*/channel.md</c>,
/// <c>owner-channel.md</c>) and what the bridge had to grow before it could drive that role for real.
/// </para>
/// <para>
/// SOURCE AND REPLY TARGET ARE ONE FIELD ON PURPOSE. A verdict written anywhere but the spoke it
/// answers does not clear <see cref="Status.MemberStates.AwaitingSupervisorReview"/>, because
/// <see cref="Status.MemberState_Resolver.Is_AwaitingVerdict"/> anchors on the last supervisor entry
/// IN THAT MEMBER'S CHANNEL. A bridge that read the spokes but answered into the owner channel would
/// leave every member awaiting review for ever and earn a supervisor nudge every eight minutes — the
/// exact loop the owner has already complained about twice.
/// </para>
/// </summary>
public interface ITurnSource
{
    /// <summary>
    /// The word the session addresses this channel by (<c>owner</c>, <c>imp-1</c>, <c>rev-2</c>) and
    /// the key its cursor is stored under. Stable for the life of the session: a member id is never
    /// reused, and the owner channel is always <c>owner</c> whichever role is reading it.
    /// </summary>
    string Key { get; }

    string ChannelFilePath { get; }

    /// <summary>
    /// Whether this is the conversation with the OWNER — the owner channel of an orchestration, or the
    /// general supervisor's own. It carries the priority rule and it is the default target for
    /// reply text a session did not address to anybody.
    /// </summary>
    bool IsOwnerChannel { get; }
}
