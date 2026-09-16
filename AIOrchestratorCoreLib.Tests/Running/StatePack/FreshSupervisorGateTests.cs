using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.StatePack;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running.StatePack;

/// <summary>
/// THE BRAKE ON THE ONE-WAY DOOR. Going fresh throws away the session's conversation, and the
/// conversation is the only place its conclusions live until it has written them down. A supervisor
/// flipped to <c>fresh</c> with an empty conclusions file loses them silently: nothing errors, the
/// turns keep coming, and a dead end is re-proposed a week later at the cost of somebody's day.
///
/// <para>
/// IN THE APP AND NOT IN A HOOK (CLAUDE.md decision 21). A hook advises and a session can unwire it;
/// this failure is silent, so the restraint has to sit at the point of effect. And it REFUSES rather
/// than warning afterwards, because an alert about memory already discarded is a post-mortem. The
/// cost of refusing is that the session keeps its transcript for one more turn, which is exactly the
/// behaviour it has today.
/// </para>
/// </summary>
public class FreshSupervisorGateTests : IDisposable
{
    readonly string _root;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationLog _log;

    public FreshSupervisorGateTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"aiorch-gate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _paths = SupervisionPaths_Factory.Create(_root);
        _log = OrchestrationLog_Factory.Create(_paths);
        Directory.CreateDirectory(_paths.Get_OrchestrationFolder("repo-1"));
        File.WriteAllText(_paths.Get_OwnerChannelFile("repo-1"), "");

        // The gate remembers what it has said for the life of the process, and xUnit runs a class's
        // cases in one. Each case starts from a bridge that has just booted.
        FreshSupervisor_Gate.Forget_EverythingItHasSaid();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    IPrintSessionState State(SessionRoles role, string memberId)
    {
        return PrintSessionState_Factory.Create_New(
            "sid", role, "repo-1", memberId, _root, null, _paths.Get_OwnerChannelFile("repo-1"), []);
    }

    void Write_Conclusions(string text)
    {
        File.WriteAllText(StatePack_Locator.Get_ConclusionsFile_OrNull(_paths, SessionRoles.Supervisor, "repo-1", "supervisor")!, text);
    }

    [Fact]
    public void A_supervisor_with_no_conclusions_file_is_not_started_fresh()
    {
        Assert.False(FreshSupervisor_Gate.Allows(_paths, State(SessionRoles.Supervisor, "supervisor"), _log));
    }

    /// <summary>
    /// AN EMPTY FILE COUNTS AS NONE — it is the exact state in which the flip loses everything and
    /// reports success.
    /// </summary>
    [Fact]
    public void An_empty_conclusions_file_counts_as_none()
    {
        Write_Conclusions("   \n\n");

        Assert.False(FreshSupervisor_Gate.Allows(_paths, State(SessionRoles.Supervisor, "supervisor"), _log));
    }

    [Fact]
    public void A_supervisor_that_has_written_its_conclusions_may_go_fresh()
    {
        Write_Conclusions("dead ends:\n- the fence route (2026-09-13): the tailer re-anchors first");

        Assert.True(FreshSupervisor_Gate.Allows(_paths, State(SessionRoles.Supervisor, "supervisor"), _log));
    }

    /// <summary>
    /// SAID ONCE, AND TO THE AGENT. A refusal repeated every tick is the waterfall this system exists
    /// to prevent (decision 14), and an alert the owner cannot act on does not reach the phone
    /// (decision 15) — the supervisor is the one who can fix this, by writing the file.
    /// </summary>
    [Fact]
    public void The_refusal_is_filed_once_in_the_channel_and_is_agent_facing()
    {
        var state = State(SessionRoles.Supervisor, "supervisor");

        FreshSupervisor_Gate.Allows(_paths, state, _log);
        FreshSupervisor_Gate.Allows(_paths, state, _log);
        FreshSupervisor_Gate.Allows(_paths, state, _log);

        var filed = ChannelEntry_Parser.Parse_All(File.ReadAllText(_paths.Get_OwnerChannelFile("repo-1")))
            .Where(entry => entry.Author == ChannelAuthors.App)
            .ToList();

        var only = Assert.Single(filed);
        Assert.True(AppEntryAudience_Tag.Is_AgentTagged(only.Subject));
        Assert.Contains("conclusions", only.Subject, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// AND IT IS STILL ONCE ACROSS A RESTART. The one-shot is read back out of the CHANNEL, not held
    /// in memory: an in-process set is lost every time the bridge restarts, and this notice would
    /// then be re-filed at every boot — a waterfall with a longer period, which is still a waterfall.
    /// </summary>
    [Fact]
    public void It_is_still_once_after_the_app_forgets_everything()
    {
        var state = State(SessionRoles.Supervisor, "supervisor");

        FreshSupervisor_Gate.Allows(_paths, state, _log);
        FreshSupervisor_Gate.Forget_EverythingItHasSaid();   // what a restart does
        FreshSupervisor_Gate.Allows(_paths, state, _log);

        Assert.Single(ChannelEntry_Parser.Parse_All(File.ReadAllText(_paths.Get_OwnerChannelFile("repo-1"))),
            entry => entry.Author == ChannelAuthors.App);
    }

    /// <summary>A MEMBER IS NEVER GATED: its channel is its durable state by design (decision 8), and its brief is in it.</summary>
    [Fact]
    public void A_member_is_not_gated()
    {
        Assert.True(FreshSupervisor_Gate.Allows(_paths, State(SessionRoles.Implementer, "imp-1"), _log));
    }

    /// <summary>
    /// AND NEITHER IS THE GENERAL SUPERVISOR, which is the one role shipping `resume: fresh` BY
    /// DEFAULT and is stateless across launches by owner directive (decision 8). Gating it would stop
    /// the only session already running the regime this plan is trying to reach.
    /// </summary>
    [Fact]
    public void The_general_supervisor_is_not_gated()
    {
        Assert.True(FreshSupervisor_Gate.Allows(_paths, State(SessionRoles.General, "general"), _log));
    }

    /// <summary>THE SOLO IS, because it owns an endeavour exactly as a supervisor does.</summary>
    [Fact]
    public void A_solo_is_gated_like_a_supervisor()
    {
        Assert.False(FreshSupervisor_Gate.Allows(_paths, State(SessionRoles.Solo, "solo"), _log));
    }
}
