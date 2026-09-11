namespace AIOrchestratorCoreLib.Configuration.TelegramProseSettings;

/// <summary>
/// HOW MUCH OF A LONG ENTRY THE PHONE SHOWS AT ONCE — the <c>telegram</c> block of config.json.
///
/// <para>
/// Both settings are about the SHAPE of a delivery and never about whether it happens: a message is
/// delivered whole under every value either of them can take, including the values that turn the
/// feature off. That is why they are safe to hand-edit and why a nonsense value costs the default
/// rather than the message.
/// </para>
/// </summary>
public interface ITelegramProseSettings
{
    /// <summary>
    /// Rendered characters above which an owner-facing entry is delivered as its opening plus one
    /// collapsed quotation. 0 (or less) disables folding: entries then arrive exactly as they did
    /// before the fold existed.
    /// </summary>
    int FoldLongEntriesAbove { get; }

    /// <summary>
    /// How many delivered messages an entry has to EXCEED before the original Markdown is also
    /// attached as a file. 0 (or less) disables the attachment; the messages themselves are never
    /// affected either way.
    /// </summary>
    int AttachEntriesAbove { get; }
}
