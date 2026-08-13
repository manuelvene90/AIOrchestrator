namespace AIOrchestratorCoreLib.Channels;

/// <summary>
/// Appends owner entries (arriving from Telegram) to a channel file, continuing its numbering.
/// Channels are append-only by protocol, and this is the only place the bridge APPENDS to one.
/// <para>
/// It is not the only place the bridge WRITES one: <see cref="Channel_Compactor"/> rewrites the
/// live file whole when it archives an old tail. This comment used to claim otherwise, which made
/// the rewrite-versus-append race invisible to anyone reading here first. Both writers take
/// <see cref="ChannelWrite_Lock"/> so they cannot overlap — within this process.
/// </para>
/// </summary>
public static class ChannelAppender
{
    public static void Append_OwnerEntry(string channelFilePath, string messageText, DateTime nowLocal)
    {
        Append_Entry(channelFilePath, "owner", "via Telegram", messageText, nowLocal);
    }

    /// <summary>App-authored entries: request confirmations/failures on the general channel.</summary>
    public static void Append_AppEntry(string channelFilePath, string subject, string body, DateTime nowLocal)
    {
        Append_Entry(channelFilePath, "app", subject, body, nowLocal);
    }

    static void Append_Entry(string channelFilePath, string authorWord, string subject, string body, DateTime nowLocal)
    {
        // The index comes from a read, so the read and the append have to be one indivisible step:
        // split them and two appenders pick the same index. The gate covers this process only —
        // see ChannelWrite_Lock for what remains open.
        ChannelWrite_Lock.Run_Serialised(channelFilePath, () =>
        {
            var existingText = File.Exists(channelFilePath)
                ? File.ReadAllText(channelFilePath)
                : string.Empty;

            var nextIndex = ChannelEntry_Parser.Get_NextIndex(existingText);

            var entry =
                $"\n## [{nextIndex}] FROM {authorWord} — {nowLocal:yyyy-MM-dd HH:mm} — {subject}\n" +
                $"\n{body.Trim()}\n";

            File.AppendAllText(channelFilePath, entry);
        });
    }
}
