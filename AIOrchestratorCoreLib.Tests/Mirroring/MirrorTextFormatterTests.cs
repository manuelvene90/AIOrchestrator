using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Channels.DiscoveredChannel;
using AIOrchestratorCoreLib.Mirroring;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Mirroring;

public class MirrorTextFormatterTests
{
    static IChannelEntry Build_Entry(ChannelAuthors author, string subject, string body)
    {
        return ChannelEntry_Factory.Create(1, author, "2026-08-06", subject, body, $"## [1] FROM x — 2026-08-06 — {subject}\n{body}");
    }

    [Fact]
    public void Format_ImplementerEntryOnSpoke_TagsTowardSupervisor()
    {
        var channel = DiscoveredChannel_Factory.Create_ForImplementer("arb-fix", "imp-2", "unused");
        var entry = Build_Entry(ChannelAuthors.Implementer, "boundary report", "All green.");

        var text = MirrorText_Formatter.Format(channel, entry);

        Assert.StartsWith("🔵 [imp-2 → sup] #1 — boundary report", text);
        Assert.Contains("All green.", text);
    }

    [Fact]
    public void Format_SupervisorEntryOnSpoke_TagsTowardImplementer()
    {
        var channel = DiscoveredChannel_Factory.Create_ForImplementer("arb-fix", "imp-1", "unused");
        var entry = Build_Entry(ChannelAuthors.Supervisor, "verdict", "Close it.");

        var text = MirrorText_Formatter.Format(channel, entry);

        Assert.StartsWith("🔴 [sup → imp-1]", text);
    }

    [Fact]
    public void Format_SupervisorEntryOnOwnerChannel_TagsTowardOwner()
    {
        var channel = DiscoveredChannel_Factory.Create_ForOwner("arb-fix", "unused");
        var entry = Build_Entry(ChannelAuthors.Supervisor, "question for you", "Which option?");

        var text = MirrorText_Formatter.Format(channel, entry);

        Assert.StartsWith("🔴 [sup → owner]", text);
    }

    [Fact]
    public void Should_Mirror_OwnerEntryOnOwnerChannel_IsFalse()
    {
        var channel = DiscoveredChannel_Factory.Create_ForOwner("arb-fix", "unused");
        var entry = Build_Entry(ChannelAuthors.Owner, "via Telegram", "hello");

        Assert.False(MirrorText_Formatter.Should_Mirror(channel, entry));
    }

    [Fact]
    public void Should_Mirror_AppEntryOnOwnerChannel_IsTrue()
    {
        var channel = DiscoveredChannel_Factory.Create_ForOwner("general", "unused");
        var entry = Build_Entry(ChannelAuthors.App, "orchestration started", "done");

        Assert.True(MirrorText_Formatter.Should_Mirror(channel, entry));
        Assert.StartsWith("⚙ [app → owner]", MirrorText_Formatter.Format(channel, entry));
    }

    [Fact]
    public void Format_EmptyBody_HeaderOnly()
    {
        var channel = DiscoveredChannel_Factory.Create_ForImplementer("x", "imp-1", "unused");
        var entry = Build_Entry(ChannelAuthors.Implementer, "ack", "");

        var text = MirrorText_Formatter.Format(channel, entry);

        Assert.DoesNotContain("\n\n", text);
    }
}
