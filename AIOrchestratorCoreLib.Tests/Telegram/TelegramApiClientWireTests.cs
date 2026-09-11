using System.Net;
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using AIOrchestratorCoreLib.Telegram.TelegramSendBudget;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// WHAT THIS CLIENT ACTUALLY PUTS ON THE WIRE — brief F9, and the first tests this file has ever
/// had.
///
/// <para>
/// It built its own <see cref="HttpClient"/>, so every URL it composes, every payload it shapes
/// and every status it classifies could only be checked by reading it. That is a lot of untested
/// surface on the one component whose mistakes are invisible until the owner's phone goes quiet:
/// drop <c>callback_query</c> from the <c>getUpdates</c> query string and every button in the
/// system stops working, with no error anywhere.
/// </para>
/// </summary>
public class TelegramApiClientWireTests
{
    const string TOKEN = "12345:test-token";
    const long CHAT_ID = -1001234567890;

    /// <summary>
    /// THE TWO DELIVERY KEYS, ASSERTED ON THE WIRE — the only place they exist.
    ///
    /// <para>
    /// `disable_notification` and `link_preview_options` are set inside
    /// <c>TelegramApiClientModel</c>, which is `internal sealed` with no `InternalsVisibleTo`: an
    /// adversarial review of the change that introduced them found both "implemented and
    /// unverifiable", because every fake in the suite takes the sound argument and discards it. This
    /// transport is the seam that closes that — it is the request Telegram would have received.
    /// </para>
    /// <para>
    /// The owner's rule, 2026-09-09: the supervisor's own words ring; status, receipts and app
    /// bookkeeping do not. So the flag has to be the CALLER's choice on the wire, not a constant.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(TelegramSendSounds.Rings, "false")]
    [InlineData(TelegramSendSounds.Silent, "true")]
    public async Task TheSoundTheCallerChose_IsTheDisableNotificationFlagOnTheWire(TelegramSendSounds sound, string expectedFlag)
    {
        var transport = new RecordingTransport_Fake();
        transport.Answer_With(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":51}}""");

        await Build_Client(transport).Send_Message_Async(5, "hello", sound, CancellationToken.None);

        var request = Assert.Single(transport.Requests);

        Assert.Contains($"\"disable_notification\":{expectedFlag}", request.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// AND NO TEXT SEND EVER UNFURLS A LINK. Unconditional and not a caller's choice: a channel
    /// entry that merely mentions a URL used to arrive with Telegram's own card underneath it, a
    /// second screenful that pushes the message being read off the top.
    /// </summary>
    [Fact]
    public async Task EveryTextSend_DisablesTheLinkPreview()
    {
        var transport = new RecordingTransport_Fake();
        transport.Answer_With(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":52}}""");

        await Build_Client(transport).Send_Message_Async(5, "see https://example.com", TelegramSendSounds.Rings, CancellationToken.None);

        var request = Assert.Single(transport.Requests);

        Assert.Contains("\"link_preview_options\"", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"is_disabled\":true", request.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE QUERY STRING THAT DECIDES WHETHER TAPS ARRIVE. <c>allowed_updates</c> is URL-encoded, so
    /// it is decoded and read as the JSON array it is rather than matched as an opaque blob — a
    /// test that pinned the encoded literal would pass just as happily on a wrongly-encoded one.
    /// </summary>
    [Fact]
    public async Task GetUpdates_AsksForMessagesAndTaps_WithTheOffsetAndTheLongPoll()
    {
        var transport = new RecordingTransport_Fake();
        transport.Answer_With(HttpStatusCode.OK, """{"ok":true,"result":[]}""");

        await Build_Client(transport).Get_UpdatesJson_Async(offset: 4242, timeoutSeconds: 20, CancellationToken.None);

        var request = Assert.Single(transport.Requests);
        var query = System.Web.HttpUtility.ParseQueryString(request.Uri.Query);

        Assert.EndsWith($"/bot{TOKEN}/getUpdates", request.Uri.AbsolutePath);
        Assert.Equal("4242", query["offset"]);
        Assert.Equal("20", query["timeout"]);

        var allowed = JsonNode.Parse(query["allowed_updates"]!)!.AsArray().Select(node => node!.GetValue<string>()).ToList();

        Assert.Contains("message", allowed);
        Assert.Contains("callback_query", allowed);
    }

    /// <summary>
    /// A 409 REACHES THE CALLER AS A 409 (brief B put a named situation behind this status: two
    /// pollers, or a webhook). It must arrive as a TelegramApiException carrying the code, not as
    /// a sentence someone has to parse.
    /// </summary>
    [Fact]
    public async Task GetUpdates_Answering409_SurfacesTheStatusOnTheException()
    {
        var transport = new RecordingTransport_Fake();
        transport.Answer_With((HttpStatusCode)409, """{"ok":false,"error_code":409,"description":"Conflict: terminated by other getUpdates request"}""");

        var failure = await Assert.ThrowsAsync<TelegramApiException>(
            () => Build_Client(transport).Get_UpdatesJson_Async(0, 20, CancellationToken.None));

        Assert.Equal(409, failure.StatusCode);
        Assert.False(failure.Is_Retryable, "a 409 is a named situation with an action attached, not a 'try again'");
    }

    /// <summary>
    /// THE POLL IS METERED NOW, and on the CONTROL bucket — it creates no message in the group, so
    /// charging it to the twenty-a-minute ceiling would spend the owner's delivery allowance on
    /// housekeeping. Asserted as "the send allowance is untouched", which is the property that
    /// matters and the one a future re-classification would break.
    /// </summary>
    // SLOW BY CONSTRUCTION: the control bucket starts empty by design, so each of these pays a
    // real ~2 s for its first token, and the retry case pays Telegram's retry_after on top.
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task GetUpdates_SpendsTheControlAllowance_NotTheOwnersDeliveryAllowance()
    {
        var budget = TelegramSendBudget_Factory.Create_Fresh();
        var transport = new RecordingTransport_Fake();
        transport.Answer_With(HttpStatusCode.OK, """{"ok":true,"result":[]}""");

        var client = TelegramApiClient_Factory.Create_WithTransport(TOKEN, CHAT_ID, budget, transport);

        await client.Get_UpdatesJson_Async(0, 20, CancellationToken.None);

        Assert.Equal(TokenBucket_Gate.DEFAULT_CAPACITY, budget.Read_SendState().Tokens, precision: 1);
        Assert.Single(transport.Requests);
    }

    /// <summary>
    /// A RATE-LIMITED POLL IS RETRIED, honouring Telegram's own number. It used to be the one call
    /// in this file that went straight to HttpClient — no bucket, no retry_after — so a 429 met the
    /// inbound loop's own backoff, which knows nothing about how long Telegram asked for.
    /// </summary>
    // SLOW BY CONSTRUCTION: the control bucket starts empty by design, so each of these pays a
    // real ~2 s for its first token, and the retry case pays Telegram's retry_after on top.
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AGetUpdatesThatIsRateLimited_IsRetried_AndTheSecondAttemptIsTheOneThatCounts()
    {
        var transport = new RecordingTransport_Fake();
        transport.Answer_With((HttpStatusCode)429, """{"ok":false,"error_code":429,"description":"Too Many Requests","parameters":{"retry_after":1}}""");
        transport.Then_Answer_With(HttpStatusCode.OK, """{"ok":true,"result":[{"update_id":5}]}""");

        var body = await Build_Client(transport).Get_UpdatesJson_Async(0, 20, CancellationToken.None);

        Assert.Contains("\"update_id\":5", body);
        Assert.Equal(2, transport.Requests.Count);
    }

    /// <summary>
    /// Past the CONTROL ceiling the failure goes to the caller — two seconds, not the message
    /// path's ten. A poll that slept for minutes would hold the inbound loop while the owner's
    /// taps queued behind it.
    /// </summary>
    // SLOW BY CONSTRUCTION: the control bucket starts empty by design, so each of these pays a
    // real ~2 s for its first token, and the retry case pays Telegram's retry_after on top.
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AGetUpdatesRateLimitedForTooLong_IsHandedBack_NotSleptThrough()
    {
        var transport = new RecordingTransport_Fake();
        transport.Answer_With((HttpStatusCode)429, """{"ok":false,"error_code":429,"description":"Too Many Requests","parameters":{"retry_after":600}}""");

        var failure = await Assert.ThrowsAsync<TelegramApiException>(
            () => Build_Client(transport).Get_UpdatesJson_Async(0, 20, CancellationToken.None));

        Assert.Equal(429, failure.StatusCode);
        Assert.Equal(600, failure.RetryAfterSeconds);
        Assert.Single(transport.Requests);
        Assert.True(TokenBucket_Gate.MAXIMUM_CONTROL_RETRY_WAIT < TimeSpan.FromSeconds(600));
    }

    /// <summary>
    /// A 429 ON A MESSAGE-CREATING CALL IS HONOURED WITH TELEGRAM'S OWN NUMBER and retried — and
    /// the retry is what the owner's message rides on, so it is worth pinning that it happens at
    /// all rather than trusting the branch.
    /// </summary>
    [Fact]
    public async Task ASendThatIsRateLimited_IsRetried_AndTheSecondAttemptIsTheOneThatCounts()
    {
        var transport = new RecordingTransport_Fake();
        transport.Answer_With((HttpStatusCode)429, """{"ok":false,"error_code":429,"description":"Too Many Requests","parameters":{"retry_after":1}}""");
        transport.Then_Answer_With(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":77}}""");

        var messageId = await Build_Client(transport).Send_Message_Async(5, "hello", TelegramSendSounds.Rings, CancellationToken.None);

        Assert.Equal(77, messageId);
        Assert.Equal(2, transport.Requests.Count);
        Assert.All(transport.Requests, request => Assert.EndsWith("/sendMessage", request.Uri.AbsolutePath));
    }

    /// <summary>
    /// PAST THE INLINE CAP THE FAILURE GOES TO THE CALLER, rather than the client sleeping through
    /// a wait that would hold a mirror tick open for minutes.
    /// </summary>
    [Fact]
    public async Task ASendRateLimitedForTooLong_IsHandedBack_NotSleptThrough()
    {
        var transport = new RecordingTransport_Fake();
        transport.Answer_With((HttpStatusCode)429, """{"ok":false,"error_code":429,"description":"Too Many Requests","parameters":{"retry_after":600}}""");

        var failure = await Assert.ThrowsAsync<TelegramApiException>(
            () => Build_Client(transport).Send_Message_Async(5, "hello", TelegramSendSounds.Rings, CancellationToken.None));

        Assert.Equal(429, failure.StatusCode);
        Assert.Equal(600, failure.RetryAfterSeconds);
        Assert.Single(transport.Requests);
    }

    /// <summary>
    /// A 429 ON A CONTROL CALL, WITHIN ITS OWN (FAR SHORTER) CEILING, IS RETRIED INLINE — the same
    /// shape as <see cref="ASendThatIsRateLimited_IsRetried_AndTheSecondAttemptIsTheOneThatCounts"/>,
    /// pinned separately because every <c>editMessageText</c> / <c>answerCallbackQuery</c> /
    /// <c>editForumTopic</c> call shares the CONTROL bucket's much tighter
    /// <see cref="TokenBucket_Gate.MAXIMUM_CONTROL_RETRY_WAIT"/> (2 s, against the message path's
    /// 10 s) — a ceiling this file had never exercised for a Control call before this test, and the
    /// one every <c>editMessageText</c> caller actually runs under.
    /// </summary>
    // SLOW BY CONSTRUCTION: the control bucket starts empty by design (TelegramSendBudgetModel), so
    // the first control call of any test pays a real ~2 s for its first token before the scripted
    // 429 is even reached, plus the ~1 s retry_after this test honours.
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AControlCallThatIsRateLimitedWithinItsCeiling_IsRetried_AndTheSecondAttemptIsTheOneThatCounts()
    {
        var transport = new RecordingTransport_Fake();
        transport.Answer_With((HttpStatusCode)429, """{"ok":false,"error_code":429,"description":"Too Many Requests","parameters":{"retry_after":1}}""");
        transport.Then_Answer_With(HttpStatusCode.OK, """{"ok":true,"result":true}""");

        // A DIFFERENT message id from every other test in this file (brief note in the task): the
        // per-message edit gate holds a 30 s minimum gap between two edits of the SAME id, and only
        // the FIRST edit of a fresh id is free of it.
        await Build_Client(transport).Edit_MessageText_Async(9001, "status line", CancellationToken.None);

        Assert.Equal(2, transport.Requests.Count);
        Assert.All(transport.Requests, request => Assert.EndsWith("/editMessageText", request.Uri.AbsolutePath));
    }

    /// <summary>
    /// PAST THE CONTROL CEILING THE FAILURE IS HANDED BACK, NOT SLEPT THROUGH — this is the
    /// behaviour the 2026-09-10 incident turns on. Production measured <c>retry_after</c> 20, 22 and
    /// 32 seconds on a once-a-minute PULSE edit while the control bucket sat nearly full (see
    /// <see cref="TokenBucket_Gate.MINIMUM_GAP_BETWEEN_EDITS_OF_ONE_MESSAGE"/>); 24 sits inside that
    /// observed range and, against a 2 s ceiling, is twelve times over it. Before this test the only
    /// pin for "handed back past the ceiling" was on the MESSAGE path
    /// (<see cref="ASendRateLimitedForTooLong_IsHandedBack_NotSleptThrough"/>, 600 s against a 10 s
    /// ceiling) — the Control path's own, much tighter ceiling had never been pinned at all, and it
    /// is the one every <c>editMessageText</c> / <c>answerCallbackQuery</c> / <c>editForumTopic</c>
    /// call actually runs under.
    /// </summary>
    // SLOW BY CONSTRUCTION: the control bucket starts empty by design, so this still pays the real
    // ~2 s for its first token even though the scripted 429 is never retried.
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AControlCallRateLimitedPastItsCeiling_IsHandedBack_NotSleptThrough()
    {
        var transport = new RecordingTransport_Fake();
        transport.Answer_With((HttpStatusCode)429, """{"ok":false,"error_code":429,"description":"Too Many Requests","parameters":{"retry_after":24}}""");

        var failure = await Assert.ThrowsAsync<TelegramApiException>(
            () => Build_Client(transport).Edit_MessageText_Async(9002, "status line", CancellationToken.None));

        Assert.Equal(429, failure.StatusCode);
        Assert.Equal(24, failure.RetryAfterSeconds);
        Assert.Single(transport.Requests);
        Assert.True(TokenBucket_Gate.MAXIMUM_CONTROL_RETRY_WAIT < TimeSpan.FromSeconds(24));
    }

    /// <summary>
    /// THE MULTIPART SHAPE. A document upload builds its own request and therefore does not pass
    /// through the JSON path at all — the caption's parse mode and the chat/thread fields are
    /// composed separately here, which is exactly how the two paths drift apart.
    /// </summary>
    [Fact]
    public async Task ADocumentUpload_CarriesTheChatTheThreadTheCaptionAndTheBytes()
    {
        var transport = new RecordingTransport_Fake();
        transport.Answer_With(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":9}}""");

        await Build_Client(transport).Send_Document_Async(7, "report.md", "hello file"u8.ToArray(), "<b>caption</b>", TelegramSendSounds.Rings, CancellationToken.None);

        var request = Assert.Single(transport.Requests);

        Assert.EndsWith("/sendDocument", request.Uri.AbsolutePath);
        Assert.Contains("multipart/form-data", request.ContentType);
        Assert_HasPart(request.Body, "chat_id");
        Assert_HasPart(request.Body, "message_thread_id");
        Assert_HasPart(request.Body, "caption");
        Assert_HasPart(request.Body, "parse_mode");
        Assert_HasPart(request.Body, "document");

        Assert.Contains(CHAT_ID.ToString(), request.Body);
        Assert.Contains("<b>caption</b>", request.Body);
        Assert.Contains("HTML", request.Body);
        Assert.Contains("report.md", request.Body);
        Assert.Contains("hello file", request.Body);
    }

    /// <summary>
    /// A DOCUMENT over Telegram's cap never reaches the wire at all (brief F7).
    ///
    /// <para>
    /// The refusal is pinned by its MESSAGE and by NOT being a <see cref="TelegramApiException"/>,
    /// not merely by "something threw". <c>ThrowsAnyAsync&lt;Exception&gt;</c> plus an empty request
    /// list is satisfied by an OutOfMemoryException from the allocation below, or by any future
    /// NullReferenceException on the way to the wire — two routes to the same green, which is what
    /// CLAUDE.md decision 20 forbids. The type matters too: the client throws a plain Exception
    /// here on purpose, because Telegram did not answer this — we did — and manufacturing a status
    /// code would put a fiction in front of every caller that classifies by status.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AnOversizedDocument_IsRefusedBeforeAnythingIsSent()
    {
        var transport = new RecordingTransport_Fake();
        transport.Answer_With(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":9}}""");

        var refusal = await Assert.ThrowsAnyAsync<Exception>(
            () => Build_Client(transport).Send_Document_Async(7, "huge.bin", new byte[TelegramFileCaps.MAX_DOCUMENT_BYTES + 1], "c", TelegramSendSounds.Rings, CancellationToken.None));

        Assert.Contains("sendDocument", refusal.Message);
        Assert.Contains($"{TelegramFileCaps.In_Megabytes(TelegramFileCaps.MAX_DOCUMENT_BYTES)} MB cap", refusal.Message);
        Assert.IsNotType<TelegramApiException>(refusal);
        Assert.Empty(transport.Requests);
    }

    /// <summary>
    /// The startup handshake brief B added: the bot's own name, and the webhook clear that must NOT
    /// drop what the owner sent while the webhook was in the way.
    /// </summary>
    [Fact]
    public async Task TheStartupHandshake_AsksWhoWeAre_AndClearsAWebhookWithoutDroppingUpdates()
    {
        var transport = new RecordingTransport_Fake();
        transport.Answer_With(HttpStatusCode.OK, """{"ok":true,"result":{"username":"vibe_bridge_bot"}}""");
        transport.Then_Answer_With(HttpStatusCode.OK, """{"ok":true,"result":true}""");

        var client = Build_Client(transport);

        Assert.Equal("vibe_bridge_bot", await client.Get_BotUsername_Async(CancellationToken.None));

        await client.Delete_Webhook_Async(dropPendingUpdates: false, CancellationToken.None);

        Assert.Equal(2, transport.Requests.Count);
        Assert.EndsWith("/getMe", transport.Requests[0].Uri.AbsolutePath);
        Assert.EndsWith("/deleteWebhook", transport.Requests[1].Uri.AbsolutePath);
        Assert.Contains("false", transport.Requests[1].Body);
    }

    /// <summary>
    /// One multipart part, by name. Quoted and unquoted both accepted: .NET writes
    /// <c>name=chat_id</c> without quotes when the name needs none, and pinning the quoted form
    /// would make this test a test of the framework's formatting rather than of our payload.
    /// </summary>
    static void Assert_HasPart(string body, string name)
    {
        Assert.True(
            body.Contains($"name=\"{name}\"", StringComparison.Ordinal) || body.Contains($"name={name}", StringComparison.Ordinal),
            $"the multipart body carries no '{name}' part:{Environment.NewLine}{body}");
    }

    static ITelegramApiClient Build_Client(RecordingTransport_Fake transport)
    {
        return TelegramApiClient_Factory.Create_WithTransport(
            TOKEN, CHAT_ID, TelegramSendBudget_Factory.Create_Fresh(), transport);
    }
}

/// <summary>
/// Records every request and answers from a scripted queue, one answer per request, IN ORDER. An
/// unscripted request throws rather than receiving a default success: a fake that invents an
/// answer lets a test pass for a reason the test does not state.
/// </summary>
internal sealed class RecordingTransport_Fake : HttpMessageHandler
{
    readonly Lock _lock = new();
    readonly List<(Uri Uri, string Body, string ContentType)> _requests = [];
    readonly Queue<(HttpStatusCode Status, string Body)> _answers = new();

    public IReadOnlyList<(Uri Uri, string Body, string ContentType)> Requests
    {
        get { lock (_lock) return [.. _requests]; }
    }

    public void Answer_With(HttpStatusCode status, string body)
    {
        lock (_lock)
            _answers.Enqueue((status, body));
    }

    public void Then_Answer_With(HttpStatusCode status, string body) => Answer_With(status, body);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var contentType = request.Content?.Headers.ContentType?.ToString() ?? "";

        (HttpStatusCode Status, string Body) answer;

        lock (_lock)
        {
            _requests.Add((request.RequestUri!, body, contentType));

            // AN UNSCRIPTED REQUEST IS A FAILURE, not a free OK. An earlier version repeated the
            // last answer for ever, so a test whose subject quietly made one call more than it
            // scripted went green on a fabricated success — the test passing for a reason that has
            // nothing to do with what it claims.
            if (_answers.Count == 0)
                throw new InvalidOperationException($"the fake transport was asked for {request.RequestUri} with no answer scripted for it");

            answer = _answers.Dequeue();
        }

        return new HttpResponseMessage(answer.Status) { Content = new StringContent(answer.Body) };
    }
}
