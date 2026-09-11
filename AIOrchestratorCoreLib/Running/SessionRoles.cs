using AIOrchestratorCoreLib.Sessions;

namespace AIOrchestratorCoreLib.Running;

/// <summary>
/// The six roles the kit ships a command for. A role is what a session IS; a
/// <see cref="Sessions.MemberKinds"/> is the subset of roles that own a spoke channel. Both exist
/// because the launcher spawns by role (a supervisor is not a member) while the roster reasons by
/// kind — this enum is the one the runner configuration is keyed on.
/// </summary>
public enum SessionRoles
{
    Supervisor,
    Implementer,
    Reviewer,
    Solo,
    General,
    Communicator,
}

/// <summary>
/// The words a role travels under: the config.json key, the <c>AIORCH_ROLE</c> value the status
/// line and hooks read (spelled exactly as <c>SpawnCommand_Builder</c> has always set it), and the
/// role slash command that boots the session.
/// </summary>
public static class SessionRole_Names
{
    public static readonly IReadOnlyList<SessionRoles> ALL =
        [SessionRoles.Supervisor, SessionRoles.Implementer, SessionRoles.Reviewer, SessionRoles.Solo, SessionRoles.General, SessionRoles.Communicator];

    public static string Get_ConfigKey(SessionRoles role)
    {
        return role switch
        {
            SessionRoles.Supervisor => "supervisor",
            SessionRoles.Implementer => "implementer",
            SessionRoles.Reviewer => "reviewer",
            SessionRoles.Solo => "solo",
            SessionRoles.General => "general",
            SessionRoles.Communicator => "communicator",
            _ => throw new Exception($"Unhandled SessionRoles: {role}"),
        };
    }

    /// <summary>The AIORCH_ROLE value — identical to the config key today, kept separate because they may not stay so.</summary>
    public static string Get_EnvWord(SessionRoles role) => Get_ConfigKey(role);

    public static SessionRoles? Parse_OrNull(string? word)
    {
        foreach (var role in ALL)
        {
            if (string.Equals(Get_ConfigKey(role), word?.Trim(), StringComparison.OrdinalIgnoreCase))
                return role;
        }

        return null;
    }

    public static SessionRoles From_MemberKind(MemberKinds kind)
    {
        return kind switch
        {
            MemberKinds.Implementer => SessionRoles.Implementer,
            MemberKinds.Reviewer => SessionRoles.Reviewer,
            MemberKinds.Solo => SessionRoles.Solo,
            _ => throw new Exception($"Unhandled MemberKinds: {kind}"),
        };
    }

    /// <summary>
    /// The role command with its argument, exactly as the terminal spawn has always passed it as
    /// the first prompt: <c>/implementer &lt;orch&gt;/&lt;member&gt;</c>, <c>/supervisor &lt;orch&gt;</c>,
    /// <c>/solo &lt;orch&gt;</c>, <c>/general-supervisor</c>.
    /// </summary>
    public static string Build_RoleCommand(SessionRoles role, string orchId, string memberId)
    {
        return role switch
        {
            SessionRoles.Supervisor => $"/supervisor {orchId}",
            SessionRoles.Implementer => $"/implementer {orchId}/{memberId}",
            SessionRoles.Reviewer => $"/reviewer {orchId}/{memberId}",
            SessionRoles.Solo => $"/solo {orchId}",
            SessionRoles.General => "/general-supervisor",
            SessionRoles.Communicator => $"/communicator {orchId}",
            _ => throw new Exception($"Unhandled SessionRoles: {role}"),
        };
    }

    /// <summary>The author word a session of this role signs its entries with.</summary>
    public static Channels.ChannelAuthors Get_Author(SessionRoles role)
    {
        return role switch
        {
            SessionRoles.Supervisor => Channels.ChannelAuthors.Supervisor,
            SessionRoles.Implementer => Channels.ChannelAuthors.Implementer,
            SessionRoles.Reviewer => Channels.ChannelAuthors.Reviewer,
            SessionRoles.Solo => Channels.ChannelAuthors.Solo,
            SessionRoles.General => Channels.ChannelAuthors.Supervisor,
            SessionRoles.Communicator => Channels.ChannelAuthors.Communicator,
            _ => throw new Exception($"Unhandled SessionRoles: {role}"),
        };
    }
}
