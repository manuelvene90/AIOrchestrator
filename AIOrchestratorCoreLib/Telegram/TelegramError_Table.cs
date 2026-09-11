namespace AIOrchestratorCoreLib.Telegram;

/// <summary>The cases the bridge actually distinguishes in a Telegram failure. Everything else is "refused".</summary>
public enum TelegramErrorCases
{
    /// <summary>Telegram refused on the merits and the bridge has no special handling for it.</summary>
    Refused,

    /// <summary>The edit would change nothing — the desired state already holds. A SUCCESS wearing a failure's clothes.</summary>
    AlreadyCurrent,

    /// <summary>The topic's name is already what we asked for. The topic twin of <see cref="AlreadyCurrent"/>.</summary>
    TopicNameAlreadyCurrent,

    /// <summary>The message this id names no longer exists. Terminal for that id: forget it and post fresh.</summary>
    MessageGone,

    /// <summary>The forum topic this thread id names no longer exists.</summary>
    TopicGone,

    /// <summary>
    /// The message EXISTS and Telegram will not delete it — past the 48-hour window, or the bot
    /// lacks can_delete_messages. Deliberately NOT <see cref="MessageGone"/>: clearing the id would
    /// post a second status line beside an undeletable one.
    /// </summary>
    DeleteRefused,

    /// <summary>The bot may not do this — demoted, removed, or missing a specific right.</summary>
    NotEnoughRights,

    /// <summary>Telegram asked us to slow down, or failed on its own side. The outcome is UNKNOWN, not failed.</summary>
    Retryable,

    /// <summary>Telegram never answered at all — a timeout, a reset, a DNS or TLS failure.</summary>
    NoAnswer,
}

/// <summary>
/// ONE TABLE FROM A TELEGRAM ERROR TO THE CASE THE BRIDGE MEANS BY IT — brief F10.
///
/// <para>
/// WHY IT EXISTS. The distinctions below were spread across three files as private predicates that
/// grew one wording at a time: <see cref="TopicStatusLine_Decider"/> held three of them,
/// <see cref="TelegramAttempt_Gate"/> a fourth, and each new wording was added wherever the incident
/// happened to be observed. Nothing said what the whole set was, so the same Telegram answer could
/// be recognised in one caller and unrecognised in the next — which is precisely what
/// <c>TOPIC_NOT_MODIFIED</c> did for months (recognised by a private <c>when</c> filter inside the
/// engine, invisible to the General-topic sync added later, which then logged a failure every tick
/// from app start).
/// </para>
/// <para>
/// THE STATUS CODE CANNOT DO THIS ALONE, which is the awkward fact the whole table exists for.
/// Telegram answers "the topic is already named that", "there is no such topic", "you may not delete
/// this" and "your parameter is malformed" ALL with HTTP 400, and the bridge does four different
/// things with them. The only place the difference is expressed is the <c>description</c> string —
/// so this file reads descriptions, and it is the ONLY file that may. <see cref="TelegramApiClient.TelegramApiException"/>
/// forbids parsing a STATUS back out of an English sentence, and that rule is untouched: the status
/// is read from the typed field, and the description only ever narrows a status the type already
/// gave.
/// </para>
/// <para>
/// PINNED AGAINST REAL BODIES. The cases below are asserted against response bodies recorded from
/// Telegram rather than against sentences a test invented — the failure mode of a table like this is
/// a wording that drifted, and a test written from the same imagination as the code cannot see it.
/// </para>
/// <para>
/// THE OLD PREDICATES STAY as thin forwarders. Their callers are correct and their doc comments
/// carry the incidents that produced them; moving the RULE here without moving the call sites is
/// what keeps this a consolidation rather than a rewrite.
/// </para>
/// </summary>
public static class TelegramError_Table
{
    /// <summary>
    /// The case, from the status Telegram answered with and the body it answered with.
    ///
    /// <para>
    /// DESCRIPTION FIRST, STATUS SECOND, and the order is the whole correctness of this method: the
    /// specific cases are all 400s, so asking "is it retryable" or "is it a refusal" first would
    /// swallow every one of them. Retryable is asked before the generic refusal for the same reason.
    /// </para>
    /// </summary>
    public static TelegramErrorCases Classify(int statusCode, string description)
    {
        if (Says(description, "message is not modified"))
            return TelegramErrorCases.AlreadyCurrent;

        if (Says(description, "TOPIC_NOT_MODIFIED"))
            return TelegramErrorCases.TopicNameAlreadyCurrent;

        if (Says(description, "message to edit not found")
            || Says(description, "message to delete not found")
            || Says(description, "MESSAGE_ID_INVALID"))
            return TelegramErrorCases.MessageGone;

        if (Says(description, "TOPIC_DELETED")
            || Says(description, "TOPIC_ID_INVALID")
            || Says(description, "message thread not found")
            || Says(description, "topic to delete not found"))
            return TelegramErrorCases.TopicGone;

        // BEFORE the rights case, because Telegram's own wording for the 48-hour window is
        // "message can't be deleted" and carries no mention of rights at all — while the
        // permission variant says both. The bridge treats them the same (latch, stop moving the
        // line) and the caller that cares reads DeleteRefused for either.
        if (Says(description, "message can't be deleted")
            || Says(description, "message can not be deleted")
            || Says(description, "message cannot be deleted"))
            return TelegramErrorCases.DeleteRefused;

        if (Says(description, "not enough rights")
            || Says(description, "CHAT_ADMIN_REQUIRED")
            || Says(description, "have no rights to send a message"))
            return TelegramErrorCases.NotEnoughRights;

        if (statusCode == 429 || statusCode >= 500)
            return TelegramErrorCases.Retryable;

        return TelegramErrorCases.Refused;
    }

    /// <summary>
    /// Same table, given the exception the client threw. A failure that is not Telegram's at all —
    /// an HttpClient timeout, a reset connection — is <see cref="TelegramErrorCases.NoAnswer"/>:
    /// Telegram did not refuse, it did not speak.
    /// </summary>
    public static TelegramErrorCases Classify(Exception failure)
    {
        if (failure is TelegramApiClient.TelegramApiException answered)
            return Classify(answered.StatusCode, answered.Message);

        if (failure is OperationCanceledException or HttpRequestException)
            return TelegramErrorCases.NoAnswer;

        // Anything else reached no wire and told us nothing either. NoAnswer rather than Refused,
        // for the reason TopicDelete_Decider states at length: a wrong "permanent" costs a stranded
        // resource for ever, a wrong "unknown" costs one retry.
        return TelegramErrorCases.NoAnswer;
    }

    /// <summary>
    /// Case-insensitive because Telegram is not consistent about it — the machine-readable slugs are
    /// upper-case (<c>TOPIC_NOT_MODIFIED</c>) and the prose ones are lower (<c>message is not
    /// modified</c>), and both have appeared in the other casing in the logs.
    /// </summary>
    static bool Says(string description, string fragment)
    {
        return description.Contains(fragment, StringComparison.OrdinalIgnoreCase);
    }
}
