namespace AIOrchestratorCoreLib.Running.ReplyLinks;

internal sealed class ReplyLinksModel : IReplyLinks
{
    /// <summary>
    /// Per channel and per kind. An owner message is answered within minutes, so the last few hundred
    /// cover any turn still in flight; older ones are dropped oldest-first, never the whole table.
    /// </summary>
    const int MAXIMUM_PER_CHANNEL = 256;

    readonly Lock _lock = new();
    readonly Dictionary<string, Dictionary<int, long>> _telegramMessageByOwnerEntry = [];
    readonly Dictionary<string, Dictionary<int, int>> _ownerEntryByAnswer = [];

    public void Record_OwnerMessage(string channelFilePath, int entryIndex, long telegramMessageId)
    {
        lock (_lock)
            Put(_telegramMessageByOwnerEntry, Key(channelFilePath), entryIndex, telegramMessageId);
    }

    public void Record_Answer(string channelFilePath, int answerEntryIndex, int answeredOwnerEntryIndex)
    {
        lock (_lock)
            Put(_ownerEntryByAnswer, Key(channelFilePath), answerEntryIndex, answeredOwnerEntryIndex);
    }

    public long? Find_AnsweredTelegramMessage_OrNull(string channelFilePath, int entryIndex)
    {
        var key = Key(channelFilePath);

        lock (_lock)
        {
            if (!_ownerEntryByAnswer.TryGetValue(key, out var answers) || !answers.TryGetValue(entryIndex, out var ownerEntry))
                return null;

            if (!_telegramMessageByOwnerEntry.TryGetValue(key, out var messages) || !messages.TryGetValue(ownerEntry, out var messageId))
                return null;

            return messageId;
        }
    }

    /// <summary>
    /// ONE SPELLING PER FILE. The dispatcher reads the path from session state and the bridge from its
    /// own channel discovery; two spellings of one file would be two tables and a link never found.
    /// </summary>
    static string Key(string channelFilePath)
    {
        return Path.GetFullPath(channelFilePath);
    }

    static void Put<TValue>(Dictionary<string, Dictionary<int, TValue>> table, string key, int index, TValue value)
    {
        if (!table.TryGetValue(key, out var perChannel))
            table[key] = perChannel = [];

        perChannel[index] = value;

        if (perChannel.Count > MAXIMUM_PER_CHANNEL)
            perChannel.Remove(perChannel.Keys.Min());
    }
}
