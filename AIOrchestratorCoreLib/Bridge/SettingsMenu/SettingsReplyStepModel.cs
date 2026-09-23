namespace AIOrchestratorCoreLib.Bridge.SettingsMenu;

internal sealed class SettingsReplyStepModel(long? threadId, string path, string? heldText, DateTime expiresUtc) : ISettingsReplyStep
{
    public long? ThreadId { get; } = threadId;

    public string Path { get; } = path;

    public string? HeldText_OrNull { get; } = heldText;

    public DateTime ExpiresUtc { get; } = expiresUtc;
}
