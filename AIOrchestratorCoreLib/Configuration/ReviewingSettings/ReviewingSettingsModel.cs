namespace AIOrchestratorCoreLib.Configuration.ReviewingSettings;

internal sealed class ReviewingSettingsModel(TimeSpan reviewCap, TimeSpan holdCeiling, int handledMemory) : IReviewingSettings
{
    public TimeSpan ReviewCap { get; } = reviewCap;
    public TimeSpan HoldCeiling { get; } = holdCeiling;
    public int HandledMemory { get; } = handledMemory;
}
