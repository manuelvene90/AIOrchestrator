namespace AIOrchestratorCoreLib.Channels;

/// <summary>
/// Appends owner entries (arriving from Telegram) to a channel file, continuing its numbering.
/// Channels are append-only by protocol; this is the ONLY write the bridge ever performs on one.
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
        var existingText = File.Exists(channelFilePath)
            ? File.ReadAllText(channelFilePath)
            : string.Empty;

        var nextIndex = ChannelEntry_Parser.Get_NextIndex(existingText);

        var entry =
            $"\n## [{nextIndex}] FROM {authorWord} — {nowLocal:yyyy-MM-dd HH:mm} — {subject}\n" +
            $"\n{body.Trim()}\n";

        File.AppendAllText(channelFilePath, entry);
    }
}
