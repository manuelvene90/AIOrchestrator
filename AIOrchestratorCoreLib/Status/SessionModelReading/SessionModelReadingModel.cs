namespace AIOrchestratorCoreLib.Status.SessionModelReading;

internal sealed class SessionModelReadingModel(
    string modelDisplayName,
    string? effortLevel) : ISessionModelReading
{
    public string ModelDisplayName { get; } = modelDisplayName;

    public string? EffortLevel { get; } = effortLevel;
}
