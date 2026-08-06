namespace AIOrchestratorCoreLib.Channels.ChannelEntry;

/// <summary>One append-only channel entry: '## [n] FROM author — date — subject' plus its body.</summary>
public interface IChannelEntry
{
    int Index { get; }
    ChannelAuthors Author { get; }
    string DateText { get; }
    string Subject { get; }
    string Body { get; }
    string RawText { get; }
}
