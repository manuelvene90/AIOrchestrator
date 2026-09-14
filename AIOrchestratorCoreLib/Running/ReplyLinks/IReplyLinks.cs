namespace AIOrchestratorCoreLib.Running.ReplyLinks;

/// <summary>
/// WHICH OWNER MESSAGE A SESSION'S ENTRY ANSWERS, so the phone can show it as a Telegram reply.
///
/// <para>
/// The owner, 2026-09-11, after two exported chats: answers were not linked to questions. In
/// fincanva-5 a supervisor reply landed after the owner's next message and read as the answer to it,
/// and nothing on the phone said otherwise. Their words: <i>"non c'è modo di linkare domande e
/// risposte? Tipo con un rispondi?"</i> Two facts make the link, and they are known in two places:
/// the bridge knows which Telegram message became which owner entry (it wrote the entry), and the
/// turn dispatcher knows which owner entry a turn was answering (it handed it over). This is where
/// they meet — owned by the dispatcher, read by the bridge through it.
/// </para>
/// <para>
/// IN MEMORY, AND THAT IS THE HONEST SIZE OF IT. After a restart an entry simply goes out unthreaded,
/// which is how every entry went out before this existed: the link is lost, never the message.
/// Keyed by channel file and entry index — an index the app allocated under the channel lock, so it
/// is not the agent-written kind decision 12 warns about.
/// </para>
/// </summary>
public interface IReplyLinks
{
    /// <summary>The bridge wrote owner entry <paramref name="entryIndex"/> from Telegram message <paramref name="telegramMessageId"/>.</summary>
    void Record_OwnerMessage(string channelFilePath, int entryIndex, long telegramMessageId);

    /// <summary>The dispatcher wrote entry <paramref name="answerEntryIndex"/> as a turn's answer to owner entry <paramref name="answeredOwnerEntryIndex"/>.</summary>
    void Record_Answer(string channelFilePath, int answerEntryIndex, int answeredOwnerEntryIndex);

    /// <summary>The Telegram message entry <paramref name="entryIndex"/> answers, or null when nothing links it.</summary>
    long? Find_AnsweredTelegramMessage_OrNull(string channelFilePath, int entryIndex);
}
