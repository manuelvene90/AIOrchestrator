namespace AIOrchestratorCoreLib.Channels.ChannelEntry;

public static class ChannelEntry_Factory
{
    public static IChannelEntry Create(
        int index,
        ChannelAuthors author,
        string dateText,
        string subject,
        string body,
        string rawText,

        // TRAILING AND OPTIONAL, because every existing caller predates typed entries and means
        // "untyped" — which is the transition the brief asks for, not a defect. A required parameter
        // here would have forced sixty call sites to pass null, and one of them would have passed
        // something else.
        string? type = null)
    {
        if (index < 1)
            throw new ArgumentException($"Channel entry index must be >= 1, got {index} (subject '{subject}')");

        return new ChannelEntryModel(index, author, dateText, subject, body, rawText, type);
    }
}
