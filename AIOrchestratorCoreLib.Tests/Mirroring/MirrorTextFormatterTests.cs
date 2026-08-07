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
    public void Should_Mirror_ImplementerSpokeTraffic_OnlyThePresenceOneLiner()
    {
        // The briefs/reports between supervisor and implementers made topics unreadable —
        // the ONLY spoke entry the owner sees is the presence line ("imp-1 online").
        var spoke = DiscoveredChannel_Factory.Create_ForImplementer("crm-2", "imp-1", "unused");

        Assert.False(MirrorText_Formatter.Should_Mirror(spoke, Build_Entry(ChannelAuthors.Supervisor, "TASK: fix crash", "long brief")));
        Assert.False(MirrorText_Formatter.Should_Mirror(spoke, Build_Entry(ChannelAuthors.Implementer, "report", "long report")));

        var presence = Build_Entry(ChannelAuthors.Implementer, "imp-1 online, awaiting brief", "details");
        Assert.True(MirrorText_Formatter.Should_Mirror(spoke, presence));
        Assert.Equal("🔵 imp-1: online", MirrorText_Formatter.Format(spoke, presence));
    }

    [Fact]
    public void Should_Mirror_OwnerChannel_SupervisorAndAppYes_OwnerEchoNo()
    {
        var ownerChannel = DiscoveredChannel_Factory.Create_ForOwner("crm-2", "unused");

        Assert.True(MirrorText_Formatter.Should_Mirror(ownerChannel, Build_Entry(ChannelAuthors.Supervisor, "s", "b")));
        Assert.True(MirrorText_Formatter.Should_Mirror(ownerChannel, Build_Entry(ChannelAuthors.App, "s", "b")));
        Assert.False(MirrorText_Formatter.Should_Mirror(ownerChannel, Build_Entry(ChannelAuthors.Owner, "via Telegram", "hello")));
    }

    [Fact]
    public void Format_Supervisor_BarePrefixAndBodyOnly_NoCeremony()
    {
        var ownerChannel = DiscoveredChannel_Factory.Create_ForOwner("crm-2", "unused");
        var entry = Build_Entry(ChannelAuthors.Supervisor, "STATUS", "- imp-1: building\n- no blockers");

        var text = MirrorText_Formatter.Format(ownerChannel, entry);

        Assert.Equal("🔴 Sup: - imp-1: building\n- no blockers", text);
        Assert.DoesNotContain("[sup", text);
        Assert.DoesNotContain("#1", text);
    }

    [Fact]
    public void Format_SupervisorWithEmptyBody_SubjectIsTheMessage()
    {
        var ownerChannel = DiscoveredChannel_Factory.Create_ForOwner("crm-2", "unused");
        var entry = Build_Entry(ChannelAuthors.Supervisor, "supervisor online — CRM — Projects\\Prova Amazon", "");

        Assert.Equal("🔴 Sup: supervisor online — CRM — Projects\\Prova Amazon", MirrorText_Formatter.Format(ownerChannel, entry));
    }

    [Fact]
    public void Format_GeneralSupervisor_HasItsOwnVoice_NotTheOrchestrationSupervisorOne()
    {
        var generalChannel = DiscoveredChannel_Factory.Create_ForOwner("general", "unused");
        var entry = Build_Entry(ChannelAuthors.Supervisor, "online", "starting orchestration: CRM");

        Assert.Equal("🟡 Gen-Sup: starting orchestration: CRM", MirrorText_Formatter.Format(generalChannel, entry));
    }

    [Fact]
    public void Format_App_SubjectOnly_BodyIsAgentFacingDetail()
    {
        var ownerChannel = DiscoveredChannel_Factory.Create_ForOwner("general", "unused");
        var entry = Build_Entry(ChannelAuthors.App, "orchestration 'crm-2' closed", "Sessions ended; folder kept as audit trail; Telegram topic deleted.");

        Assert.Equal("⚙ App: orchestration 'crm-2' closed", MirrorText_Formatter.Format(ownerChannel, entry));
    }

    [Fact]
    public void Is_StatusEntry_MatchesStatusSubjectPrefix_CaseInsensitive()
    {
        Assert.True(MirrorText_Formatter.Is_StatusEntry(Build_Entry(ChannelAuthors.Supervisor, "STATUS", "- no change")));
        Assert.True(MirrorText_Formatter.Is_StatusEntry(Build_Entry(ChannelAuthors.Supervisor, "status update", "x")));
        Assert.False(MirrorText_Formatter.Is_StatusEntry(Build_Entry(ChannelAuthors.Supervisor, "question", "x")));
    }
}
