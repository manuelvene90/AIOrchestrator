using AIOrchestratorCoreLib.Running;

namespace AIOrchestratorCoreLib.Configuration.EffortSettings;

internal sealed class EffortSettingsModel(IReadOnlyDictionary<SessionRoles, string?> levelsByRole) : IEffortSettings
{
    readonly IReadOnlyDictionary<SessionRoles, string?> _levelsByRole = levelsByRole;

    /// <summary>
    /// A MISSING ROLE THROWS RATHER THAN READING AS NULL, the same direction
    /// <c>OrchestratorConfigModel.Get_ModelForRole</c>'s switch takes for the same reason: null is a
    /// real answer here ("emit no flag"), so an unknown role answering null would be indistinguishable
    /// from a role the owner deliberately left alone. The factory fills every role in
    /// <see cref="SessionRole_Names.ALL"/>, so this can only fire for a role the enum grew and the
    /// factory did not — which is the moment someone must be told.
    /// </summary>
    public string? Get_ForRole_OrNull(SessionRoles role)
    {
        if (!_levelsByRole.TryGetValue(role, out var level))
            throw new Exception($"Unhandled SessionRoles: {role} — no effort level was resolved for it");

        return level;
    }
}
