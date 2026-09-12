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

    /// <summary>
    /// THE ONE DEFINITION OF "BLANK IS ABSENT" FOR A SESSION-SCOPE OVERRIDE — the string the session
    /// actually states, or null when it states nothing. Blank is absent, the same rule the resolver
    /// applies to every other layer.
    ///
    /// <para>
    /// PUBLIC SINCE 2026-09-12 (task-7 fix round 1) because there is a SECOND reader of these same two
    /// override fields: <c>OrchestrationLauncherModel</c> resolves them against the role default at
    /// spawn time, and it did so with <c>??</c>, which catches null only. So
    /// <c>"supervisorEffortOverride": ""</c> — which <c>SessionJson_Serializer.Get_String_OrNull</c>
    /// returns verbatim to whoever hand-edits session.json — was absent to the catalogue and present
    /// to the launcher: one precedence with two descriptions that disagreed on one input, which is the
    /// drift CLAUDE.md decision 12 forbids. The deleted <c>SpawnCommand_Builder.Resolve_Effort_OrDefault</c>
    /// had said <c>IsNullOrWhiteSpace</c> too, so the launcher's <c>??</c> also quietly CHANGED that
    /// answer when the role default moved. Both readers now come through here rather than each
    /// spelling the rule out, which is the only shape in which they cannot drift again.
    /// </para>
    /// </summary>
    public static string? Stated_OrNull(string? text)
    {
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    static JsonNode? Text_OrNull(string? text)
    {
        var stated = Stated_OrNull(text);

        return stated == null ? null : JsonValue.Create(stated);
    }
}
