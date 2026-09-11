namespace AIOrchestratorCoreLib.Telegram.TelegramOwnerMessage;

/// <summary>
/// A FILE THE OWNER SENT AS A DOCUMENT — a log, a CSV, a screenshot they dragged in as a file
/// rather than as a photo, an export from another tool.
///
/// <para>
/// THEY WERE DROPPED IN SILENCE, and that is why this exists. The parser read text, photo, voice
/// and callback and nothing else, so a document with no caption produced no owner message at all —
/// the early "nothing here" return fired, the offset advanced, and the file was gone with no log
/// line anywhere. A document WITH a caption was worse in one specific way: the caption arrived as
/// an ordinary text message, so the owner saw their words land and had every reason to believe the
/// file had landed with them.
/// </para>
/// <para>
/// ONE RECORD RATHER THAN FOUR PARAMETERS, on the factory that builds owner messages: the fields
/// only ever mean anything together, and four trailing optionals on a ten-argument factory is how
/// a caller ends up passing a mime type as a file name.
/// </para>
/// </summary>
public sealed record TelegramDocumentRef
{
    /// <summary>Telegram's handle for the bytes — the only field the download needs.</summary>
    public required string FileId { get; init; }

    /// <summary>
    /// The name as the owner's own machine had it, when Telegram sends one. Kept because it is the
    /// only thing that tells a reader — and a session about to open the file — what it IS; the
    /// photo path's hardcoded `.jpg` has no equivalent here, where anything can arrive.
    /// </summary>
    public string? FileName { get; init; }

    public string? MimeType { get; init; }

    /// <summary>
    /// As DECLARED in the update, which is why the cap is also checked against what was actually
    /// downloaded. Telegram's own bot download limit is 20 MB and this field is how a refusal can
    /// be sent without spending the download first.
    /// </summary>
    public long? SizeBytes { get; init; }
}
