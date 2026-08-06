namespace AIOrchestratorCoreLib.Channels.ChannelEntry;

internal sealed class ChannelEntryModel(
    int index,
    ChannelAuthors author,
    string dateText,
    string subject,
    string body,
    string rawText) : IChannelEntry
{
    public int Index { get; } = index;
    public ChannelAuthors Author { get; } = author;
    public string DateText { get; } = dateText;
    public string Subject { get; } = subject;
    public string Body { get; } = body;
    public string RawText { get; } = rawText;
}
