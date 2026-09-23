namespace AIOrchestratorCoreLib.Telegram.SettingsMenu;

/// <summary>
/// DOES THIS INBOUND MESSAGE ANSWER A PENDING SETTINGS PROMPT? (plan 04 Task 5, D9) — pure, so the rules
/// that decide whether an owner's message is SWALLOWED are provable without a bot.
///
/// <para>
/// THE MOST DANGEROUS STATE IN THE PLAN, which is why its rules are written down here and nowhere else. A
/// step that eats a message meant for the supervisor loses it with nothing said: the owner waits for an
/// answer to something that never arrived. So the step is narrow on every axis D9 names — per (chat, topic),
/// never across topics; <see cref="EXPIRY_MINUTES"/> long, after which it swallows nothing
/// (<see cref="SettingsReplyStepActions.Lapsed"/>); ended by ANY <c>/command</c>, which then runs normally, so
/// <c>/pending</c> still works mid-prompt; ended and answered by <see cref="CANCEL_COMMAND"/>; and a message
/// that is not text (a photo, a voice note) is never taken as a value. The prompt says all of this in its own
/// words (<see cref="SettingsMenu_Builder.Build_ReplyPrompt"/>).
/// </para>
/// <para>
/// A HELD VALUE SWALLOWS NOTHING MORE. Once a Kernel value is typed it waits on its confirm tap (ruling P6),
/// and the next message is the owner talking again — a second value would silently replace the one the
/// Confirm on screen names.
/// </para>
/// </summary>
public static class SettingsReplyStep_Decider
{
    /// <summary>D9's window: long enough to look a number up, short enough that a forgotten prompt is gone before the next conversation.</summary>
    public const int EXPIRY_MINUTES = 5;

    /// <summary>
    /// The one word the step consumes (ruling P15) — and only while a step is live; otherwise it falls through
    /// like any other unknown command. Deliberately NOT in <c>BotCommandMenu.ALL</c>: it means nothing outside a
    /// prompt, and the prompt names it.
    /// </summary>
    public const string CANCEL_COMMAND = "cancel";

    /// <param name="hasStep">Whether a step is pending in the topic this message was typed in.</param>
    /// <param name="isHeld">Whether that step already holds its typed value, waiting on the confirm tap.</param>
    /// <param name="expiresUtc">The step's deadline — its ORIGINAL one, restored across a restart (ruling P23).</param>
    /// <param name="nowUtc">Now.</param>
    /// <param name="command">The message's bot command as the engine lexed it, or null for plain text.</param>
    /// <param name="carriesMedia">A photo, a voice note or a document — never a value.</param>
    public static SettingsReplyStepActions Decide(bool hasStep, bool isHeld, DateTime expiresUtc, DateTime nowUtc, string? command, bool carriesMedia)
    {
        if (!hasStep)
            return SettingsReplyStepActions.NoStep;

        if (nowUtc >= expiresUtc)
            return SettingsReplyStepActions.Lapsed;

        if (command == CANCEL_COMMAND)
            return SettingsReplyStepActions.Cancel;

        if (command != null)
            return SettingsReplyStepActions.EndedByCommand;

        if (isHeld || carriesMedia)
            return SettingsReplyStepActions.PassThrough;

        return SettingsReplyStepActions.TakeAsValue;
    }
}
