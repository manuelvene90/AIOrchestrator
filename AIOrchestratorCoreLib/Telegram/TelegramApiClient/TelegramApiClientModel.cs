using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace AIOrchestratorCoreLib.Telegram.TelegramApiClient;

internal sealed class TelegramApiClientModel : ITelegramApiClient
{
    /// <summary>
    /// How many times a 429 is honoured before the failure is handed to the caller. Telegram answers
    /// a rate limit with a wait, not a refusal, so retrying is the correct reading — but it has to
    /// TERMINATE: an endpoint that answers 429 for ever would otherwise hold a mirror tick open
    /// indefinitely, which is the shape of every unbounded-retry defect this file has already paid
    /// for. Two attempts after the first cover a burst; a third means the limit is not a burst.
    /// </summary>
    const int RATE_LIMIT_RETRIES = 2;

    readonly HttpClient _httpClient;
    readonly string _botToken;
    readonly long _supergroupChatId;

    /// <summary>
    /// THE ONE PLACE THE OUTBOUND RATE LIMIT LIVES — now two buckets rather than one, and held
    /// OUTSIDE this class so the send allowance can survive a restart (brief F5). See
    /// <see cref="TelegramSendBudget.ITelegramSendBudget"/> for both, and
    /// <see cref="TokenBucket_Gate"/> for why a bucket and not a delay.
    /// </summary>
    readonly TelegramSendBudget.ITelegramSendBudget _budget;

    /// <summary>
    /// WHAT TELEGRAM HAS ALREADY TOLD US, shared by every call that passes through this class. The
    /// buckets above are an ESTIMATE of a limit nobody publishes; a 429 carries the limit's own
    /// answer, and until now that answer was known only to the call site that received it while
    /// every other surface kept ringing the same bell. See <see cref="OutboundCooldowns"/> for the
    /// measurement (388 in one hour, 376 of them one bell).
    /// </summary>
    readonly OutboundCooldowns _cooldowns = new();

    public TelegramApiClientModel(string botToken, long supergroupChatId, TelegramSendBudget.ITelegramSendBudget budget)
        : this(botToken, supergroupChatId, budget, transport: null)
    {
    }

    /// <summary>
    /// <paramref name="transport"/> is THE TEST SEAM, and null everywhere in production (brief F9).
    ///
    /// <para>
    /// Until now nothing in this file was reachable from a test: it builds its own
    /// <see cref="HttpClient"/>, so every URL it composes, every payload it shapes and every status
    /// it classifies could only be checked by reading it. That is a lot of untested surface for the
    /// one component whose mistakes are invisible until the owner's phone goes quiet — the
    /// <c>getUpdates</c> query string alone decides whether button taps arrive at all.
    /// </para>
    /// <para>
    /// A handler rather than an <c>HttpClient</c>, so the timeout below stays the shipped one in a
    /// test as well: what is faked is the wire, not the client's own configuration.
    /// </para>
    /// </summary>
    internal TelegramApiClientModel(
        string botToken,
        long supergroupChatId,
        TelegramSendBudget.ITelegramSendBudget budget,
        HttpMessageHandler? transport)
    {
        _budget = budget;
        _botToken = botToken;
        _supergroupChatId = supergroupChatId;
        // POOLED CONNECTIONS ARE RECYCLED, and this daemon is the case that needs it: it runs for
        // WEEKS on the VPS, and SocketsHttpHandler's default PooledConnectionLifetime is Infinite —
        // a connection opened at start is kept and reused for the life of the process, so it never
        // re-resolves DNS. api.telegram.org sits behind a rotating set of addresses; when the one
        // this process pinned is withdrawn, every call fails on a socket that will never be
        // replaced, and only a restart brings the bridge back. Two minutes is the documented remedy
        // (it is also what ASP.NET Core's own factory defaults to): the handler retires idle-able
        // connections at that age and the next request re-resolves, at the cost of one handshake.
        //
        // The idle timeout is separate and shorter for the same reason it always is: an idle
        // connection that a middlebox has already dropped is worse than no connection.
        HttpMessageHandler handler = transport ?? new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(90),
        };

        // disposeHandler ONLY for the one we built. A handler handed in by a caller is the
        // caller's to dispose; taking ownership of it would mean a fake reused across two clients
        // is dead by the second, whenever this type grows an IDisposable.
        _httpClient = new HttpClient(handler, disposeHandler: transport == null)
        {
            // Must exceed the getUpdates long-poll timeout with margin.
            Timeout = TimeSpan.FromSeconds(90),
        };
    }

    public async Task<long> Create_ForumTopic_Async(string topicName, int? iconColor, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["name"] = topicName,
        };

        // GUARDED AT THE WIRE, not only at the caller. Telegram refuses the WHOLE call for a colour
        // outside its six, and a refused createForumTopic costs the orchestration its topic — its
        // entries then mirror into General, which Resolve_ThreadId_OrNull_Async calls the one
        // failure here worse than a lost tick. config.json is hand-edited, so this is reachable.
        if (iconColor != null && TopicColor_Rotation.Is_Permitted(iconColor.Value))
            payload["icon_color"] = iconColor.Value;

        var resultJson = await Post_Async("createForumTopic", payload, cancellationToken);

        var root = JsonNode.Parse(resultJson) as JsonObject
            ?? throw new Exception($"createForumTopic returned non-object JSON: {resultJson}");

        var threadIdNode = root["result"]?["message_thread_id"]
            ?? throw new Exception($"createForumTopic response has no result.message_thread_id: {resultJson}");

        return threadIdNode.GetValue<long>();
    }

    public async Task Edit_ForumTopic_Async(long messageThreadId, string newName, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["message_thread_id"] = messageThreadId,
            ["name"] = newName,
        };

        await Post_Async("editForumTopic", payload, cancellationToken, TelegramCallClasses.Control);
    }

    public async Task Edit_GeneralForumTopic_Async(string newName, CancellationToken cancellationToken)
    {
        // A DIFFERENT METHOD, not editForumTopic with a special id. Telegram's General topic has no
        // message_thread_id at all — that absence is how the whole app recognises it — so the only
        // way to rename it is the dedicated call.
        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["name"] = newName,
        };

        await Post_Async("editGeneralForumTopic", payload, cancellationToken, TelegramCallClasses.Control);
    }

    public async Task Delete_ForumTopic_Async(long messageThreadId, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["message_thread_id"] = messageThreadId,
        };

        await Post_Async("deleteForumTopic", payload, cancellationToken, TelegramCallClasses.Control);
    }

    public async Task Remove_TopicCreationPin_Async(long messageThreadId, CancellationToken cancellationToken)
    {
        var unpinPayload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["message_thread_id"] = messageThreadId,
        };

        await Post_Async("unpinAllForumTopicMessages", unpinPayload, cancellationToken);

        try
        {
            // The service message's id equals the thread id; deleting it removes the header
            // entirely. Cosmetic — the unpin above is the actual fix, so failures are ignored.
            var deletePayload = new JsonObject
            {
                ["chat_id"] = _supergroupChatId,
                ["message_id"] = messageThreadId,
            };

            await Post_Async("deleteMessage", deletePayload, cancellationToken, TelegramCallClasses.Control);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Requires can_delete_messages; without it the unpinned service message just stays.
        }
    }

    /// <summary>
    /// THE TWO KEYS EVERY OWNER-FACING TEXT SEND CARRIES, in one place so a new send cannot forget
    /// them.
    ///
    /// <para>
    /// <c>disable_notification</c> comes from the CALLER, because whether the owner's phone rings is
    /// a decision about the message, not about the transport — see <see cref="TelegramSendSounds"/>.
    /// </para>
    /// <para>
    /// <c>link_preview_options.is_disabled</c> is unconditional and needs no caller: a channel entry
    /// that happens to mention a URL used to arrive with Telegram's own card unfurled underneath it,
    /// which is a second screenful the owner did not ask for and which pushes the message they were
    /// reading off the top. Nothing this app sends is a link the owner wants previewed.
    /// </para>
    /// </summary>
    /// <summary>
    /// The multipart twin of <see cref="Stamp_DeliveryOptions"/>. An upload carries no link preview
    /// to disable, so only the notification choice crosses.
    /// </summary>
    static void Stamp_DeliveryOption(MultipartFormDataContent form, TelegramSendSounds sound)
    {
        form.Add(new StringContent(sound == TelegramSendSounds.Silent ? "true" : "false"), "disable_notification");
    }

    static void Stamp_DeliveryOptions(JsonObject payload, TelegramSendSounds sound)
    {
        payload["disable_notification"] = sound == TelegramSendSounds.Silent;
        payload["link_preview_options"] = new JsonObject { ["is_disabled"] = true };
    }

    public async Task<long?> Send_Message_Async(long? messageThreadId, string text, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["text"] = text,
        };

        if (messageThreadId != null)
            payload["message_thread_id"] = messageThreadId.Value;

        Stamp_DeliveryOptions(payload, sound);

        var responseJson = await Post_Async("sendMessage", payload, cancellationToken, TelegramCallClasses.Message);

        return Read_MessageId_OrNull(responseJson);
    }

    public async Task<long?> Send_HtmlMessage_Async(long? messageThreadId, string html, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["text"] = html,
            ["parse_mode"] = "HTML",
        };

        if (messageThreadId != null)
            payload["message_thread_id"] = messageThreadId.Value;

        Stamp_DeliveryOptions(payload, sound);

        return Read_MessageId_OrNull(await Post_Async("sendMessage", payload, cancellationToken, TelegramCallClasses.Message));
    }

    /// <summary>
    /// Not metered by the send bucket: it creates no message, and a refresh that queued behind a burst
    /// of real sends would arrive after Telegram had already cleared the previous one.
    /// </summary>
    public async Task Send_TypingAction_Async(long? messageThreadId, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["action"] = "typing",
        };

        if (messageThreadId != null)
            payload["message_thread_id"] = messageThreadId.Value;

        await Post_Async("sendChatAction", payload, cancellationToken);
    }

    public async Task<long?> Send_HtmlMessageWithButtons_Async(long? messageThreadId, string html, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["text"] = html,
            ["parse_mode"] = "HTML",
            ["reply_markup"] = new JsonObject { ["inline_keyboard"] = Build_InlineKeyboard(Wrap_OneButtonPerRow(buttons)) },
        };

        if (messageThreadId != null)
            payload["message_thread_id"] = messageThreadId.Value;

        Stamp_DeliveryOptions(payload, sound);

        return Read_MessageId_OrNull(await Post_Async("sendMessage", payload, cancellationToken, TelegramCallClasses.Message));
    }

    public async Task Edit_MessageText_Async(long messageId, string text, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["message_id"] = messageId,
            ["text"] = text,
        };

        Hold_UnlessThisMessageMayBeEdited(messageId);

        await Post_Async("editMessageText", payload, cancellationToken, TelegramCallClasses.Control,
            OutboundCooldown_Keys.For_Message(messageId));
    }

    public async Task Edit_HtmlMessageText_Async(long messageId, string html, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["message_id"] = messageId,
            ["text"] = html,
            ["parse_mode"] = "HTML",
        };

        Hold_UnlessThisMessageMayBeEdited(messageId);

        await Post_Async("editMessageText", payload, cancellationToken, TelegramCallClasses.Control,
            OutboundCooldown_Keys.For_Message(messageId));
    }

    /// <summary>
    /// Rewrites a message AND the buttons under it in one call.
    ///
    /// The plain edit above is not a subset of this one — it sends no <c>reply_markup</c>, and
    /// Telegram reads that as "remove the keyboard". That is exactly what the question flow wants
    /// (recording a choice strips the buttons), and exactly what the hold receipt does NOT: its
    /// button has to CHANGE from ⏸ Wait to ▶ GO as the state changes. Two methods rather than an
    /// optional argument, so neither behaviour can be reached by accident.
    /// </summary>
    public Task Edit_MessageTextWithButtons_Async(long messageId, string text, IReadOnlyList<(string Data, string Label)> buttons, CancellationToken cancellationToken)
    {
        return Edit_MessageTextWithButtonRows_Async(messageId, text, Wrap_OneButtonPerRow(buttons), cancellationToken);
    }

    public async Task Edit_MessageTextWithButtonRows_Async(long messageId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["message_id"] = messageId,
            ["text"] = text,
            ["reply_markup"] = new JsonObject { ["inline_keyboard"] = Build_InlineKeyboard(buttonRows) },
        };

        Hold_UnlessThisMessageMayBeEdited(messageId);

        await Post_Async("editMessageText", payload, cancellationToken, TelegramCallClasses.Control,
            OutboundCooldown_Keys.For_Message(messageId));
    }

    /// <summary>
    /// One button per row — the shape every DECISION keyboard wants, because an option's label is a
    /// sentence and two of them side by side are each half-readable.
    /// </summary>
    static IReadOnlyList<IReadOnlyList<(string Data, string Label)>> Wrap_OneButtonPerRow(IReadOnlyList<(string Data, string Label)> buttons)
    {
        return [.. buttons.Select(button => (IReadOnlyList<(string Data, string Label)>)[button])];
    }

    /// <summary>
    /// THE ONE PLACE the inline-keyboard markup is built. It was written out twice — once in the
    /// edit and once in the send — and a third copy was about to be added for the status line's
    /// command bar; two copies of a formatter is how they drift.
    /// </summary>
    static JsonArray Build_InlineKeyboard(IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows)
    {
        var keyboardRows = new JsonArray();

        foreach (var row in buttonRows)
        {
            var buttons = new JsonArray();

            foreach (var button in row)
            {
                buttons.Add(new JsonObject
                {
                    ["text"] = button.Label,
                    ["callback_data"] = button.Data,
                });
            }

            keyboardRows.Add(buttons);
        }

        return keyboardRows;
    }

    static long? Read_MessageId_OrNull(string responseJson)
    {
        try
        {
            var node = (JsonNode.Parse(responseJson) as JsonObject)?["result"]?["message_id"];

            if (node == null)
                return null;

            return node.GetValue<long>();
        }
        catch
        {
            return null;
        }
    }

    public Task<long?> Send_MessageWithButtons_Async(long? messageThreadId, string text, IReadOnlyList<(string Data, string Label)> buttons, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        return Send_MessageWithButtonRows_Async(messageThreadId, text, Wrap_OneButtonPerRow(buttons), sound, cancellationToken);
    }

    public async Task<long?> Send_MessageWithButtonRows_Async(long? messageThreadId, string text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> buttonRows, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["text"] = text,
            ["reply_markup"] = new JsonObject { ["inline_keyboard"] = Build_InlineKeyboard(buttonRows) },
        };

        if (messageThreadId != null)
            payload["message_thread_id"] = messageThreadId.Value;

        Stamp_DeliveryOptions(payload, sound);

        // The id is needed later: on a tap this message is rewritten to show the chosen option.
        return Read_MessageId_OrNull(await Post_Async("sendMessage", payload, cancellationToken, TelegramCallClasses.Message));
    }

    public async Task Answer_CallbackQuery_Async(string callbackQueryId, string text, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["callback_query_id"] = callbackQueryId,
            ["text"] = text,
        };

        await Post_Async("answerCallbackQuery", payload, cancellationToken, TelegramCallClasses.Control);
    }

    public async Task Remove_MessageButtons_Async(long messageId, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["message_id"] = messageId,
        };

        Hold_UnlessThisMessageMayBeEdited(messageId);

        await Post_Async("editMessageReplyMarkup", payload, cancellationToken, TelegramCallClasses.Control,
            OutboundCooldown_Keys.For_Message(messageId));
    }

    public async Task Delete_Message_Async(long messageId, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["message_id"] = messageId,
        };

        await Post_Async("deleteMessage", payload, cancellationToken, TelegramCallClasses.Control);
    }

    public async Task Send_Photo_Async(long? messageThreadId, string filePath, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        using var form = Build_MultipartForm(messageThreadId);

        var photoBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);

        Refuse_IfOverCap(photoBytes.Length, TelegramFileCaps.MAX_PHOTO_BYTES, "sendPhoto", $"'{filePath}'");

        form.Add(new ByteArrayContent(photoBytes), "photo", Path.GetFileName(filePath));

        Stamp_DeliveryOption(form, sound);

        await Post_Multipart_Async("sendPhoto", form, $"for '{filePath}'", cancellationToken);
    }

    /// <summary>
    /// The whole entry as a file, so a long one is something the owner can scroll and keep rather
    /// than four chat messages to stitch together. The caption is parsed as HTML for the same reason
    /// every other owner-facing send is: agents write Markdown, and the choice of parse mode is the
    /// only thing that decides whether the owner reads it or reads its markers.
    /// </summary>
    public async Task Send_Document_Async(long? messageThreadId, string fileName, byte[] content, string captionHtml, TelegramSendSounds sound, CancellationToken cancellationToken)
    {
        using var form = Build_MultipartForm(messageThreadId);

        Refuse_IfOverCap(content.Length, TelegramFileCaps.MAX_DOCUMENT_BYTES, "sendDocument", $"'{fileName}'");

        form.Add(new StringContent(captionHtml), "caption");
        form.Add(new StringContent("HTML"), "parse_mode");
        form.Add(new ByteArrayContent(content), "document", fileName);

        Stamp_DeliveryOption(form, sound);

        await Post_Multipart_Async("sendDocument", form, $"for '{fileName}' ({content.Length} bytes)", cancellationToken);
    }

    /// <summary>
    /// THE LAST LINE OF DEFENCE ON A SIZE CAP (brief F7). <see cref="Bridge.EntryAttachment_Policy"/>
    /// already refuses an oversized <c>IMAGE:</c> or <c>ATTACH:</c> and tells the AGENT why — that is
    /// the useful refusal and it stays where it is. This one exists because the policy guards ONE
    /// path: the screenshots and the undelivered-entry digest call these methods directly, and an
    /// oversized one reached Telegram to be answered with a 413 or an opaque 400.
    ///
    /// <para>
    /// A plain <see cref="Exception"/> rather than a <see cref="TelegramApiException"/>, deliberately:
    /// Telegram did not answer this, we did, and manufacturing a status code for a call that never
    /// left the machine would put a fiction in front of every caller that classifies by status.
    /// </para>
    /// </summary>
    static void Refuse_IfOverCap(long lengthBytes, long capBytes, string method, string subject)
    {
        if (lengthBytes <= capBytes)
            return;

        throw new Exception(
            $"Telegram '{method}' refused before sending: {subject} is {TelegramFileCaps.In_Megabytes(lengthBytes)} MB, "
            + $"over Telegram's {TelegramFileCaps.In_Megabytes(capBytes)} MB cap");
    }

    /// <summary>The part every multipart upload shares: which chat, and which topic inside it.</summary>
    MultipartFormDataContent Build_MultipartForm(long? messageThreadId)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(_supergroupChatId.ToString()), "chat_id" },
        };

        if (messageThreadId != null)
            form.Add(new StringContent(messageThreadId.Value.ToString()), "message_thread_id");

        return form;
    }

    /// <summary>
    /// The multipart twin of <see cref="Post_Async"/>, and it exists so the two uploads cannot drift
    /// apart on the two things that matter.
    ///
    /// <para>
    /// METERED LIKE ANY OTHER MESSAGE. A multipart call builds its own request and therefore does not
    /// pass through <see cref="Post_Async"/> — which is exactly how a message-creating call ends up
    /// outside the one place the rate limit lives. It creates a message in the group, so it spends the
    /// group's allowance whether or not it shares that code path.
    /// </para>
    /// <para>
    /// AND IT FAILS AS A <see cref="TelegramApiException"/>, carrying the status. Callers classify a
    /// failure by status and never by sentence — a 400 is Telegram refusing on the merits, everything
    /// else is an outcome nobody knows — so an upload that threw a bare Exception would be
    /// unclassifiable at every site that has to decide whether to retry.
    /// </para>
    /// <para>
    /// It does NOT retry a 429 inline. Post_Async's retry exists for the mirror's 2 s tick; an upload
    /// is best-effort at every call site here, so the honest move is to hand the failure back rather
    /// than hold a tick open re-sending a file.
    /// </para>
    /// </summary>
    async Task Post_Multipart_Async(string method, MultipartFormDataContent form, string subject, CancellationToken cancellationToken)
    {
        await Wait_ForBudget_Async(TelegramCallClasses.Message, cancellationToken);

        var response = await _httpClient.PostAsync(Build_MethodUrl(method), form, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new TelegramApiException(
                (int)response.StatusCode,
                $"Telegram '{method}' failed with HTTP {(int)response.StatusCode} {subject}: {body}",
                Read_RetryAfterSeconds_OrNull(body));
        }
    }

    public async Task Set_MyCommands_Async(IReadOnlyList<(string Command, string Description)> commands, CancellationToken cancellationToken)
    {
        // REGISTERED UNDER EVERY SCOPE, and the admin one is the whole reason (owner, 2026-08-24:
        // "add the native command menu / in every chat").
        //
        // Telegram resolves a user's command menu down a fallback chain, most specific first:
        // chat_member → chat_administrators → chat → all_chat_administrators → all_group_chats →
        // default. The owner is an ADMINISTRATOR of their own supergroup, so in the topics they
        // actually live in, `all_chat_administrators` is consulted BEFORE `all_group_chats` — and
        // the two scopes we used to set were the two that an admin reaches last, or not at all.
        //
        // The same list goes to all four on purpose: the chain stops at the first scope that carries
        // commands, so a scope set to a DIFFERENT (or empty) list does not merge with the ones below
        // it, it replaces them.
        await Post_Async("setMyCommands", Build_CommandsPayload(commands, null), cancellationToken);
        await Post_Async("setMyCommands", Build_CommandsPayload(commands, "all_private_chats"), cancellationToken);
        await Post_Async("setMyCommands", Build_CommandsPayload(commands, "all_group_chats"), cancellationToken);
        await Post_Async("setMyCommands", Build_CommandsPayload(commands, "all_chat_administrators"), cancellationToken);
    }

    /// <summary>
    /// THE `/` BUTTON IS A SEPARATE FACT FROM THE COMMANDS, and that is what made it intermittent.
    ///
    /// setMyCommands says WHAT the menu contains; the chat's menu button says WHETHER the client
    /// offers one. Left unset it is `default`, and each client then decides for itself — so the
    /// owner saw the `/` in some topics and not others, with the command list correct in all of
    /// them. Their report, 2026-08-25, of a screenshot pointing straight at it: *"This is not always
    /// present. Only sometimes. It's very convenient, can't you make it ALWAYS present?"*
    ///
    /// Set WITHOUT a chat_id on purpose: that writes the account-wide default for this bot, so it
    /// covers the supergroup, every topic inside it, and any private chat, and it does not have to
    /// be re-applied per topic as new orchestrations create them. A per-chat call would fix the
    /// topics that exist today and leave tomorrow's back on the client default.
    /// </summary>
    public async Task Set_ChatMenuButton_ToCommands_Async(CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["menu_button"] = new JsonObject { ["type"] = "commands" },
        };

        await Post_Async("setChatMenuButton", payload, cancellationToken);
    }

    static JsonObject Build_CommandsPayload(IReadOnlyList<(string Command, string Description)> commands, string? scopeType)
    {
        var commandsArray = new JsonArray();

        foreach (var command in commands)
        {
            commandsArray.Add(new JsonObject
            {
                ["command"] = command.Command,
                ["description"] = command.Description,
            });
        }

        var payload = new JsonObject
        {
            ["commands"] = commandsArray,
        };

        if (scopeType != null)
            payload["scope"] = new JsonObject { ["type"] = scopeType };

        return payload;
    }

    /// <summary>
    /// THE POLL PAYS ITS WAY LIKE EVERY OTHER CALL. It used to be the one method in this file that
    /// went straight to <see cref="HttpClient"/>: no bucket, no 429 handling, no shared throw site.
    ///
    /// <para>
    /// It was easy to miss because a long poll is low volume — one call per <c>timeoutSeconds</c>
    /// while things are quiet. It is not low volume when it FAILS: every error path in the inbound
    /// loop retries with a backoff that starts small, so a Telegram-side 429 met an unmetered
    /// caller hammering the same endpoint, and the only brake was the loop's own backoff, which
    /// knows nothing about <c>retry_after</c>. Metered on the CONTROL bucket rather than the
    /// message one: this creates no message in the group, so charging it to the twenty-a-minute
    /// ceiling would spend the owner's delivery allowance on housekeeping.
    /// </para>
    /// <para>
    /// The bucket is taken BEFORE the long poll begins and not held across it — a token buys the
    /// right to start a poll, and the twenty seconds it then spends waiting
    /// (<c>BridgeEngineModel.INBOUND_LONG_POLL_SECONDS</c>) are Telegram's, not ours.
    /// </para>
    /// <para>
    /// WHAT THE INLINE RETRY ACTUALLY COVERS, stated narrowly because the first version of this
    /// comment claimed more than the code does: only a <c>retry_after</c> of two seconds or less
    /// (<see cref="TokenBucket_Gate.MAXIMUM_CONTROL_RETRY_WAIT"/>). That is the burst case. A real
    /// Telegram flood wait — anything longer — is thrown to the inbound loop exactly as before,
    /// and that loop still backs off on its own ladder without reading <c>retry_after</c>. So this
    /// buys the short case and leaves the long one where it was; it does not make the bridge
    /// <c>retry_after</c>-aware, and saying otherwise would be a comment the code cannot cash.
    /// </para>
    /// </summary>
    public async Task<string> Get_UpdatesJson_Async(long offset, int timeoutSeconds, CancellationToken cancellationToken)
    {
        // allowed_updates = ["message","callback_query"] — without callback_query, inline-button
        // taps would never reach the bridge.
        var url = $"{Build_MethodUrl("getUpdates")}?offset={offset}&timeout={timeoutSeconds}&allowed_updates=%5B%22message%22%2C%22callback_query%22%5D";

        // ONE TOKEN PER LOGICAL POLL, TAKEN OUTSIDE THE RETRY LOOP. Inside it, a 429'd poll would
        // spend up to three tokens for one poll — on a bucket that refills one every two seconds
        // and that the mirror's edits are already competing for. The retry is Telegram asking us
        // to wait, not a second poll: charging it again would make the rate limit cost the owner's
        // inbound latency twice over.
        await Wait_ForBudget_Async(TelegramCallClasses.Control, cancellationToken);

        for (var attempt = 0; ; attempt++)
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
                return body;

            var statusCode = (int)response.StatusCode;
            var retryAfterSeconds = Read_RetryAfterSeconds_OrNull(body);

            if (statusCode == 429 && attempt < RATE_LIMIT_RETRIES)
            {
                var wait = TokenBucket_Gate.Read_RetryAfter(retryAfterSeconds);

                // A 429 with no retry_after still has to cost something, or the retry is immediate
                // and lands on the same wall.
                if (wait <= TimeSpan.Zero)
                    wait = TimeSpan.FromSeconds(TokenBucket_Gate.DEFAULT_REFILL_SECONDS / TokenBucket_Gate.CONTROL_CAPACITY);

                if (wait <= TokenBucket_Gate.MAXIMUM_CONTROL_RETRY_WAIT)
                {
                    await Task.Delay(wait, cancellationToken);
                    continue;
                }
            }

            // UNCHANGED FOR EVERY OTHER STATUS, and that matters: the inbound loop reads 409 by its
            // code to name the two-pollers situation (brief B), and the wording of this message is
            // what the log has always carried.
            throw new TelegramApiException(statusCode, $"getUpdates failed with HTTP {statusCode}: {body}", retryAfterSeconds);
        }
    }

    public async Task<string> Get_BotUsername_Async(CancellationToken cancellationToken)
    {
        var resultJson = await Post_Async("getMe", new JsonObject(), cancellationToken);

        var root = JsonNode.Parse(resultJson) as JsonObject
            ?? throw new Exception($"getMe returned non-object JSON: {resultJson}");

        return root["result"]?["username"]?.GetValue<string>() ?? "";
    }

    public async Task Delete_Webhook_Async(bool dropPendingUpdates, CancellationToken cancellationToken)
    {
        await Post_Async("deleteWebhook", new JsonObject { ["drop_pending_updates"] = dropPendingUpdates }, cancellationToken);
    }

    public async Task Set_MessageReaction_Async(long messageId, string? emoji, CancellationToken cancellationToken)
    {
        var reactions = new JsonArray();

        if (emoji != null)
            reactions.Add(new JsonObject { ["type"] = "emoji", ["emoji"] = emoji });

        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["message_id"] = messageId,

            // An EMPTY array clears the reaction — that is Telegram's own spelling for "remove",
            // not an omitted field, and omitting it is a 400.
            ["reaction"] = reactions,
        };

        // CONTROL, not Message: it creates nothing in the group. It changes a message that is
        // already there, which is exactly what that bucket is for.
        await Post_Async("setMessageReaction", payload, cancellationToken, TelegramCallClasses.Control);
    }

    public async Task<byte[]> Download_File_Async(string fileId, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["file_id"] = fileId,
        };

        var resultJson = await Post_Async("getFile", payload, cancellationToken);

        var root = JsonNode.Parse(resultJson) as JsonObject
            ?? throw new Exception($"getFile returned non-object JSON: {resultJson}");

        var filePath = root["result"]?["file_path"]?.GetValue<string>()
            ?? throw new Exception($"getFile response has no result.file_path: {resultJson}");

        // REFUSED BEFORE A BYTE IS PULLED (brief F7). The download is read fully into memory, so an
        // unbounded one is a way to take the bridge down from a phone — and 20 MB is Telegram's own
        // ceiling for a bot anyway, so anything above it would fail after we had paid for it.
        // getFile's file_size is advisory (it can be absent), which is why the read below is capped
        // as well rather than instead.
        var declaredSize = root["result"]?["file_size"]?.GetValue<long>();

        if (declaredSize > TelegramFileCaps.MAX_DOWNLOAD_BYTES)
            throw new Exception($"Telegram file '{fileId}' is {TelegramFileCaps.In_Megabytes(declaredSize.Value)} MB, over the {TelegramFileCaps.In_Megabytes(TelegramFileCaps.MAX_DOWNLOAD_BYTES)} MB a bot may download — not fetched");

        var downloadUrl = $"https://api.telegram.org/file/bot{_botToken}/{filePath}";
        var response = await _httpClient.GetAsync(downloadUrl, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new TelegramApiException((int)response.StatusCode, $"Telegram file download failed with HTTP {(int)response.StatusCode} for file id '{fileId}'");

        if (response.Content.Headers.ContentLength > TelegramFileCaps.MAX_DOWNLOAD_BYTES)
            throw new Exception($"Telegram file '{fileId}' is {TelegramFileCaps.In_Megabytes(response.Content.Headers.ContentLength!.Value)} MB, over the {TelegramFileCaps.In_Megabytes(TelegramFileCaps.MAX_DOWNLOAD_BYTES)} MB a bot may download — not fetched");

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>
    /// Every Telegram call goes through here, which is what makes the rate limit expressible once.
    ///
    /// <para>
    /// <paramref name="rateLimited"/> is OPT-IN, and what opts in is exactly the set of calls that
    /// CREATE A MESSAGE IN THE GROUP — the thing Telegram's 20-a-minute ceiling actually counts.
    /// Metering everything was the first shape of this and it was wrong in both directions: it spent
    /// the message allowance on edits and deletes, which do not consume it, and it queued
    /// <c>answerCallbackQuery</c> behind the mirror's backlog — a call Telegram gives ten seconds
    /// before the button spinner hangs on the owner's phone. Edits and deletes have their own, far
    /// higher limits, and the 429 handling below is what covers them.
    /// </para>
    /// <para>
    /// A 429 IS HONOURED WITH TELEGRAM'S OWN NUMBER, not ours. The bucket is a guess at a limit we
    /// cannot observe; <c>retry_after</c> is the limit answering. Retries are bounded — see
    /// <see cref="RATE_LIMIT_RETRIES"/> — and a request that still fails is thrown exactly as
    /// before, so every caller's existing classification of the failure is unchanged.
    /// </para>
    /// </summary>
    async Task<string> Post_Async(string method, JsonObject payload, CancellationToken cancellationToken, TelegramCallClasses callClass = TelegramCallClasses.Unmetered, string? cooldownKey = null)
    {
        for (var attempt = 0; ; attempt++)
        {
            await Wait_ForBudget_Async(callClass, cancellationToken);

            var response = await _httpClient.PostAsJsonAsync(Build_MethodUrl(method), payload, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
                return body;

            var statusCode = (int)response.StatusCode;
            var retryAfterSeconds = Read_RetryAfterSeconds_OrNull(body);

            // RETRIED ONLY FOR THE CALLS THAT CREATE A MESSAGE, and only for a wait short enough to
            // spend inside a 2 s tick.
            //
            // Retrying everything cost far more than it bought. `editMessageText` and
            // `answerCallbackQuery` come through here unmetered, so a 429 carrying
            // `retry_after: 300` slept 300 s, retried, slept 300 s and then threw — ten minutes
            // inside a single mirror tick, holding its channel-write allowance, with nothing
            // mirrored and no deadline swept for the duration. And for a callback query it was
            // pointless as well as expensive: Telegram invalidates one after about ten seconds, so
            // the wait guaranteed the retry would fail while the owner's spinner hung.
            //
            // Past the inline cap the failure is handed to the caller, which is where the per-channel
            // backoff and the outcome classification already live.
            // CONTROL CALLS ARE RETRIED TOO NOW (brief F5), under a much shorter cap. The comment
            // above records why they were forbidden outright: a retry_after of 300 slept five
            // minutes twice inside one two-second mirror tick, and a callback query is dead after
            // about ten seconds anyway. Both objections are about the LENGTH of the wait, not about
            // retrying, so the fix is the cap — two seconds — rather than the ban. An unmetered
            // call is still never retried: it is unmetered precisely because it must not queue.
            if (statusCode == 429 && callClass != TelegramCallClasses.Unmetered && attempt < RATE_LIMIT_RETRIES)
            {
                var wait = TokenBucket_Gate.Read_RetryAfter(retryAfterSeconds);

                // A 429 with no retry_after still has to cost something, or the retry is immediate
                // and lands on the same wall. One token's worth of time is the smallest honest wait.
                if (wait <= TimeSpan.Zero)
                    wait = TimeSpan.FromSeconds(TokenBucket_Gate.DEFAULT_REFILL_SECONDS / TokenBucket_Gate.DEFAULT_CAPACITY);

                var ceiling = callClass == TelegramCallClasses.Control
                    ? TokenBucket_Gate.MAXIMUM_CONTROL_RETRY_WAIT
                    : TokenBucket_Gate.MAXIMUM_INLINE_RETRY_WAIT;

                if (wait <= ceiling)
                {
                    await Task.Delay(wait, cancellationToken);
                    continue;
                }
            }

            // THE WINDOW IS WRITTEN DOWN HERE, at the one place a rate limit is finally admitted
            // rather than retried — so the next caller for this same target reads the note instead
            // of ringing the bell. Only for a call that named a door: an unkeyed call has no target
            // to hold, and inventing one would hold traffic Telegram never refused.
            if (statusCode == 429 && cooldownKey != null)
                _cooldowns.Note_RateLimited(cooldownKey, retryAfterSeconds, DateTime.UtcNow);

            throw new TelegramApiException(statusCode, $"Telegram '{method}' failed with HTTP {statusCode}: {body}", retryAfterSeconds);
        }
    }


    /// <summary>
    /// THE DOOR, CHECKED BEFORE THE BELL IS RUNG — and it throws
    /// <see cref="TelegramHeldException"/> rather than waiting.
    ///
    /// <para>
    /// Two reasons a message may not be edited right now, and they are different things. The
    /// COOLDOWN is Telegram's own answer to somebody else's attempt on this same message: measured
    /// on the VPS 2026-09-10, 376 of 388 HTTP 429 in one hour were two status lines re-attempted at
    /// the 2 s tick rate against a `retry_after` of 20-34 s, so every one of those attempts was a
    /// bell already known to be dead. The per-message SLOT is our own brake, sized from the same
    /// journal (<see cref="TokenBucket_Gate.MINIMUM_GAP_BETWEEN_EDITS_OF_ONE_MESSAGE"/>).
    /// </para>
    /// <para>
    /// NEITHER SLEEPS. This runs inside the 2 s mirror tick, and a wait here parks the mirror, the
    /// owner's deliveries and the deadline sweep along with it. The caller decides whether its
    /// message is worth waiting for: a status line comes back next tick, the owner's tap waits out
    /// the window it is handed (<see cref="RateLimitedRetry_Policy"/>).
    /// </para>
    /// </summary>
    void Hold_UnlessThisMessageMayBeEdited(long messageId)
    {
        var nowUtc = DateTime.UtcNow;
        var target = OutboundCooldown_Keys.For_Message(messageId);

        if (_cooldowns.Is_Held(target, nowUtc, out var notBeforeUtc))
            throw new TelegramHeldException(target, notBeforeUtc);

        var owed = _budget.Reserve_MessageEdit(messageId, nowUtc);

        if (owed > TimeSpan.Zero)
            throw new TelegramHeldException(target, nowUtc + owed);
    }

    /// <summary>Blocks until the class of call named by <paramref name="callClass"/> may go out.</summary>
    Task Wait_ForBudget_Async(TelegramCallClasses callClass, CancellationToken cancellationToken)
    {
        return callClass switch
        {
            TelegramCallClasses.Message => _budget.Wait_ForSend_Async(cancellationToken),
            TelegramCallClasses.Control => _budget.Wait_ForControl_Async(cancellationToken),
            _ => Task.CompletedTask,
        };
    }

    /// <summary>
    /// Telegram's <c>parameters.retry_after</c>, in seconds. Null for any body that does not carry
    /// one — including one that is not JSON at all, which a proxy or a gateway can produce.
    /// </summary>
    static int? Read_RetryAfterSeconds_OrNull(string body)
    {
        try
        {
            if (JsonNode.Parse(body) is not JsonObject root)
                return null;

            return root["parameters"]?["retry_after"]?.GetValue<int>();
        }
        catch
        {
            // Broad by intent: a body that will not parse simply carries no advice.
            return null;
        }
    }

    string Build_MethodUrl(string method)
    {
        return $"https://api.telegram.org/bot{_botToken}/{method}";
    }
}
