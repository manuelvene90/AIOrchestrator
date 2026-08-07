using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace AIOrchestratorCoreLib.Telegram.TelegramApiClient;

internal sealed class TelegramApiClientModel : ITelegramApiClient
{
    readonly HttpClient _httpClient;
    readonly string _botToken;
    readonly long _supergroupChatId;

    public TelegramApiClientModel(string botToken, long supergroupChatId)
    {
        _botToken = botToken;
        _supergroupChatId = supergroupChatId;
        _httpClient = new HttpClient
        {
            // Must exceed the getUpdates long-poll timeout with margin.
            Timeout = TimeSpan.FromSeconds(90),
        };
    }

    public async Task<long> Create_ForumTopic_Async(string topicName, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["name"] = topicName,
        };

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

        await Post_Async("editForumTopic", payload, cancellationToken);
    }

    public async Task Delete_ForumTopic_Async(long messageThreadId, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["message_thread_id"] = messageThreadId,
        };

        await Post_Async("deleteForumTopic", payload, cancellationToken);
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

            await Post_Async("deleteMessage", deletePayload, cancellationToken);
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

    public async Task Send_Message_Async(long? messageThreadId, string text, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["text"] = text,
        };

        if (messageThreadId != null)
            payload["message_thread_id"] = messageThreadId.Value;

        await Post_Async("sendMessage", payload, cancellationToken);
    }

    public async Task Send_MessageWithButtons_Async(long? messageThreadId, string text, IReadOnlyList<(string Data, string Label)> buttons, CancellationToken cancellationToken)
    {
        var keyboardRows = new JsonArray();

        foreach (var button in buttons)
        {
            keyboardRows.Add(new JsonArray(new JsonObject
            {
                ["text"] = button.Label,
                ["callback_data"] = button.Data,
            }));
        }

        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["text"] = text,
            ["reply_markup"] = new JsonObject { ["inline_keyboard"] = keyboardRows },
        };

        if (messageThreadId != null)
            payload["message_thread_id"] = messageThreadId.Value;

        await Post_Async("sendMessage", payload, cancellationToken);
    }

    public async Task Answer_CallbackQuery_Async(string callbackQueryId, string text, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["callback_query_id"] = callbackQueryId,
            ["text"] = text,
        };

        await Post_Async("answerCallbackQuery", payload, cancellationToken);
    }

    public async Task Remove_MessageButtons_Async(long messageId, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["chat_id"] = _supergroupChatId,
            ["message_id"] = messageId,
        };

        await Post_Async("editMessageReplyMarkup", payload, cancellationToken);
    }

    public async Task Send_Photo_Async(long? messageThreadId, string filePath, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(_supergroupChatId.ToString()), "chat_id");

        if (messageThreadId != null)
            form.Add(new StringContent(messageThreadId.Value.ToString()), "message_thread_id");

        var photoBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        form.Add(new ByteArrayContent(photoBytes), "photo", Path.GetFileName(filePath));

        var response = await _httpClient.PostAsync(Build_MethodUrl("sendPhoto"), form, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Telegram 'sendPhoto' failed with HTTP {(int)response.StatusCode} for '{filePath}': {body}");
    }

    public async Task Set_MyCommands_Async(IReadOnlyList<(string Command, string Description)> commands, CancellationToken cancellationToken)
    {
        // Registered under BOTH the default scope and all_group_chats: group chats sit at the
        // BOTTOM of Telegram's scope fallback chain, and some clients only surface the command
        // menu in a group when a group-applicable scope explicitly carries commands.
        await Post_Async("setMyCommands", Build_CommandsPayload(commands, null), cancellationToken);
        await Post_Async("setMyCommands", Build_CommandsPayload(commands, "all_group_chats"), cancellationToken);
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

    public async Task<string> Get_UpdatesJson_Async(long offset, int timeoutSeconds, CancellationToken cancellationToken)
    {
        // allowed_updates = ["message","callback_query"] — without callback_query, inline-button
        // taps would never reach the bridge.
        var url = $"{Build_MethodUrl("getUpdates")}?offset={offset}&timeout={timeoutSeconds}&allowed_updates=%5B%22message%22%2C%22callback_query%22%5D";

        var response = await _httpClient.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"getUpdates failed with HTTP {(int)response.StatusCode}: {body}");

        return body;
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

        var downloadUrl = $"https://api.telegram.org/file/bot{_botToken}/{filePath}";
        var response = await _httpClient.GetAsync(downloadUrl, cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Telegram file download failed with HTTP {(int)response.StatusCode} for file id '{fileId}'");

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    async Task<string> Post_Async(string method, JsonObject payload, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync(Build_MethodUrl(method), payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Telegram '{method}' failed with HTTP {(int)response.StatusCode}: {body}");

        return body;
    }

    string Build_MethodUrl(string method)
    {
        return $"https://api.telegram.org/bot{_botToken}/{method}";
    }
}
