using AIOrchestratorCoreLib.Logging.OrchestrationLog;

namespace AIOrchestratorCoreLib.Translation.MessageTranslator;

public static class MessageTranslator_Factory
{
    public static IMessageTranslator Create(IOrchestrationLog log)
    {
        return new MessageTranslatorModel(log);
    }
}
