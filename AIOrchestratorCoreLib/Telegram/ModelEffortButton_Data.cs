using System.Text;

namespace AIOrchestratorCoreLib.Telegram;

/// <summary>Which dial a /model or /effort button turns.</summary>
public enum ModelEffortKinds
{
    Model,
    Effort,
}

/// <summary>
/// The callback payload behind a /model or /effort button.
///
/// STATELESS, on purpose: it names the orchestration, the role and the value, so a tap needs no
/// registry to be understood — it survives an app restart, it is never evicted, and two prompts
/// for the same topic cannot collide. The single-use option registry the question flow uses is the
/// wrong home for it twice over: its taps expire, and every tap through it ends up routed to an
/// AGENT as a synthetic owner message. A model change is the APP's decision to execute, not a line
/// to forward to a supervisor.
///
/// The role is `sup` or `imp` — the two override slots the session file has. A solo is covered by
/// the implementer slot, exactly as the launcher already treats it, so a basic orchestration's
/// buttons all carry `imp`.
///
/// Prefixed "model:" / "effort:", which are not prefixes of, nor prefixed by, any other family in
/// flight ("cmd:", "hold:", "go:", "close-yes-", "close-no-", "opt-"), so the parse ORDER in the
/// tap handler cannot decide the meaning of a payload. <see cref="Parse_OrNull"/> returns null for
/// everything that is not ours — a tap this cannot read must fall THROUGH untouched.
/// </summary>
public static class ModelEffortButton_Data
{
    const string MODEL_PREFIX = "model:";
    const string EFFORT_PREFIX = "effort:";
    const char FIELD_SEPARATOR = ':';

    /// <summary>Telegram's hard cap on callback_data; a payload over it is rejected at send time.</summary>
    const int TELEGRAM_CALLBACK_DATA_BYTE_LIMIT = 64;

    public const string SUPERVISOR_ROLE = "sup";
    public const string IMPLEMENTER_ROLE = "imp";

    public static string Build(ModelEffortKinds kind, string orchId, string role, string value)
    {
        if (role != SUPERVISOR_ROLE && role != IMPLEMENTER_ROLE)
            throw new ArgumentException($"role must be '{SUPERVISOR_ROLE}' or '{IMPLEMENTER_ROLE}', got '{role}'", nameof(role));

        Require_UsableField(orchId, nameof(orchId));
        Require_UsableField(value, nameof(value));

        var prefix = kind switch
        {
            ModelEffortKinds.Model => MODEL_PREFIX,
            ModelEffortKinds.Effort => EFFORT_PREFIX,
            _ => throw new Exception($"Unhandled ModelEffortKinds: {kind}"),
        };

        var data = $"{prefix}{orchId}{FIELD_SEPARATOR}{role}{FIELD_SEPARATOR}{value}";
        var bytes = Encoding.UTF8.GetByteCount(data);

        if (bytes > TELEGRAM_CALLBACK_DATA_BYTE_LIMIT)
            throw new ArgumentException($"callback data '{data}' is {bytes} bytes; Telegram allows at most {TELEGRAM_CALLBACK_DATA_BYTE_LIMIT}");

        return data;
    }

    /// <summary>Null for anything that is not one of ours, or that is ours but unreadable.</summary>
    public static (ModelEffortKinds Kind, string OrchId, string Role, string Value)? Parse_OrNull(string? callbackData)
    {
        if (callbackData == null)
            return null;

        if (callbackData.StartsWith(MODEL_PREFIX, StringComparison.Ordinal))
            return Parse_Body_OrNull(ModelEffortKinds.Model, callbackData[MODEL_PREFIX.Length..]);

        if (callbackData.StartsWith(EFFORT_PREFIX, StringComparison.Ordinal))
            return Parse_Body_OrNull(ModelEffortKinds.Effort, callbackData[EFFORT_PREFIX.Length..]);

        return null;
    }

    static (ModelEffortKinds Kind, string OrchId, string Role, string Value)? Parse_Body_OrNull(ModelEffortKinds kind, string body)
    {
        var fields = body.Split(FIELD_SEPARATOR);

        if (fields.Length != 3)
            return null;

        if (fields[0].Length == 0 || fields[2].Length == 0)
            return null;

        if (fields[1] != SUPERVISOR_ROLE && fields[1] != IMPLEMENTER_ROLE)
            return null;

        return (kind, fields[0], fields[1], fields[2]);
    }

    /// <summary>A separator inside a field would shift every field after it on the way back.</summary>
    static void Require_UsableField(string field, string name)
    {
        if (string.IsNullOrEmpty(field) || field.Contains(FIELD_SEPARATOR))
            throw new ArgumentException($"{name} must be non-empty and must not contain '{FIELD_SEPARATOR}', got '{field}'", name);
    }
}
