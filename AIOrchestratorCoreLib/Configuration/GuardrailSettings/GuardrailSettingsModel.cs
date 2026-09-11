namespace AIOrchestratorCoreLib.Configuration.GuardrailSettings;

internal sealed class GuardrailSettingsModel(
    IReadOnlyList<string> highRiskPatterns,
    int highRiskCodeExpiryMinutes,
    double dispatchPauseThresholdPercent,
    int buttonExpiryMinutes) : IGuardrailSettings
{
    public IReadOnlyList<string> HighRiskPatterns { get; } = highRiskPatterns;
    public int HighRiskCodeExpiryMinutes { get; } = highRiskCodeExpiryMinutes;
    public double DispatchPauseThresholdPercent { get; } = dispatchPauseThresholdPercent;
    public int ButtonExpiryMinutes { get; } = buttonExpiryMinutes;
}
