using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation.SettingEditor;
using AIOrchestratorCoreLib.Configuration.SettingsPresentation.SettingReading;
using AIOrchestratorCoreLib.Configuration.SettingsWriting;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.SettingsMenu;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using AIOrchestratorCoreLib.Telegram.TelegramCallbackTap;
using AIOrchestratorCoreLib.Telegram.TelegramOwnerMessage;
using AIOrchestratorCoreLib.Telegram.TelegramSendBudget;
using AIOrchestratorCoreLib.Time.Clock;

using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Bridge.SettingsMenu;

/// <summary>
/// See <see cref="ISettingsMenu"/>. Every word the phone shows comes from the builder, the formatters and the
/// catalogue; the sentences written here are only about the step and the menu itself (a stale tap, a lapsed
/// prompt, a stopped one), never about a value (decision 12) and never a refusal of one (decision 21).
///
/// <para>
/// PLAIN TEXT, AND ROWS (Task 4's contract, and the review's carry). The builder's text carries <c>&lt;</c>,
/// <c>&gt;</c> and <c>&amp;</c> unescaped, so it goes out ONLY through <c>Send_MessageWithButtonRows_Async</c> /
/// <c>Edit_MessageTextWithButtonRows_Async</c> / <c>Send_Message_Async</c>, none of which set a parse mode, and
/// the keyboard goes out as the rows the builder returned — never flattened through the HTML button path.
/// </para>
/// <para>
/// SILENT, EVERY ONE OF THEM. A settings menu is not news: nothing here rings the owner's phone.
/// </para>
/// <para>
/// THE SELF-EDITING MESSAGE AND ITS FALLBACK. A tap edits the message it was tapped on, which becomes the live
/// menu and carries D7's edit-gap exemption. When the edit is refused anyway (Telegram's own cooldown, the
/// message deleted, a 400) the view is REPOSTED as a new live menu rather than lost — the topic status line's
/// shape (<c>BridgeEngineModel</c>'s status-line edit-or-repost) with one difference argued there and here: the
/// status line deletes before it posts, because two status lines is the defect it exists to prevent; the menu
/// posts first and then takes the old one down, because a menu that vanished is the defect THIS one must not
/// have. "Message is not modified" is not a refusal: the menu already shows what was asked for.
/// </para>
/// <para>
/// A TYPED SECRET IS NEVER ECHOED (ruling P40). <c>web.token</c> is editable from the phone — the chat is
/// authenticated by the owner's id and it is the only route a headless machine has to its first token — so its
/// value passes through here, and it appears in no reply, no toast, no Confirm (the builder's own rule) and no
/// log line: this class logs paths and outcomes, never values.
/// </para>
/// </summary>
internal sealed class SettingsMenuModel(
    ISupervisionPaths paths,
    IOrchestrationSessionStore store,
    IOrchestrationLog log,
    IClock clock,
    ITelegramSendBudget? sendBudget,
    ISettingsMenuState state) : ISettingsMenu
{
    /// <summary>A tap on a set: payload outside General — the Orchestration view has no buttons, so this is a forged or foreign one (D3).</summary>
    internal const string GENERAL_ONLY_ANSWER = "This machine's settings are changed from /settings in the General topic.";

    internal const string NO_ORCHESTRATION_HERE =
        "No orchestration is bound to this topic. /settings in the General topic shows this machine's settings.";

    internal const string CANCELLED = "Settings: stopped — nothing was changed.";

    internal const string HELD_VALUE_GONE = "That typed value is no longer held — open the setting and tap ✎ Reply again.";

    internal const string NOTHING_TO_SAVE = "Nothing to save in that message.";

    internal const string SEND_ANOTHER = "Send another value, or /cancel.";

    /// <summary>A secret's own refusal may not quote it back: the place its text was is shown as this.</summary>
    const string SECRET_MASK = "(not shown)";

    /// <summary>Telegram's cap on an answerCallbackQuery toast.</summary>
    const int TOAST_LIMIT = 200;

    public ISettingsMenuState State => state;

    public async Task Send_Menu_Async(ITelegramApiClient client, ISettingsMenuHost host, long? messageThreadId, CancellationToken cancellationToken)
    {
        if (messageThreadId != null)
        {
            await Send_OrchestrationView_Async(client, host, messageThreadId.Value, cancellationToken);
            return;
        }

        var (readings, presetName) = Read_Snapshot();
        var view = SettingsMenu_Builder.Build(SettingsMenuViews.Categories, null, null, 0, null, null, readings, presetName);

        await Post_LiveMenu_Async(client, host, view.Text, view.Rows, cancellationToken);
        host.Persist_EngineState();
    }

    public async Task<bool> Try_HandleTap_Async(ITelegramApiClient client, ISettingsMenuHost host, ITelegramCallbackTap tap, CancellationToken cancellationToken)
    {
        if (!SettingsButton_Data.Is_Ours(tap.Data))
            return false;

        var parsed = SettingsButton_Data.Parse_OrNull(tap.Data);
        var resolved = parsed == null ? null : SettingsButton_Data.Resolve_OrNull(parsed.Value);

        if (resolved == null || tap.MessageId == null)
        {
            log.Log_Info(Describe_Scope(tap.MessageThreadId), $"Settings tap '{tap.Data}' does not resolve against this build's catalogue — answered as stale, nothing changed");
            await Answer_BestEffort_Async(client, tap.CallbackQueryId, SettingsButton_Data.STALE_ANSWER, cancellationToken);
            return true;
        }

        if (tap.MessageThreadId != null)
        {
            await Answer_BestEffort_Async(client, tap.CallbackQueryId, GENERAL_ONLY_ANSWER, cancellationToken);
            return true;
        }

        var (view, category, index, definition, page, edit, word) = resolved.Value;
        var messageId = tap.MessageId.Value;

        // READ BEFORE THE MENU IS ADOPTED: adopting a different message replaces the live menu, and replacing
        // it clears the steps — including the held value this very tap may be the Yes for.
        var step = state.Find_Step_OrNull(null);

        Adopt_AsLiveMenu(messageId);

        // ANY TAP BUT THE HELD VALUE'S OWN YES ENDS A PENDING STEP: the owner has navigated away from the
        // prompt, and a prompt that is no longer on screen must not go on waiting to swallow a message.
        if (edit != SettingsMenuEdits.ApplyHeldReply)
            state.Clear_Step(null);

        var (readings, presetName) = Read_Snapshot();
        string? note = null;
        (string Text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> Rows) shown;
        ISettingsReplyStep? startedStep = null;

        if (definition != null && SettingsButton_Data.Writes_OnTap(view, edit))
        {
            var reading = Find_Reading(readings, definition.Path);

            note = Is_Refused_OnThePhone(reading) ?? Apply_Edit(reading, edit!.Value, word, step);

            if (edit == SettingsMenuEdits.ApplyHeldReply)
                state.Clear_Step(null);

            (readings, presetName) = Read_Snapshot();
            shown = SettingsMenu_Builder.Build(view, category, index, page, null, null, readings, presetName);
        }
        else if (definition != null && edit == SettingsMenuEdits.Reply)
        {
            var reading = Find_Reading(readings, definition.Path);
            var back = SettingsButton_Data.Build(SettingsMenuViews.Setting, null, index, 0, null, null);

            note = Is_Refused_OnThePhone(reading);

            if (note == null)
            {
                shown = (SettingsMenu_Builder.Build_ReplyPrompt(reading, SettingsReplyStep_Decider.EXPIRY_MINUTES), [[(back, SettingsMenu_Builder.BACK)]]);
                startedStep = SettingsReplyStep_Factory.Create_Waiting(null, definition.Path, clock.UtcNow.AddMinutes(SettingsReplyStep_Decider.EXPIRY_MINUTES));
            }
            else
            {
                shown = SettingsMenu_Builder.Build(SettingsMenuViews.Setting, category, index, 0, null, null, readings, presetName);
            }
        }
        else if (definition != null && view == SettingsMenuViews.Confirm && word != null)
        {
            // VALIDATED BEFORE THE CONFIRM IS DRAWN (Task 5 carry): a Yes that can only be refused is a question
            // the owner should never have been asked. The refusal is the definition's own.
            var reading = Find_Reading(readings, definition.Path);
            var problem = Validate_Edit_OrNull(reading, edit!.Value, word);

            if (problem == null)
            {
                shown = SettingsMenu_Builder.Build(view, category, index, page, edit, word, readings, presetName);
            }
            else
            {
                note = problem;
                shown = SettingsMenu_Builder.Build(SettingsMenuViews.Setting, category, index, 0, null, null, readings, presetName);
            }
        }
        else
        {
            shown = SettingsMenu_Builder.Build(view, category, index, page, edit, word, readings, presetName);
        }

        await Answer_BestEffort_Async(client, tap.CallbackQueryId, Describe_Toast(note), cancellationToken);
        await Show_Async(client, host, messageId, Prefix_Note(note, shown.Text), shown.Rows, cancellationToken);

        if (startedStep != null)
            state.Put_Step(startedStep);

        host.Persist_EngineState();

        return true;
    }

    public async Task<bool> Try_EndReplyStep_OnCommand_Async(
        ITelegramApiClient client, ISettingsMenuHost host, ITelegramOwnerMessage message, string command, CancellationToken cancellationToken)
    {
        var step = state.Find_Step_OrNull(message.MessageThreadId);
        var action = SettingsReplyStep_Decider.Decide(
            step != null, step?.HeldText_OrNull != null, step?.ExpiresUtc ?? default, clock.UtcNow, command, carriesMedia: false);

        switch (action)
        {
            case SettingsReplyStepActions.NoStep:
                return false;

            case SettingsReplyStepActions.Lapsed:
            case SettingsReplyStepActions.EndedByCommand:
                state.Clear_Step(message.MessageThreadId);
                host.Persist_EngineState();
                log.Log_Info(Describe_Scope(message.MessageThreadId), $"Settings reply step for '{step!.Path}' ended by /{command} ({action}) — the command runs as usual");
                return false;

            case SettingsReplyStepActions.Cancel:
                state.Clear_Step(message.MessageThreadId);
                host.Persist_EngineState();
                log.Log_Info(Describe_Scope(message.MessageThreadId), $"Settings reply step for '{step!.Path}' cancelled by the owner");
                await Reply_Async(client, host, message.MessageThreadId, CANCELLED, cancellationToken);
                return true;

            case SettingsReplyStepActions.PassThrough:
            case SettingsReplyStepActions.TakeAsValue:
                throw new InvalidOperationException($"a command cannot be {action} — Decide answers a command before either");

            default:
                throw new InvalidOperationException($"Unhandled SettingsReplyStepActions: {action}");
        }
    }

    public async Task<bool> Try_TakeReply_Async(ITelegramApiClient client, ISettingsMenuHost host, ITelegramOwnerMessage message, CancellationToken cancellationToken)
    {
        var threadId = message.MessageThreadId;
        var step = state.Find_Step_OrNull(threadId);
        var carriesMedia = message.PhotoFileId != null || message.VoiceFileId != null || message.Document != null;
        var action = SettingsReplyStep_Decider.Decide(
            step != null, step?.HeldText_OrNull != null, step?.ExpiresUtc ?? default, clock.UtcNow, command: null, carriesMedia);

        switch (action)
        {
            case SettingsReplyStepActions.NoStep:
            case SettingsReplyStepActions.PassThrough:
                return false;

            case SettingsReplyStepActions.Lapsed:
                // AN EXPIRED STEP MUST NOT EAT THE OWNER'S MESSAGE: it is cleared and the message goes on to the
                // supervisor. The one line said here tells the owner why their value did not land — a message
                // they may have meant as one — without holding back the message itself.
                state.Clear_Step(threadId);
                host.Persist_EngineState();
                log.Log_Info(Describe_Scope(threadId), $"Settings reply step for '{step!.Path}' had lapsed at {step.ExpiresUtc:HH:mm:ss}Z — the message was routed as usual");

                if (step.HeldText_OrNull == null)
                    await Reply_Async(client, host, threadId, Describe_Lapsed(step.Path), cancellationToken);

                return false;

            case SettingsReplyStepActions.TakeAsValue:
                await Take_Value_Async(client, host, step!, message.Text, cancellationToken);
                host.Persist_EngineState();
                return true;

            case SettingsReplyStepActions.Cancel:
            case SettingsReplyStepActions.EndedByCommand:
                throw new InvalidOperationException($"a routable message is never {action} — its command was answered before routing");

            default:
                throw new InvalidOperationException($"Unhandled SettingsReplyStepActions: {action}");
        }
    }

    /// <summary>
    /// THE STEP TAKES THE MESSAGE, and answers in the topic every time. A value the definition refuses keeps the
    /// step live with its reason; a Kernel value is held and its Confirm posted (ruling P6); any other value is
    /// written and the result posted as the new live menu.
    /// </summary>
    async Task Take_Value_Async(ITelegramApiClient client, ISettingsMenuHost host, ISettingsReplyStep step, string text, CancellationToken cancellationToken)
    {
        var definition = Catalog.Find_OrNull(step.Path);

        if (definition == null)
        {
            state.Clear_Step(step.ThreadId);
            await Reply_Async(client, host, step.ThreadId, SettingsButton_Data.STALE_ANSWER, cancellationToken);
            return;
        }

        var (readings, presetName) = Read_Snapshot();
        var reading = Find_Reading(readings, definition.Path);
        var index = Index_Of(definition.Path);
        var fenced = Is_Refused_OnThePhone(reading);

        if (fenced != null)
        {
            state.Clear_Step(step.ThreadId);
            var setting = SettingsMenu_Builder.Build(SettingsMenuViews.Setting, null, index, 0, null, null, readings, presetName);
            await Post_LiveMenu_Async(client, host, Prefix_Note(fenced, setting.Text), setting.Rows, cancellationToken);
            return;
        }

        var problem = Validate_Edit_OrNull(reading, SettingsMenuEdits.Set, text);

        if (problem != null)
        {
            await Reply_Async(client, host, step.ThreadId, $"{problem}\n{SEND_ANOTHER}", cancellationToken);
            return;
        }

        if (SettingsMenu_Builder.Needs_Confirm(definition))
        {
            var confirm = SettingsMenu_Builder.Build(SettingsMenuViews.Confirm, null, index, 0, SettingsMenuEdits.ApplyHeldReply, text, readings, presetName);

            await Post_LiveMenu_Async(client, host, confirm.Text, confirm.Rows, cancellationToken);
            state.Put_Step(SettingsReplyStep_Factory.Create_Holding(step, text));
            return;
        }

        var note = Apply_Edit(reading, SettingsMenuEdits.Set, text, step);

        state.Clear_Step(step.ThreadId);
        (readings, presetName) = Read_Snapshot();

        var after = SettingsMenu_Builder.Build(SettingsMenuViews.Setting, null, index, 0, null, null, readings, presetName);
        await Post_LiveMenu_Async(client, host, Prefix_Note(note, after.Text), after.Rows, cancellationToken);
    }

    /// <summary>
    /// ONE WRITE, AND THE LINE THAT SAYS WHAT HAPPENED — through <see cref="Settings_Writer"/>, the one writer, and
    /// <see cref="SettingWriteNote_Formatter"/>, the one line every renderer draws after an edit. A refusal is
    /// the writer's or the definition's own words; a file the writer could not read (<c>WriteFailed</c>) is drawn
    /// as the refusal it is, never as "Saved."; a write that threw says nothing was saved.
    /// </summary>
    string Apply_Edit(ISettingReading reading, SettingsMenuEdits edit, string? word, ISettingsReplyStep? step)
    {
        var definition = reading.Definition;
        var scope = ChannelDiscovery.GENERAL_ORCH_ID;
        var isSecret = SettingEditor_Factory.Create_ForReading(reading).Kind == SettingEditorKinds.Secret;
        string? typed = null;

        try
        {
            SettingsWriteOutcomes outcome;
            string? message;

            if (edit == SettingsMenuEdits.Reset)
            {
                (outcome, message) = Settings_Writer.Reset(paths, definition.Path, log);
            }
            else
            {
                typed = edit == SettingsMenuEdits.ApplyHeldReply ? Find_HeldText_OrNull(step, definition.Path) : word;

                if (typed == null)
                    return HELD_VALUE_GONE;

                var (isEdit, value) = SettingsMenu_Builder.Build_EditedValue(reading, edit, typed);

                if (!isEdit)
                    return NOTHING_TO_SAVE;

                if (!isSecret && SettingEditor_Factory.Create_ForReading(reading).Is_Unchanged(value))
                    return SettingWriteNote_Formatter.UNCHANGED;

                (outcome, message) = Settings_Writer.Apply(paths, definition.Path, value, log);
            }

            log.Log_Info(scope, $"Settings: '{definition.Path}' {outcome} from the phone ({edit})");

            return Mask_Secret(SettingWriteNote_Formatter.Describe(outcome, message).Text, isSecret, typed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Log_Warning(scope, $"Settings: writing '{definition.Path}' from the phone threw — nothing was saved ({ex.GetType().Name})");

            return Mask_Secret(SettingWriteNote_Formatter.Describe_WriteThrew(ex.Message), isSecret, typed);
        }
    }

    /// <summary>
    /// The definition's verdict on the value this change would write, in its own words — null when it accepts it
    /// or when there is nothing to judge. Asked BEFORE a Confirm is drawn and before a typed value is held.
    /// </summary>
    static string? Validate_Edit_OrNull(ISettingReading reading, SettingsMenuEdits edit, string word)
    {
        var (isEdit, value) = SettingsMenu_Builder.Build_EditedValue(reading, edit, word);

        if (!isEdit)
            return NOTHING_TO_SAVE;

        var problem = reading.Definition.Validate_OrNull(value);

        if (problem == null)
            return null;

        var isSecret = SettingEditor_Factory.Create_ForReading(reading).Kind == SettingEditorKinds.Secret;

        return Mask_Secret(SettingWriteNote_Formatter.Describe(SettingsWriteOutcomes.RefusedInvalid, problem).Text, isSecret, word);
    }

    /// <summary>
    /// THE FENCE, RE-ASKED ON THE READING BEFORE ANY WRITE (Task 5 carry; D2): the builder drew no button for a
    /// fenced row, but a stale or forged payload can still name one, and the rule that refuses it must be the
    /// same rule that drew nothing. Null when the phone may change the row.
    /// </summary>
    static string? Is_Refused_OnThePhone(ISettingReading reading)
    {
        if (SettingsMenu_Builder.Is_EditableOnThePhone(reading))
            return null;

        return reading.IsEditable ? SettingsMenu_Builder.PHONE_FENCE_NOTE : SettingsRow_Builder.READ_ONLY_NOTE;
    }

    /// <summary>
    /// D3: the orchestration's own rows, read-only, as a plain message with no keyboard. An orchestration topic
    /// never gets the machine menu, so nothing typed or tapped there can change a machine setting.
    /// </summary>
    async Task Send_OrchestrationView_Async(ITelegramApiClient client, ISettingsMenuHost host, long messageThreadId, CancellationToken cancellationToken)
    {
        var session = store.Find_ByTelegramTopicId_OrNull(messageThreadId);

        if (session == null)
        {
            await Reply_Async(client, host, messageThreadId, NO_ORCHESTRATION_HERE, cancellationToken);
            return;
        }

        var (readings, presetName) = SettingsSnapshot_Reader.Read_All_FromDisk(paths, session, log);
        var view = SettingsMenu_Builder.Build(SettingsMenuViews.Orchestration, null, null, 0, null, null, readings, presetName);

        await Reply_Async(client, host, messageThreadId, view.Text, cancellationToken);
    }

    /// <summary>
    /// EDITS THE LIVE MENU IN PLACE, and reposts it when the edit is refused rather than leave the owner looking
    /// at a menu that no longer answers (see the type's remarks).
    /// </summary>
    async Task Show_Async(
        ITelegramApiClient client, ISettingsMenuHost host, long messageId, string text,
        IReadOnlyList<IReadOnlyList<(string Data, string Label)>> rows, CancellationToken cancellationToken)
    {
        try
        {
            await client.Edit_MessageTextWithButtonRows_Async(messageId, text, rows, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (TopicStatusLine_Decider.Is_MessageAlreadyCurrent(ex.Message))
        {
            // The menu already shows exactly this — a Back to the view on screen. Nothing to do.
        }
        catch (Exception ex)
        {
            log.Log_Info(ChannelDiscovery.GENERAL_ORCH_ID, $"Settings menu {messageId} could not be edited ({ex.GetType().Name}: {ex.Message}) — reposting it as a new message");
            await Post_LiveMenu_Async(client, host, text, rows, cancellationToken);
        }
    }

    /// <summary>
    /// A NEW LIVE MENU: posted first, and only then is the previous one released and taken down, so the owner is
    /// never left with none. Replacing it clears every pending step (D9). The exemption moves with it (D7).
    /// </summary>
    async Task Post_LiveMenu_Async(
        ITelegramApiClient client, ISettingsMenuHost host, string text,
        IReadOnlyList<IReadOnlyList<(string Data, string Label)>> rows, CancellationToken cancellationToken)
    {
        var messageId = await client.Send_MessageWithButtonRows_Async(null, text, rows, TelegramSendSounds.Silent, cancellationToken);

        host.Remember_TopicMessage(null, messageId);

        var previous = state.Replace_LiveMenu(messageId);

        if (previous != null && previous != messageId)
        {
            sendBudget?.Release_EditGapExemption(previous.Value);
            await Delete_BestEffort_Async(client, previous.Value, cancellationToken);
        }

        if (messageId != null)
            sendBudget?.Exempt_FromEditGap(messageId.Value);
    }

    /// <summary>
    /// A tap on a menu message makes THAT message the live one — the exemption follows the message the owner is
    /// actually using (an older menu, or one restored across a restart). The old one is left up: it was not
    /// replaced by anything the owner did to it.
    /// </summary>
    void Adopt_AsLiveMenu(long messageId)
    {
        var previous = state.LiveMenuMessageId;

        if (previous == messageId)
            return;

        state.Replace_LiveMenu(messageId);

        if (previous != null)
            sendBudget?.Release_EditGapExemption(previous.Value);

        sendBudget?.Exempt_FromEditGap(messageId);
    }

    async Task Reply_Async(ITelegramApiClient client, ISettingsMenuHost host, long? messageThreadId, string text, CancellationToken cancellationToken)
    {
        try
        {
            var messageId = await client.Send_Message_Async(messageThreadId, text, TelegramSendSounds.Silent, cancellationToken);
            host.Remember_TopicMessage(messageThreadId, messageId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.Log_Warning(Describe_Scope(messageThreadId), $"Settings reply could not be sent: {ex.Message}");
        }
    }

    async Task Answer_BestEffort_Async(ITelegramApiClient client, string callbackQueryId, string text, CancellationToken cancellationToken)
    {
        try
        {
            await client.Answer_CallbackQuery_Async(callbackQueryId, text, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.Log_Warning(ChannelDiscovery.GENERAL_ORCH_ID, $"answerCallbackQuery failed for a settings tap: {ex.Message}");
        }
    }

    async Task Delete_BestEffort_Async(ITelegramApiClient client, long messageId, CancellationToken cancellationToken)
    {
        try
        {
            await client.Delete_Message_Async(messageId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.Log_Info(ChannelDiscovery.GENERAL_ORCH_ID, $"The replaced settings menu {messageId} could not be taken down ({ex.Message}) — it stays up, no longer live");
        }
    }

    (IReadOnlyList<ISettingReading> Readings, string PresetName) Read_Snapshot()
    {
        return SettingsSnapshot_Reader.Read_All_FromDisk(paths, null, log);
    }

    static ISettingReading Find_Reading(IReadOnlyList<ISettingReading> readings, string path)
    {
        return readings.First(reading => reading.Definition.Path == path);
    }

    static int Index_Of(string path)
    {
        for (var index = 0; index < Catalog.ALL.Count; index++)
        {
            if (Catalog.ALL[index].Path == path)
                return index;
        }

        throw new ArgumentException($"'{path}' is not a row of SettingsCatalog.ALL");
    }

    /// <summary>The held value for this row, or null when none is held for it or its window has passed (D9: a lapsed step writes nothing).</summary>
    string? Find_HeldText_OrNull(ISettingsReplyStep? step, string path)
    {
        return step != null && step.Path == path && clock.UtcNow < step.ExpiresUtc ? step.HeldText_OrNull : null;
    }

    /// <summary>A secret's refusal or failure, with its typed value taken out wherever it was quoted.</summary>
    static string Mask_Secret(string text, bool isSecret, string? typed)
    {
        if (!isSecret || string.IsNullOrEmpty(typed))
            return text;

        return text.Replace(typed, SECRET_MASK, StringComparison.Ordinal);
    }

    static string Prefix_Note(string? note, string text)
    {
        return note == null ? text : $"{note}\n\n{text}";
    }

    static string Describe_Toast(string? note)
    {
        if (note == null)
            return "";

        return note.Length <= TOAST_LIMIT ? note : note[..(TOAST_LIMIT - 1)] + "…";
    }

    static string Describe_Lapsed(string path)
    {
        return $"The settings prompt for {path} had lapsed, so this message went on as usual. Send /settings to set it.";
    }

    string Describe_Scope(long? messageThreadId)
    {
        if (messageThreadId == null)
            return ChannelDiscovery.GENERAL_ORCH_ID;

        return store.Find_ByTelegramTopicId_OrNull(messageThreadId.Value)?.OrchId ?? ChannelDiscovery.GENERAL_ORCH_ID;
    }
}
