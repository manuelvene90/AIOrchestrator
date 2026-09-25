namespace AIOrchestratorCoreLib.Bridge.SettingsMenu;

public static class SettingsReplyStep_Factory
{
    /// <summary>A step waiting for its value.</summary>
    public static ISettingsReplyStep Create_Waiting(long? threadId, string path, DateTime expiresUtc)
    {
        return new SettingsReplyStepModel(threadId, path, null, expiresUtc);
    }

    /// <summary>
    /// The same step holding the value the owner typed, waiting on the Confirm's Yes — with the SAME deadline:
    /// a held value does not buy itself a fresh window.
    /// </summary>
    public static ISettingsReplyStep Create_Holding(ISettingsReplyStep waiting, string heldText)
    {
        return new SettingsReplyStepModel(waiting.ThreadId, waiting.Path, heldText, waiting.ExpiresUtc);
    }

    /// <summary>A step read back from the engine-state file, exactly as it was written.</summary>
    public static ISettingsReplyStep Create_Restored(long? threadId, string path, string? heldText, DateTime expiresUtc)
    {
        return new SettingsReplyStepModel(threadId, path, heldText, expiresUtc);
    }
}
