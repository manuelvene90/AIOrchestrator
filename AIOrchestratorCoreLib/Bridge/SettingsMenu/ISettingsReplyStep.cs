namespace AIOrchestratorCoreLib.Bridge.SettingsMenu;

/// <summary>
/// ONE PENDING "REPLY WITH THE VALUE" STEP (plan 04 D9): the /settings menu asked the owner to type a value
/// for one setting, in one topic, before a deadline. Immutable — a held value is a NEW step with the same
/// deadline, never an edit of this one.
///
/// <para>
/// KEYED BY THE SETTING'S PATH, NOT ITS CATALOGUE INDEX. The step is persisted with the engine state (ruling
/// P23), and an index renumbers across an upgrade (D8's reason for the payload check); a path is what the
/// catalogue itself looks a row up by, and a path that no longer resolves is answered, never guessed.
/// </para>
/// </summary>
public interface ISettingsReplyStep
{
    /// <summary>The topic the prompt was answered in — null is General, where the machine menu lives (D3).</summary>
    long? ThreadId { get; }

    /// <summary>The catalogue path of the setting being set.</summary>
    string Path { get; }

    /// <summary>
    /// The typed value a Kernel row holds while its Confirm waits for Yes (ruling P6), or null while the step
    /// is still waiting for the value. For <c>web.token</c> this is the secret: it is kept here and in the
    /// engine-state file beside config.json, and is never drawn, answered or logged (ruling P40).
    /// </summary>
    string? HeldText_OrNull { get; }

    /// <summary>The ORIGINAL deadline — a restart restores it rather than restarting the window (ruling P23).</summary>
    DateTime ExpiresUtc { get; }
}
