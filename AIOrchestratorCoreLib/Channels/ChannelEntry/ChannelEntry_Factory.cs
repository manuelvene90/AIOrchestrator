namespace AIOrchestratorCoreLib.Channels.ChannelEntry;

public static class ChannelEntry_Factory
{
    public static IChannelEntry Create(
        int index,
        ChannelAuthors author,
        string dateText,
        string subject,
        string body,
        string rawText)
    {
        if (index < 1)
            throw new ArgumentException($"Channel entry index must be >= 1, got {index} (subject '{subject}')");

        return new ChannelEntryModel(index, author, dateText, subject, body, rawText);
    }
}
