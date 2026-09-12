using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingDefinition;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;

namespace AIOrchestratorCoreLib.Configuration.SettingsCatalog;

/// <summary>
/// The session.json value behind an Orchestration-scope catalogue path, or null when the session
/// says nothing. A SWITCH rather than reflection or a name convention: session.json's field names
/// and the catalogue's paths are two vocabularies that agree today by hand, and a convention would
/// make a rename on either side fail silently at runtime instead of loudly at compile time.
///
/// <para>
/// A SOLO SITS ON THE IMPLEMENTER SLOT, as it does for the model dial — that is the existing
/// semantic of ImplementerEffortOverride (one implementer-side override covers every member kind),
/// and it is restated here rather than changed.
/// </para>
/// </summary>
public static class SessionScoped_Reader
{
    public static JsonNode? Read_OrNull(ISettingDefinition definition, IOrchestrationSession session)
    {
        return definition.Path switch
        {
            "models.supervisor" => Text_OrNull(session.SupervisorModelOverride),
            "models.implementer" => Text_OrNull(session.ImplementerModelOverride),
            "effort.supervisor" => Text_OrNull(session.SupervisorEffortOverride),
            "effort.implementer" => Text_OrNull(session.ImplementerEffortOverride),
            "session.paused" => JsonValue.Create(session.Paused),
            "session.telegramMode" => JsonValue.Create(session.TelegramMode.ToString()),
            "session.ownerPresence" => JsonValue.Create(session.OwnerPresence.ToString()),
            _ => null,
        };
    }

    /// <summary>Blank is absent, the same rule the resolver applies to every other layer.</summary>
    static JsonNode? Text_OrNull(string? text)
    {
        return string.IsNullOrWhiteSpace(text) ? null : JsonValue.Create(text);
    }
}
