using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Sessions;

/// <summary>
/// THE DOOR BESIDE THE PROMOTION GATE, found by rev-5.
///
/// The desktop's "+ Implementer" button calls the launcher directly, so a click on a BASIC card
/// produced a solo plus an implementer and no supervisor — bypassing the request, the handover entry
/// and the owner's tap that the promotion path spends four steps enforcing.
///
/// The second-order effect is what makes it more than a stray session: nothing on that path stamps
/// `SupervisorSpawnedUtc`, so the orchestration still reads as BASIC, the watchdog never spawns a
/// supervisor into it, and the new implementer sits on its spoke waiting for a brief that cannot come
/// from anywhere. It burns a session's context and tokens to wait for something that does not exist.
///
/// Decision 21 decides where the guard lives: a click can only ask, so the enforcement is at the
/// point of effect rather than in the button's visibility.
/// </summary>
public class AddMemberShapeGateTests : IDisposable
{
    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;

    public AddMemberShapeGateTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-shapegate-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        _store = OrchestrationSessionStore_Factory.Create(_paths);

        _launcher = OrchestrationLauncher_Factory.Create(
            _paths, OrchestratorConfigProvider_Factory.Create(_paths), _store, new RecordingSpawner_Fake(), OrchestrationLog_Factory.Create(_paths));
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// THE CLICK THAT DEFEATED THE FEATURE. Through the REAL launcher, so this is what the button
    /// does — not what a helper returns.
    /// </summary>
    [Fact]
    public void AnImplementerCannotBeAddedToABasicOrchestration()
    {
        var basic = _launcher.Start_BasicOrchestration("repo", _tempRepo);

        var refusal = Assert.Throws<Exception>(() => _launcher.Add_Implementer(basic.OrchId));

        // The refusal names the SUPPORTED path. "Not allowed" would leave the owner stuck with no
        // idea that promotion is the thing they actually want.
        Assert.Contains("promotion", refusal.Message, StringComparison.OrdinalIgnoreCase);

        // And nothing was created — a refusal that still spawned would be the defect with a message.
        Assert.Single(_store.Get_Session(basic.OrchId).Members);
    }

    /// <summary>
    /// PROMOTION PASSES ITS OWN GATE UNAIDED, and that is the case that would have made this fix
    /// unshippable if it were false. `Promote_ToFullCrew` spawns the supervisor FIRST, so the stamp
    /// exists by the time it adds imp-1 — the same rule that refuses the button admits the promotion,
    /// with no exemption and no flag.
    ///
    /// It is also the guard on that ordering: if the spawn is ever moved after the member add, this
    /// reddens.
    /// </summary>
    [Fact]
    public void PromotionStillAddsItsImplementer()
    {
        var basic = _launcher.Start_BasicOrchestration("repo", _tempRepo);

        var promoted = _launcher.Promote_ToFullCrew(basic.OrchId);

        Assert.Contains(promoted.Members, member => member.MemberId == "imp-1" && member.ClosedUtc == null);
    }

    /// <summary>A crew takes implementers and reviewers exactly as before — the ordinary path.</summary>
    [Fact]
    public void ACrewStillTakesMembers()
    {
        var full = _launcher.Start_Orchestration("repo", _tempRepo);

        var withAnother = _launcher.Add_Implementer(full.OrchId);

        Assert.Contains(withAnother.Members, member => member.MemberId == "imp-2");
        Assert.Contains(_launcher.Add_Member(full.OrchId, MemberKinds.Reviewer).Members, member => member.MemberId == "rev-2");
    }

    /// <summary>
    /// AND THE OTHER DIRECTION: a crew cannot take a SOLO. Without it the rule would be "basic is
    /// special" rather than "each shape has its members", and nothing would stop a second solo being
    /// added to an orchestration that already has a supervisor reading the same channel.
    /// </summary>
    [Fact]
    public void ACrewCannotTakeASolo()
    {
        var full = _launcher.Start_Orchestration("repo", _tempRepo);

        Assert.Throws<Exception>(() => _launcher.Add_Member(full.OrchId, MemberKinds.Solo));
    }

    /// <summary>The rule itself, at all four combinations, so neither direction rests on one example.</summary>
    [Theory]
    [InlineData(null, MemberKinds.Solo, true)]
    [InlineData(null, MemberKinds.Implementer, false)]
    [InlineData(null, MemberKinds.Reviewer, false)]
    [InlineData("2026-08-13T10:00:00Z", MemberKinds.Solo, false)]
    [InlineData("2026-08-13T10:00:00Z", MemberKinds.Implementer, true)]
    [InlineData("2026-08-13T10:00:00Z", MemberKinds.Reviewer, true)]
    public void TheRuleIsShapeThenKind(string? spawnedUtc, MemberKinds kind, bool allowed)
    {
        var stamp = spawnedUtc == null ? (DateTime?)null : DateTime.Parse(spawnedUtc).ToUniversalTime();

        Assert.Equal(allowed, OrchestrationShape.Can_AddMember(stamp, kind));
    }
}
