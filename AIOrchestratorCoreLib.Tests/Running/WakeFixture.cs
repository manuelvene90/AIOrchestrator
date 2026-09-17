using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Reviewing;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.ExecutedTurn;
using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.RunnerConfigs;
using AIOrchestratorCoreLib.Running.TurnCursor;
using AIOrchestratorCoreLib.Running.TurnSource;
using AIOrchestratorCoreLib.Running.WakeDecision;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// A SUPERVISOR THAT HAS TAKEN A TURN, over a real supervision root with real channel files and a
/// real state file — the resolver reads cursors and entries off the disk, so a mock of either
/// would be a test of the mock. The boot turn is deliberately spent (<c>ExecutedTurns</c> is
/// non-empty): it is the one rule that answers before all the others, so leaving it unspent would
/// make every case read "the session has not taken a turn yet" and pin nothing.
///
/// <para>
/// ITS OWN FILE SINCE 2026-09-17, unchanged in behaviour. It was a private nested class of
/// <see cref="WakeDecisionResolverTests"/>; the routed-hold cases
/// (<c>Reviewing.RoutedReportRidesTheNextTurnTests</c>) need the same supervisor over the same disk,
/// and a second fixture is the thing this suite has been told repeatedly not to write.
/// </para>
/// </summary>
public sealed class WakeFixture : IDisposable
{
    /// <summary>
    /// The instant every entry this fixture writes is stamped with, and the one the cases measure
    /// their windows from. ONE copy, because two test classes now hand it to the resolver and a
    /// second literal is a second clock (CLAUDE.md decision 12).
    /// </summary>
    public static readonly DateTime T0 = new(2026, 9, 15, 10, 0, 0);

    readonly PrintRunnerTestHarness _harness;
    readonly string _orchId;
    readonly string _supervisorId;

    WakeFixture(PrintRunnerTestHarness harness, string orchId, string supervisorId)
    {
        _harness = harness;
        _orchId = orchId;
        _supervisorId = supervisorId;
    }

    public ISupervisionPaths Paths => _harness.Paths;

    /// <summary>
    /// The identity (<see cref="ChannelEntry_Digest"/>) of the last entry this fixture appended —
    /// what a routed contract names, and what a case hands to <see cref="Decide"/> as riding.
    /// </summary>
    public string IdentityOfLastEntry { get; private set; } = string.Empty;

    public static WakeFixture ForSupervisor(string orchId)
    {
        var harness = new PrintRunnerTestHarness("supervisor:stream");
        var supervisorId = harness.Register_Supervisor(orchId);
        var fixture = new WakeFixture(harness, orchId, supervisorId);

        fixture.Spend_TheBootTurn();
        return fixture;
    }

    public WakeFixture With_OwnerEntry(string text)
    {
        var file = _harness.Paths.Get_OwnerChannelFile(_orchId);

        if (!ChannelAppender.Append_OwnerEntry(file, text, T0))
            throw new Exception($"could not append the owner entry to '{_orchId}'");

        Remember_LastEntryOf(file);
        return this;
    }

    /// <summary>
    /// A member whose spoke this supervisor HAS already been handed something from — the greeting
    /// is appended and then absorbed into the cursor, exactly as the first turn absorbs it, so what
    /// the case measures afterwards is the digest and not the first-entry exemption.
    ///
    /// <para>
    /// THE KIND COMES FROM THE ID THE CASE NAMES (<see cref="MemberKind_Ids.Resolve_Kind"/>), so
    /// <c>"rev-1"</c> registers a reviewer and <c>"imp-1"</c> an implementer — and the allocation is
    /// still asserted, so a case that names an id the store would not hand out fails loudly instead
    /// of silently measuring a different member.
    /// </para>
    /// </summary>
    public WakeFixture With_SeenMemberEntry(string memberId, string subject, string body = "reporting")
    {
        var (_, allocated) = _harness.Register_Member(MemberKind_Ids.Resolve_Kind(memberId), _orchId);

        if (!string.Equals(allocated, memberId, StringComparison.Ordinal))
            throw new Exception($"the store allocated '{allocated}', not the '{memberId}' this case names");

        var spokeFile = _harness.Paths.Get_ImplementerChannelFile(_orchId, allocated);
        var author = MemberKind_Ids.Resolve_Kind(allocated) == MemberKinds.Reviewer ? ChannelAuthors.Reviewer : ChannelAuthors.Implementer;

        if (!ChannelAppender.Append_SessionEntry(spokeFile, author, $"{allocated} online", "reporting for duty", T0))
            throw new Exception($"could not append '{allocated}' greeting to its spoke");

        Absorb_Spoke(allocated, spokeFile);

        if (!ChannelAppender.Append_SessionEntry(spokeFile, author, subject, body, T0))
            throw new Exception($"could not append '{allocated}' report to its spoke");

        Remember_LastEntryOf(spokeFile);
        return this;
    }

    public WakeFixture With_AppNote(string subject, DateTime stampedLocal)
    {
        var file = _harness.Paths.Get_OwnerChannelFile(_orchId);

        if (!ChannelAppender.Append_AppEntry(file, AppEntryAudiences.Agent, subject, "Bring it up to date.", stampedLocal))
            throw new Exception($"could not append the app note to '{_orchId}'");

        Remember_LastEntryOf(file);
        return this;
    }

    /// <summary>A contract the supervisor has declared and the app has NOT yet relayed.</summary>
    public WakeFixture With_DeclaredContract(string implementerId, string reviewerId)
    {
        Store_Contract(Declare(implementerId, reviewerId));
        return this;
    }

    /// <summary>A contract whose relay the app has already written into the reviewer's channel.</summary>
    public WakeFixture With_RoutedContract(string implementerId, string reviewerId, string reportIdentity)
    {
        Store_Contract(RerouteContract_Factory.CreateFrom_Routed(
            Declare(implementerId, reviewerId), reportIdentity, headCommit: "def5678", routedUtc: DateTime.UtcNow));

        return this;
    }

    /// <summary>
    /// The state file of any registered session of this orchestration — how a case asks
    /// <see cref="RoutedHold_Policy"/> the same question about a role that is not the supervisor.
    /// </summary>
    public IPrintSessionState StateFor(SessionRoles role, string memberId)
    {
        return _harness.Read_State(role, _orchId, memberId);
    }

    public IPrintSessionState StateOfTheSupervisor()
    {
        return Read_State();
    }

    public IWakeDecision? Resolve(DateTime? digestHeldSince, DateTime nowLocal)
    {
        return WakeDecision_Resolver.Resolve_OrNull(
            _harness.Paths, _harness.Store, Read_State(), RunnerConfigs_Factory.Create_Default(), digestHeldSince, nowLocal);
    }

    /// <summary>
    /// THE DECISION WITH THE RIDING SET NAMED OUTRIGHT — the same read <see cref="Resolve"/> does,
    /// stopping one step short so a case can state which identities ride instead of arranging a
    /// contract on disk to imply them. The end-to-end wiring (contract file → resolver → hold) is
    /// pinned separately through <see cref="Resolve"/>, because a case that only ever enters here
    /// would pass with the resolver never asking <see cref="RoutedHold_Policy"/> at all.
    /// </summary>
    public IWakeDecision? Decide(IReadOnlyCollection<string> ridingOnly, DateTime? digestHeldSince, DateTime nowLocal)
    {
        var state = Read_State();
        var sources = TurnSources_Resolver.Resolve(_harness.Paths, _harness.Store, state.Role, state.OrchId, state.MemberId);
        var reads = WakeDecision_Resolver.Read_Pending(state, state.Role, sources);
        var ordered = PendingTraffic_Orderer.Order([.. reads.Select(read => (read.Source, read.Pending))]);

        return WakeDecision_Resolver.Decide_OrNull(
            _harness.Paths,
            state,
            sources,
            ordered,
            WakeDecision_Resolver.Describe_FirstContactSources(reads),
            ridingOnly,
            digestHeldSince,
            nowLocal,
            RunnerConfigs_Factory.Create_Default().MemberDigestWindow);
    }

    IRerouteContract Declare(string implementerId, string reviewerId)
    {
        return RerouteContract_Factory.Create_Declared(
            id: $"decl-{implementerId}-{reviewerId}",
            orchId: _orchId,
            implementerId: implementerId,
            reviewerId: reviewerId,
            baseCommit: "abc1234",
            brief: "F1 was the ordering bug. Read the delta only.",
            declaredUtc: DateTime.UtcNow);
    }

    void Store_Contract(IRerouteContract contract)
    {
        RerouteContract_Store.Write_Open(_harness.Paths, _orchId, [.. RerouteContract_Store.Read_Open(_harness.Paths, _orchId), contract]);
    }

    void Remember_LastEntryOf(string channelFile)
    {
        var entries = ChannelEntry_Parser.Parse_All(File.ReadAllText(channelFile));

        if (entries.Count == 0)
            throw new Exception($"nothing parsed out of '{channelFile}' after an append — the fixture cannot name the entry it just wrote");

        IdentityOfLastEntry = ChannelEntry_Digest.Compute(entries[^1]);
    }

    IPrintSessionState Read_State()
    {
        return _harness.Read_State(SessionRoles.Supervisor, _orchId, _supervisorId);
    }

    void Write_State(IPrintSessionState state)
    {
        PrintSessionState_Store.Write(PrintSessionState_Store.Get_StateFile(_harness.Paths, SessionRoles.Supervisor, _orchId, _supervisorId), state);
    }

    void Spend_TheBootTurn()
    {
        var state = Read_State();

        Write_State(PrintSessionState_Factory.CreateFrom_Existing_TurnExecuted(
            state,
            ExecutedTurn_Factory.Create(1, "req-boot", 0, 0, DateTime.UtcNow, "ok", null),
            state.SessionId,
            state.Cursors));
    }

    /// <summary>Records everything now live in the spoke as delivered, the way a completed turn does.</summary>
    void Absorb_Spoke(string memberId, string spokeFile)
    {
        var state = Read_State();
        var source = TurnSource_Factory.Create_Spoke(memberId, spokeFile);
        var baseline = TurnCursor_Factory.Create_Baseline(source, SessionRoles.Supervisor, ChannelEntry_Parser.Parse_All(File.ReadAllText(spokeFile)));

        if (baseline.Delivered.Count == 0 || baseline.HighWaterIndex == 0)
            throw new Exception($"the greeting was not absorbed into '{memberId}' cursor, so the case would measure the first-entry exemption instead of the digest");

        Write_State(PrintSessionState_Factory.CreateFrom_Existing_Cursors(
            state, [.. state.Cursors.Where(cursor => !string.Equals(cursor.SourceKey, memberId, StringComparison.OrdinalIgnoreCase)), baseline]));
    }

    public void Dispose()
    {
        _harness.Dispose();
    }
}
