namespace AIOrchestratorCoreLib.Bridge.TopicNaming;

/// <summary>
/// WHICH NAME A NEW TOPIC IS BORN WITH.
///
/// <para>
/// It used to be the bare orch id, always, and the real name arrived later by rename. Observed
/// 2026-09-23 on `da-vinci-fintech-suite-33`: the solo filed `set-orchestration-name`
/// ("TKT · ticket view sync") at 15:07:39 UTC, four seconds BEFORE its topic existed, so the topic
/// was born "da-vinci-fintech-suite-33" and the name depended entirely on the rename sync — which
/// logged nothing when it succeeded. The owner saw the bare id while the session said it had named
/// the topic, and nothing in the files could say whether or when the name landed.
/// </para>
/// <para>
/// So the topic is created with the name the rename sync WOULD push (the caller passes
/// `Build_WantedTopicName`, the one composer — decision 12), and this only decides when that name
/// cannot be trusted at creation, falling back to the orch id, which is what every topic was
/// created with before and which Telegram has always accepted. The fallback matters more here than
/// at a rename: a refused CREATION mirrors to the General topic, a refused rename only leaves a name
/// stale.
/// </para>
/// </summary>
public static class TopicCreationName_Resolver
{
    /// <summary>Telegram's documented cap for a forum topic name ("1-128 characters").</summary>
    public const int MAX_TOPIC_NAME_LENGTH = 128;

    /// <summary>
    /// The wanted name when it is usable, else the orch id. Measured in UTF-16 units, which counts
    /// every emoji glyph as two: stricter than Telegram's own count, so it can only fall back too
    /// early, never pass a name Telegram would refuse for length.
    /// </summary>
    public static string Resolve(string orchId, string? wantedName)
    {
        if (string.IsNullOrWhiteSpace(orchId))
            throw new ArgumentException($"A topic cannot be created for a blank orch id (got '{orchId}').", nameof(orchId));

        if (string.IsNullOrWhiteSpace(wantedName))
            return orchId;

        var trimmed = wantedName.Trim();

        return trimmed.Length > MAX_TOPIC_NAME_LENGTH ? orchId : trimmed;
    }
}
