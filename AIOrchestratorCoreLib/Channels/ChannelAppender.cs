namespace AIOrchestratorCoreLib.Channels;

/// <summary>
/// Appends owner entries (arriving from Telegram) to a channel file, continuing its numbering.
/// Channels are append-only by protocol, and this is the only place the bridge APPENDS to one.
/// <para>
/// It is not the only place the bridge WRITES one: <see cref="Channel_Compactor"/> rewrites the
/// live file whole when it archives an old tail. This comment used to claim otherwise, which made
/// the rewrite-versus-append race invisible to anyone reading here first. Both writers take
/// <see cref="ChannelWrite_Lock"/>, so they cannot overlap with each other or with a session that
/// appends through <c>kit/channel-append.sh</c>.
/// </para>
/// <para>
/// Both methods return WHETHER THEY WROTE. False means the channel was locked by another writer
/// for the whole budget and the entry was not appended — it is not a detail to discard. Today most
/// call sites in the bridge ignore it, which is a known gap recorded with this change rather than
/// papered over: the entry is dropped and nothing says so. The owner-delivery path does check it,
/// because an owner message has already left its buffer by then and is otherwise lost outright.
/// </para>
/// </summary>
public static class ChannelAppender
{
    /// <summary>Returns whether the entry was appended; false means the channel stayed locked.</summary>
    public static bool Append_OwnerEntry(string channelFilePath, string messageText, DateTime nowLocal)
    {
        return Append_Entry(channelFilePath, "owner", "via Telegram", messageText, nowLocal);
    }

    /// <summary>
    /// App-authored entries: request confirmations/failures on the general channel. Returns whether
    /// the entry was appended.
    /// </summary>
    public static bool Append_AppEntry(string channelFilePath, string subject, string body, DateTime nowLocal)
    {
        return Append_Entry(channelFilePath, "app", subject, body, nowLocal);
    }

    static bool Append_Entry(string channelFilePath, string authorWord, string subject, string body, DateTime nowLocal)
    {
        // The index comes from a read, so the read and the append have to be one indivisible step:
        // split them and two appenders pick the same index.
        return ChannelWrite_Lock.Try_Run_Serialised(channelFilePath, ChannelWrite_Lock.DEFAULT_BUDGET, () =>
        {
            var existingText = File.Exists(channelFilePath)
                ? File.ReadAllText(channelFilePath)
                : string.Empty;

            var nextIndex = ChannelEntry_Parser.Get_NextIndex(existingText);

            // The leading "\n" is the whole requirement, and it is not cosmetic: the parser matches
            // its header regex per line with no lookback, so an entry is read iff its header BEGINS
            // A LINE. Starting the append with a newline guarantees that whether or not the file
            // ended in one. There is no blank-line rule — that was believed briefly on 2026-08-13
            // and disproved by reading the parser.
            var entry =
                $"\n## [{nextIndex}] FROM {authorWord} — {nowLocal:yyyy-MM-dd HH:mm} — {subject}\n" +
                $"\n{body.Trim()}\n";

            File.AppendAllText(channelFilePath, entry);
        }, out _);
    }
}
