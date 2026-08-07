namespace AIOrchestratorCoreLib.Transcription.VoiceTranscriber;

/// <summary>
/// Transcribes an owner voice note via the CONFIGURED external command (config.json
/// "voiceTranscribeCommand", e.g. a whisper CLI) — Claude cannot transcribe audio itself.
/// Null result = failure or no output; the caller tells the owner to type instead.
/// </summary>
public interface IVoiceTranscriber
{
    Task<string?> Transcribe_OrNull_Async(string audioFilePath, string commandTemplate, CancellationToken cancellationToken);
}
