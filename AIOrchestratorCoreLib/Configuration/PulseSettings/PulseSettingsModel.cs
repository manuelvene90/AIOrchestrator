namespace AIOrchestratorCoreLib.Configuration.PulseSettings;

internal sealed class PulseSettingsModel(
    IReadOnlyList<string> fields,
    int stepMinutes,
    IReadOnlyList<string> buttons,
    IReadOnlyList<string> generalButtons,
    bool holdToggle) : IPulseSettings
{
    public IReadOnlyList<string> Fields { get; } = fields;
    public int StepMinutes { get; } = stepMinutes;
    public IReadOnlyList<string> Buttons { get; } = buttons;
    public IReadOnlyList<string> GeneralButtons { get; } = generalButtons;
    public bool HoldToggle { get; } = holdToggle;
}
