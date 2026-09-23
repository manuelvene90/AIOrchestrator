namespace AIOrchestratorCoreLib.Telegram.SettingsMenu;

/// <summary>
/// What an inbound owner message means to a pending "reply with the value" step (plan 04 D9) — the six
/// answers <see cref="SettingsReplyStep_Decider"/> gives, and the only ones the engine acts on.
/// </summary>
public enum SettingsReplyStepActions
{
    /// <summary>No step is pending in this topic: the message is whatever it was going to be.</summary>
    NoStep,

    /// <summary>The step's window has passed: it is cleared and the message is handled as if it never existed.</summary>
    Lapsed,

    /// <summary><c>/cancel</c> while the step is live: the step ends, and the command is consumed and answered.</summary>
    Cancel,

    /// <summary>Any other <c>/command</c> while the step is live: the step ends and the command runs as it always does.</summary>
    EndedByCommand,

    /// <summary>The value was already taken and waits on its confirm tap, or the message is not text: it goes on as usual.</summary>
    PassThrough,

    /// <summary>The message IS the value — the step swallows it, and says so in the topic.</summary>
    TakeAsValue,
}
