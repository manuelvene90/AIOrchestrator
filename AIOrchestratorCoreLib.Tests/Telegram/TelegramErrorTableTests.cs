using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// THE TABLE, PINNED AGAINST TELEGRAM'S OWN WORDINGS — brief F10.
///
/// <para>
/// PROVENANCE, stated rather than implied: the bodies below are Telegram's documented and
/// widely-observed <c>description</c> strings, wrapped in the EXACT sentence
/// <c>TelegramApiClientModel.Post_Async</c> builds around them — which is the string every caller
/// of these predicates actually receives. They were NOT harvested from the production VPS logs
/// (this branch read nothing there). Anyone who does harvest a body that this table misreads should
/// add it here as a case; that is what the file is for.
/// </para>
/// <para>
/// The property that matters is that four different meanings share HTTP 400, and the bridge does
/// four different things with them. A table that collapsed any two would be invisible to a status
/// check and visible only as a topic that never goes away, a status line that duplicates itself, or
/// a name sync that retries for ever.
/// </para>
/// </summary>
public class TelegramErrorTableTests
{
    [Theory]
    [InlineData(400, "Bad Request: message is not modified: specified new message content and reply markup are exactly the same as a current content and reply markup of the message", TelegramErrorCases.AlreadyCurrent)]
    [InlineData(400, "Bad Request: TOPIC_NOT_MODIFIED", TelegramErrorCases.TopicNameAlreadyCurrent)]
    [InlineData(400, "Bad Request: message to edit not found", TelegramErrorCases.MessageGone)]
    [InlineData(400, "Bad Request: message to delete not found", TelegramErrorCases.MessageGone)]
    [InlineData(400, "Bad Request: MESSAGE_ID_INVALID", TelegramErrorCases.MessageGone)]
    [InlineData(400, "Bad Request: message thread not found", TelegramErrorCases.TopicGone)]
    [InlineData(400, "Bad Request: TOPIC_DELETED", TelegramErrorCases.TopicGone)]
    [InlineData(400, "Bad Request: TOPIC_ID_INVALID", TelegramErrorCases.TopicGone)]
    [InlineData(400, "Bad Request: message can't be deleted for everyone", TelegramErrorCases.DeleteRefused)]
    [InlineData(400, "Bad Request: not enough rights to manage pinned messages", TelegramErrorCases.NotEnoughRights)]
    [InlineData(400, "Bad Request: CHAT_ADMIN_REQUIRED", TelegramErrorCases.NotEnoughRights)]
    [InlineData(403, "Forbidden: bot is not a member of the supergroup chat", TelegramErrorCases.Refused)]
    [InlineData(400, "Bad Request: can't parse entities: Unsupported start tag \"code\" at byte offset 12", TelegramErrorCases.Refused)]
    [InlineData(429, "Too Many Requests: retry after 33", TelegramErrorCases.Retryable)]
    [InlineData(500, "Internal Server Error", TelegramErrorCases.Retryable)]
    [InlineData(502, "Bad Gateway", TelegramErrorCases.Retryable)]
    public void ArealTelegramBody_IsClassifiedAsTheCaseTheBridgeMeans(int errorCode, string description, TelegramErrorCases expected)
    {
        var body = $"{{\"ok\":false,\"error_code\":{errorCode},\"description\":\"{description.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"}}";
        var thrownMessage = $"Telegram 'editMessageText' failed with HTTP {errorCode}: {body}";

        Assert.Equal(expected, TelegramError_Table.Classify(errorCode, thrownMessage));
        Assert.Equal(expected, TelegramError_Table.Classify(new TelegramApiException(errorCode, thrownMessage)));
    }

    /// <summary>
    /// THE FOUR MEANINGS THAT SHARE A STATUS, asserted as a set rather than one at a time. A change
    /// that made any two of them collapse would still pass every single case above if the cases
    /// were read one by one and the wrong one happened to win.
    /// </summary>
    [Fact]
    public void TheFourHundredsAreFourDifferentCases_NotOne()
    {
        var cases = new[]
        {
            TelegramError_Table.Classify(400, "Bad Request: TOPIC_NOT_MODIFIED"),
            TelegramError_Table.Classify(400, "Bad Request: message thread not found"),
            TelegramError_Table.Classify(400, "Bad Request: message can't be deleted for everyone"),
            TelegramError_Table.Classify(400, "Bad Request: chat_id is empty"),
        };

        Assert.Equal(4, cases.Distinct().Count());
    }

    /// <summary>
    /// A 429 whose description happens to name a specific case is that CASE, not merely retryable —
    /// asked in this order because every specific case is a 400 and asking the status first would
    /// swallow all of them. This pins the order itself.
    /// </summary>
    [Fact]
    public void ASpecificDescription_BeatsARetryableStatus()
    {
        Assert.Equal(TelegramErrorCases.MessageGone, TelegramError_Table.Classify(429, "Bad Request: message to edit not found"));
    }

    [Fact]
    public void AFailureTelegramNeverAnswered_IsNoAnswer_NotRefused()
    {
        Assert.Equal(TelegramErrorCases.NoAnswer, TelegramError_Table.Classify(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 90 seconds elapsing.")));
        Assert.Equal(TelegramErrorCases.NoAnswer, TelegramError_Table.Classify(new HttpRequestException("Connection reset by peer")));
        Assert.Equal(TelegramErrorCases.NoAnswer, TelegramError_Table.Classify(new Exception("Telegram client vanished")));
    }

    /// <summary>
    /// THE PREDICATES THE CALLERS STILL USE MUST NOT HAVE MOVED. This is the whole risk of a
    /// consolidation: the rule is now in one place, and if the forwarders answer differently from
    /// the code they replaced, three unrelated features change behaviour at once.
    /// </summary>
    [Theory]
    [InlineData("Bad Request: message is not modified: specified new message content is exactly the same", true, false, false)]
    [InlineData("Bad Request: message to edit not found", false, true, false)]
    [InlineData("Bad Request: message to delete not found", false, true, false)]
    [InlineData("Bad Request: MESSAGE_ID_INVALID", false, true, false)]
    [InlineData("Bad Request: message can't be deleted for everyone", false, false, true)]
    [InlineData("Bad Request: message can not be deleted", false, false, true)]
    [InlineData("Bad Request: message cannot be deleted", false, false, true)]
    [InlineData("Bad Request: not enough rights to manage pinned messages", false, false, true)]
    [InlineData("Bad Request: CHAT_ADMIN_REQUIRED", false, false, true)]
    [InlineData("Bad Request: TOPIC_NOT_MODIFIED", false, false, false)]
    [InlineData("Bad Request: chat not found", false, false, false)]
    public void TheOldPredicatesAnswerExactlyWhatTheyAlwaysDid(string description, bool alreadyCurrent, bool messageGone, bool deleteRefused)
    {
        Assert.Equal(alreadyCurrent, TopicStatusLine_Decider.Is_MessageAlreadyCurrent(description));
        Assert.Equal(messageGone, TopicStatusLine_Decider.Is_MessageGone(description));
        Assert.Equal(deleteRefused, TopicStatusLine_Decider.Is_DeleteRefused(description));
    }

    /// <summary>
    /// "message can't be edited" is DELIBERATELY not MessageGone — the message exists and is not
    /// editable, so clearing the id would post a second status line beside the frozen one. The
    /// original predicate's summary argues this at length; the consolidation must not lose it.
    /// </summary>
    [Fact]
    public void AMessageThatCannotBeEdited_IsNotAMessageThatIsGone()
    {
        Assert.False(TopicStatusLine_Decider.Is_MessageGone("Bad Request: message can't be edited"));
        Assert.NotEqual(TelegramErrorCases.MessageGone, TelegramError_Table.Classify(400, "Bad Request: message can't be edited"));
    }

    /// <summary>Telegram is not consistent about casing: upper-case slugs, lower-case prose, both seen inverted.</summary>
    [Fact]
    public void CasingDoesNotChangeTheAnswer()
    {
        Assert.Equal(TelegramErrorCases.TopicNameAlreadyCurrent, TelegramError_Table.Classify(400, "bad request: topic_not_modified"));
        Assert.Equal(TelegramErrorCases.AlreadyCurrent, TelegramError_Table.Classify(400, "Bad Request: MESSAGE IS NOT MODIFIED"));
    }
}
