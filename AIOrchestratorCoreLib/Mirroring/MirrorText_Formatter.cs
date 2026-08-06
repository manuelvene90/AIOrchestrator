using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Channels.DiscoveredChannel;

namespace AIOrchestratorCoreLib.Mirroring;

/// <summary>
/// Formats a channel entry for the Telegram mirror. The unified group view lives ONLY here:
/// every spoke's traffic lands in one topic, direction-tagged.
/// </summary>
public static class MirrorText_Formatter
{
    public static string Format(IDiscoveredChannel channel, IChannelEntry entry)
    {
        var tag = Build_DirectionTag(channel, entry.Author);
        var header = $"{tag} #{entry.Index} — {entry.Subject}";

        if (string.IsNullOrWhiteSpace(entry.Body))
            return header;

        return $"{header}\n\n{entry.Body}";
    }

    public static bool Should_Mirror(IDiscoveredChannel channel, IChannelEntry entry)
    {
        // Owner entries came FROM Telegram (or from the owner's own terminal typing) — echoing
        // them back would duplicate what the owner already sees/wrote.
        if (channel.IsOwnerChannel && entry.Author == ChannelAuthors.Owner)
            return false;

        return true;
    }

    static string Build_DirectionTag(IDiscoveredChannel channel, ChannelAuthors author)
    {
        if (channel.IsOwnerChannel)
        {
            return author switch
            {
                ChannelAuthors.Supervisor => "🔴 [sup → owner]",
                ChannelAuthors.Owner => "[owner → sup]",
                ChannelAuthors.App => "⚙ [app → owner]",
                ChannelAuthors.Implementer => $"⚠ [implementer?! → owner] (unexpected author on the owner channel)",
                ChannelAuthors.Unknown => "[? → owner]",
                _ => throw new Exception($"Unhandled ChannelAuthors: {author}"),
            };
        }

        return author switch
        {
            ChannelAuthors.Supervisor => $"🔴 [sup → {channel.SpokeName}]",
            ChannelAuthors.Implementer => $"🔵 [{channel.SpokeName} → sup]",
            ChannelAuthors.App => $"⚙ [app on {channel.SpokeName}]",
            ChannelAuthors.Owner => $"⚠ [owner?! → {channel.SpokeName}] (unexpected author on an implementer channel)",
            ChannelAuthors.Unknown => $"[? on {channel.SpokeName}]",
            _ => throw new Exception($"Unhandled ChannelAuthors: {author}"),
        };
    }
}
