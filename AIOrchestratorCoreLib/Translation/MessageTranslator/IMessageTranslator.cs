namespace AIOrchestratorCoreLib.Translation.MessageTranslator;

/// <summary>
/// The Telegram Italian layer: the owner reads/writes Italian on the phone while every session,
/// channel and repo artifact stays 100% English. Translation NEVER blocks traffic — any failure
/// or timeout returns the original text unchanged.
/// </summary>
public interface IMessageTranslator
{
    /// <summary>Owner → session direction. Already-English text passes through unchanged.</summary>
    Task<string> Translate_ToEnglish_Async(string text, CancellationToken cancellationToken);

    /// <summary>Session → owner direction. Technical terms stay English, as Italian professionals use them.</summary>
    Task<string> Translate_ToItalian_Async(string text, CancellationToken cancellationToken);
}
