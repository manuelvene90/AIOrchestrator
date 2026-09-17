using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Channels.StatusLog;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.RoleRunnerConfig;
using AIOrchestratorCoreLib.Running.TurnCursor;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// THE ROUTER, AND THE ONE LINE OF IT THAT PROTECTS THE OWNER'S PHONE.
///
/// <para>
/// EVERY CASE HERE IS SET UP SO THE POLICY SAYS <c>Log</c>. That is not decoration: the policy
/// refuses the log for a session it cannot see a state file for, so a test that passed a null state
/// would find its note in the channel whatever the router did with the audience — an assertion with
/// two routes to its state, which pins neither (CLAUDE.md decision 20). So every session below
/// drives its own turns, and the channel is therefore evidence of the audience screen and of nothing
/// else. Verified by mutation on 2026-09-16: deleting <c>audience == AppEntryAudiences.Owner</c>
/// turns the theory red in all five of its cases.
/// </para>
/// </summary>
public class AppNoteRoutingTests
{
    static readonly DateTime NOW = new(2026, 9, 15, 14, 30, 0, DateTimeKind.Local);

    static IRoleRunnerConfig Role(BookkeepingSinks sink, SessionRunners runner = SessionRunners.Print, WakeModes wake = WakeModes.Watcher) =>
        RoleRunnerConfig_Factory.Create(runner, ResumeModes.Transcript, null, null, wake, sink);

    /// <summary>
    /// The session's own state file — the only thing that knows whether the app opens this session's
    /// turns, and therefore whether anything will ever hand it a state pack to read a routed note in.
    /// </summary>
    static IPrintSessionState Session(bool drivesTurns) =>
        PrintSessionState_Factory.Create(
            "11111111-1111-1111-1111-111111111111", true, SessionRoles.Implementer, "repo-1", "imp-1",
            "/tmp", null, "/tmp/channel.md", new List<ITurnCursor>(), 1, 0, [], null, drivesTurns: drivesTurns);

    sealed class Files : IDisposable
    {
        public string Channel { get; } = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".md");
        public string Log { get; } = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".jsonl");

        public Files() => File.WriteAllText(Channel, "## [1] FROM supervisor — 2026-09-15 10:00 — brief\n\ndo X\n");

        public IReadOnlyList<IChannelEntry> ChannelEntries() => ChannelEntry_Parser.Parse_All(File.ReadAllText(Channel));

        public void Dispose()
        {
            File.Delete(Channel);
            File.Delete(Log);
        }
    }

    /// <summary>
    /// THE DEFAULT IS THE BEHAVIOUR EVERY LIVE MACHINE ALREADY HAS. Until somebody sets the key, a
    /// routed note is written exactly where it was written before this existed, byte for byte.
    /// </summary>
    [Fact]
    public void UnderTheDefaultSink_ARoutedNoteStillGoesToTheChannel()
    {
        using var files = new Files();

        Assert.True(AppNote_Writer.Write(
            files.Channel, files.Log, Role(BookkeepingSinks.Channel), Session(drivesTurns: true), AppNoteKinds.TurnEnded,
            AppEntryAudiences.Agent, "turn_ended imp-1 turn 4 — success", "request_id: r/imp-1/4", NOW));

        Assert.Equal(2, files.ChannelEntries().Count);
        Assert.Empty(StatusLog_Store.Read_Entries(files.Log));
    }

    [Fact]
    public void UnderTheLogSink_ARoutedAgentNoteLeavesTheChannel()
    {
        using var files = new Files();

        Assert.True(AppNote_Writer.Write(
            files.Channel, files.Log, Role(BookkeepingSinks.Log), Session(drivesTurns: true), AppNoteKinds.TurnEnded,
            AppEntryAudiences.Agent, "turn_ended imp-1 turn 4 — success", "request_id: r/imp-1/4", NOW));

        Assert.Single(files.ChannelEntries());
        Assert.Equal("[agent] turn_ended imp-1 turn 4 — success", Assert.Single(StatusLog_Store.Read_Entries(files.Log)).Subject);
    }

    /// <summary>
    /// THE SINK IS THE POLICY'S, NOT THE CONFIG'S — and this is the case that made the router take a
    /// session at all. A member demoted by <c>OrchestrationLauncherModel.Demote_ToTerminal</c> goes on
    /// reading <c>runner: print</c> in config while its own file says the dispatcher let it go, and
    /// nobody writes that session a pack. Asking the config alone would have made it deaf.
    /// </summary>
    [Fact]
    public void UnderTheLogSink_ASessionTheDispatcherHasLetGo_KeepsItsNoteInTheChannel()
    {
        using var files = new Files();

        Assert.True(AppNote_Writer.Write(
            files.Channel, files.Log, Role(BookkeepingSinks.Log), Session(drivesTurns: false), AppNoteKinds.LedgerAdvisory,
            AppEntryAudiences.Agent, "PLAN.md is behind your verdicts", "update it", NOW));

        Assert.Equal(2, files.ChannelEntries().Count);
        Assert.Empty(StatusLog_Store.Read_Entries(files.Log));
    }

    /// <summary>
    /// THE ORACLE OF THIS WHOLE PLAN, and it is one line of production code: the owner's phone is fed
    /// from the CHANNEL — <c>MirrorText_Formatter.Should_Mirror</c> refuses an <c>[agent]</c>-tagged
    /// app entry and mirrors every other one — so an entry that leaves the channel leaves the phone.
    /// The brief's constraint is that every entry the owner sees today goes on being seen; this is
    /// where that is discharged, mechanically, for all 78 sites at once.
    ///
    /// <para>
    /// It is a THEORY over every kind, not one case, because the failure it guards against is a
    /// future kind added without the audience screen. A single-kind assertion would pass while the
    /// next one was wrong. <see cref="EveryKind"/> is built from <c>Enum.GetValues</c>, so a kind
    /// added to <see cref="AppNoteKinds"/> next year is covered by this test on the commit that adds
    /// it, with nobody remembering to come here.
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryKind))]
    public void AnOwnerFacingNoteNeverLeavesTheChannel_WhateverTheKindAndWhateverTheSink(AppNoteKinds kind)
    {
        using var files = new Files();

        Assert.True(AppNote_Writer.Write(
            files.Channel, files.Log, Role(BookkeepingSinks.Log), Session(drivesTurns: true), kind,
            AppEntryAudiences.Owner, "turn stalled solo-1 turn 9", "it has not answered", NOW));

        Assert.Equal(2, files.ChannelEntries().Count);
        Assert.Empty(StatusLog_Store.Read_Entries(files.Log));
    }

    /// <summary>
    /// THE OPPOSITE ROAD, and it is what makes the theory above mean the audience and not the sink:
    /// the identical call with the identical kind and the identical session goes to the LOG when the
    /// audience is the agent. Without this pair, "it landed in the channel" would be consistent with a
    /// router that never consults the policy at all.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryKind))]
    public void TheSameKindWithTheAgentAudience_DoesLeaveTheChannel(AppNoteKinds kind)
    {
        using var files = new Files();

        Assert.True(AppNote_Writer.Write(
            files.Channel, files.Log, Role(BookkeepingSinks.Log), Session(drivesTurns: true), kind,
            AppEntryAudiences.Agent, "turn stalled solo-1 turn 9", "it has not answered", NOW));

        Assert.Single(files.ChannelEntries());
        Assert.Single(StatusLog_Store.Read_Entries(files.Log));
    }

    public static TheoryData<AppNoteKinds> EveryKind
    {
        get
        {
            TheoryData<AppNoteKinds> data = [];

            foreach (var kind in Enum.GetValues<AppNoteKinds>())
                data.Add(kind);

            return data;
        }
    }

    /// <summary>
    /// A LOCKED CHANNEL IS STILL A FALSE, and the callers depend on it: several of them record a memo
    /// on the strength of the return and would suppress the retry the next tick owes.
    /// </summary>
    [Fact]
    public void AnUnwritableChannel_AnswersFalse()
    {
        using var files = new Files();

        Assert.False(AppNote_Writer.Write(
            Path.Combine(files.Channel, "not-a-folder", "c.md"),
            files.Log,
            Role(BookkeepingSinks.Channel), Session(drivesTurns: true), AppNoteKinds.LedgerAdvisory,
            AppEntryAudiences.Agent, "PLAN.md is behind your verdicts", "update it", NOW));
    }

    /// <summary>The log half of the same contract — a record that could not be written is a false too.</summary>
    [Fact]
    public void AnUnwritableLog_AnswersFalse()
    {
        using var files = new Files();

        // THE LOG FILE HAS TO EXIST for the path below to be unwritable: the store creates missing
        // folders, so a path under an ABSENT file is one it would happily create. Under a real file
        // it cannot.
        File.WriteAllText(files.Log, string.Empty);

        Assert.False(AppNote_Writer.Write(
            files.Channel,
            Path.Combine(files.Log, "not-a-folder", "s.jsonl"),
            Role(BookkeepingSinks.Log), Session(drivesTurns: true), AppNoteKinds.LedgerAdvisory,
            AppEntryAudiences.Agent, "PLAN.md is behind your verdicts", "update it", NOW));

        // AND THE CHANNEL IS NOT A FALLBACK. A routed note that could not reach the log must not
        // quietly reappear in the boot read; the false is the whole of the report.
        Assert.Single(files.ChannelEntries());
    }

    /// <summary>
    /// THE SAME EVENT, TWO DESTINATIONS, DECIDED BY WHO IT IS FOR. A stall on a member is bookkeeping
    /// its supervisor reads at its next turn; the identical stall on the SUPERVISOR is the owner's
    /// only warning that their orchestration has stopped, and the dispatcher's <c>Stall_Audience</c>
    /// already says so. Routing on the audience is what makes plan 02 unable to take anything off the
    /// owner's phone — the seven turn-machinery sites of task 6 pass that value straight through and
    /// add no branch of their own.
    /// </summary>
    [Fact]
    public void AStallOnAMemberMoves_TheSameStallOnASupervisorDoesNot()
    {
        using var members = new Files();
        using var supervisors = new Files();

        Assert.True(AppNote_Writer.Write(members.Channel, members.Log, Role(BookkeepingSinks.Log), Session(drivesTurns: true),
            AppNoteKinds.TurnMachinery, AppEntryAudiences.Agent, "turn stalled imp-1 turn 9", "no reply", NOW));

        Assert.True(AppNote_Writer.Write(supervisors.Channel, supervisors.Log, Role(BookkeepingSinks.Log), Session(drivesTurns: true),
            AppNoteKinds.TurnMachinery, AppEntryAudiences.Owner, "turn stalled supervisor turn 9", "no reply", NOW));

        Assert.Single(members.ChannelEntries());
        Assert.Equal("[agent] turn stalled imp-1 turn 9", Assert.Single(StatusLog_Store.Read_Entries(members.Log)).Subject);

        Assert.Equal(2, supervisors.ChannelEntries().Count);
        Assert.Equal("turn stalled supervisor turn 9", supervisors.ChannelEntries()[^1].Subject);
        Assert.Empty(StatusLog_Store.Read_Entries(supervisors.Log));
    }
}
