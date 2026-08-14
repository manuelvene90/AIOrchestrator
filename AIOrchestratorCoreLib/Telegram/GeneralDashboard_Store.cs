using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration;

namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// Remembers WHICH message in General is the dashboard, across app restarts.
///
/// It has to survive a restart or the feature defeats itself: the remembered TEXT lives in memory, so
/// a restart with no stored id posts a SECOND dashboard beside the first, every time the app starts —
/// the waterfall this exists to replace, arriving by its own door. That is the exact failure the
/// per-topic status line was built around, which is why its decider (shared, not copied) treats
/// "an id and no remembered text" as EDIT.
///
/// Shape and parsing live here rather than in the engine because the engine is internal sealed with
/// no InternalsVisibleTo — anything decided inside it cannot be reached by the suite.
/// </summary>
public static class GeneralDashboard_Store
{
    const string MESSAGE_ID_PROPERTY = "messageId";

    public static string To_Json(long messageId)
    {
        return new JsonObject { [MESSAGE_ID_PROPERTY] = messageId }.ToJsonString(JsonWriting.INDENTED);
    }

    /// <summary>
    /// Null for every unusable input — absent, empty, not an object, wrong property, not a number.
    /// A dashboard id that cannot be read costs one duplicate message; an exception on the startup
    /// path costs the bridge, so nothing here throws for the state of the file.
    /// </summary>
    public static long? Parse_MessageId_OrNull(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var root = JsonNode.Parse(json) as JsonObject;

            if (root == null || !root.TryGetPropertyValue(MESSAGE_ID_PROPERTY, out var value) || value == null)
                return null;

            return value.GetValue<long>();
        }
        catch
        {
            return null;
        }
    }
}
