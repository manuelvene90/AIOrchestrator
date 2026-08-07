using AIOrchestratorCoreLib.Logging.OrchestrationLog;

namespace AIOrchestratorCoreLib.Transcription.VoiceTranscriber;

public static class VoiceTranscriber_Factory
{
    public static IVoiceTranscriber Create(IOrchestrationLog log)
    {
        return new VoiceTranscriberModel(log);
    }
}
