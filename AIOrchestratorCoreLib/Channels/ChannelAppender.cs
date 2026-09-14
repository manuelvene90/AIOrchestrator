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
        return Append_OwnerEntry_OrNull(channelFilePath, messageText, nowLocal) != null;
    }

    /// <summary>
    /// The same append, returning the index it allocated — null when the channel stayed locked. For a
    /// caller that has to name the entry afterwards (Running.ReplyLinks), which a re-read cannot do
    /// safely: another writer may have appended in between.
    /// </summary>
    public static int? Append_OwnerEntry_OrNull(string channelFilePath, string messageText, DateTime nowLocal)
    {
        return Append_Entry(channelFilePath, "owner", "via Telegram", messageText, nowLocal);
    }

    /// <summary>
    /// App-authored entries: request confirmations/failures, nudges, alerts. Returns whether the
    /// entry was appended.
    ///
    /// <paramref name="audience"/> is REQUIRED and has no default on purpose — see
    /// <see cref="AppEntryAudiences"/>. It decides whether the entry also reaches the owner's phone,
    /// and it is written into the subject because the mirror re-reads entries from the file and cannot
    /// see this call.
    /// </summary>
    public static bool Append_AppEntry(string channelFilePath, AppEntryAudiences audience, string subject, string body, DateTime nowLocal)
    {
        return Append_Entry(channelFilePath, "app", AppEntryAudience_Tag.Apply(subject, audience), body, nowLocal) != null;
    }

    /// <summary>
    /// An entry written ON BEHALF OF a session — the print runner appending a member's final
    /// message under the member's own author word. The session never touches the file: the bridge
    /// writes the header, the index and the time, which is what makes CLAUDE.md decision 12 (agent-
    /// written headers are untrusted) moot for print-run sessions. Only session authors are
    /// accepted; the owner and the app have their own methods above.
    /// </summary>
    public static bool Append_SessionEntry(string channelFilePath, ChannelAuthors author, string subject, string body, DateTime nowLocal)
    {
        return Append_SessionEntry_OrNull(channelFilePath, author, subject, body, nowLocal) != null;
    }

    /// <summary>The same append, returning the index it allocated — null when the channel stayed locked.</summary>
    public static int? Append_SessionEntry_OrNull(string channelFilePath, ChannelAuthors author, string subject, string body, DateTime nowLocal)
    {
        if (!ChannelAuthor_Kinds.Is_Session(author))
            throw new ArgumentException($"Append_SessionEntry is for session authors, not {author} (subject '{subject}')");

        return Append_Entry(channelFilePath, ChannelAuthor_Words.Get_Word(author), subject, body, nowLocal);
    }

    /// <summary>The index the entry was written under, or null when the channel stayed locked for the whole budget.</summary>
    static int? Append_Entry(string channelFilePath, string authorWord, string subject, string body, DateTime nowLocal)
    {
        int? writtenIndex = null;

        // The index comes from a read, so the read and the append have to be one indivisible step:
        // split them and two appenders pick the same index.
        var landed = ChannelWrite_Lock.Try_Run_Serialised(channelFilePath, ChannelWrite_Lock.DEFAULT_BUDGET, () =>
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
            writtenIndex = nextIndex;
        }, out _);

        return landed ? writtenIndex : null;
    }
}
