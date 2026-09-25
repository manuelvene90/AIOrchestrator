using AIOrchestratorCoreLib.Bridge.Siblings;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.GeneralSupervision.SpawnSiblingRequest;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.Siblings;

/// <summary>
/// THE REFUSAL TABLE (spec 2026-09-23 §4.2), one label per test, over a world built in memory. The
/// validator is pure on purpose: it is asked at arrival, again when the prompt is drawn and again at the
/// tap, so what it decides must depend on the gathered world and nothing else.
///
/// <para>
/// The paths are built from this OS's temp root (never touched on disk), so they are ABSOLUTE on every
/// OS the suite runs on — a hard-coded <c>C:\</c> would read as relative on Linux and every test would
/// be refused as worktree-not-absolute for the wrong reason.
/// </para>
/// </summary>
public class SiblingRequestValidatorTests
{
    const string REQUESTER_ID = "ai-orchestrator-7";
    const string NAME = "AI-Orch · limits rework";

    static readonly DateTime CREATED = new(2026, 9, 23, 10, 0, 0, DateTimeKind.Utc);
    static readonly string ROOT = Path.Combine(Path.GetTempPath(), "sibling-validator-never-created");
    static readonly string REPO = Path.Combine(ROOT, "repo");
    static readonly string WORKTREE = Path.Combine(ROOT, "repo.worktrees", "limits");
    static readonly string OTHER_WORKTREE = Path.Combine(ROOT, "repo.worktrees", "digest");

    // ---------------------------------------------------------------- unspawnable, not-a-solo

    [Fact]
    public void AMissingRequester_IsUnspawnable()
    {
        var refusal = Decide(World(requester: null, sessions: []));

        Assert.Equal(SiblingRefusals.UNSPAWNABLE, refusal?.Label);
        Assert.Contains(REQUESTER_ID, refusal?.Body);
    }

    [Fact]
    public void AClosedRequester_IsUnspawnable()
    {
        var requester = Session(REQUESTER_ID, closed: true);

        Assert.Equal(SiblingRefusals.UNSPAWNABLE, Decide(World(requester, [requester]))?.Label);
    }

    [Fact]
    public void ACrew_IsNotASolo()
    {
        var requester = Session(REQUESTER_ID, supervisorSpawned: true);
        var refusal = Decide(World(requester, [requester]));

        Assert.Equal(SiblingRefusals.NOT_A_SOLO, refusal?.Label);
        Assert.Contains("add-implementer", refusal?.Body);
    }

    // ---------------------------------------------------------------- handover-already-used

    [Fact]
    public void AHandoverCitedByALiveSibling_IsAlreadyUsed_AndNamesIt()
    {
        var requester = Session(REQUESTER_ID, endeavourId: REQUESTER_ID);
        var child = Session("ai-orchestrator-8", endeavourId: REQUESTER_ID, bornFromHandover: $"{REQUESTER_ID}#14", workingPath: OTHER_WORKTREE);

        var refusal = Decide(World(requester, [requester, child]));

        Assert.Equal(SiblingRefusals.HANDOVER_ALREADY_USED, refusal?.Label);
        Assert.Contains("already started as ai-orchestrator-8", refusal?.Body);
    }

    [Fact]
    public void AHandoverCitedByAnotherParkedRequest_IsAlreadyUsed_AndSaysHeld()
    {
        var requester = Session(REQUESTER_ID);
        var parked = Request(sourceFilePath: Path.Combine(ROOT, "awaiting-owner", "sibling-earlier.json"));

        var refusal = Decide(World(requester, [requester], otherParked: [parked]));

        Assert.Equal(SiblingRefusals.HANDOVER_ALREADY_USED, refusal?.Label);
        Assert.Contains("already held — do not re-drop", refusal?.Body);
    }

    /// <summary>ONE HANDOVER BIRTHS AT MOST ONE SIBLING, EVER (§4.4) — closing the child does not free its brief for a twin.</summary>
    [Fact]
    public void AHandoverCitedByAClosedSibling_IsStillAlreadyUsed()
    {
        var requester = Session(REQUESTER_ID, endeavourId: REQUESTER_ID);
        var child = Session("ai-orchestrator-8", endeavourId: REQUESTER_ID, bornFromHandover: $"{REQUESTER_ID}#14", closed: true);

        var refusal = Decide(World(requester, [requester, child]));

        Assert.Equal(SiblingRefusals.HANDOVER_ALREADY_USED, refusal?.Label);
        Assert.Contains("ai-orchestrator-8", refusal?.Body);
    }

    /// <summary>The key is the requester's own entry: another orchestration's [14] is a different handover.</summary>
    [Fact]
    public void TheSameIndexCitedFromAnotherOrchestration_IsNotAlreadyUsed()
    {
        var requester = Session(REQUESTER_ID);
        var stranger = Session("crm-3", bornFromHandover: "crm-2#14", workingPath: OTHER_WORKTREE);
        var strangerParked = Request(orchId: "crm-2", sourceFilePath: Path.Combine(ROOT, "awaiting-owner", "sibling-crm.json"));

        Assert.Null(Decide(World(requester, [requester, stranger], otherParked: [strangerParked])));
    }

    // ---------------------------------------------------------------- no-handover-entry

    [Fact]
    public void NoEntryAtThatIndex_IsNoHandoverEntry()
    {
        var requester = Session(REQUESTER_ID);

        var refusal = Decide(World(requester, [requester], history: [Entry(13, ChannelAuthors.Solo, "HANDOVER — limits")]));

        Assert.Equal(SiblingRefusals.NO_HANDOVER_ENTRY, refusal?.Label);
        Assert.Contains("[14]", refusal?.Body);
    }

    /// <summary>THE AUTHOR GATE: the owner quoting "HANDOVER" is not the solo writing one down (HandoverEntry_Detector's rule).</summary>
    [Fact]
    public void AnEntryAtThatIndexByTheOwner_IsNoHandoverEntry()
    {
        var requester = Session(REQUESTER_ID);

        var refusal = Decide(World(requester, [requester], history: [Entry(14, ChannelAuthors.Owner, "HANDOVER — can you split this?")]));

        Assert.Equal(SiblingRefusals.NO_HANDOVER_ENTRY, refusal?.Label);
    }

    [Fact]
    public void AnEntryWithoutTheMarker_IsNoHandoverEntry()
    {
        var requester = Session(REQUESTER_ID);

        var refusal = Decide(World(requester, [requester], history: [Entry(14, ChannelAuthors.Solo, "limits — notes on the split")]));

        Assert.Equal(SiblingRefusals.NO_HANDOVER_ENTRY, refusal?.Label);
    }

    // ---------------------------------------------------------------- at-cap

    [Fact]
    public void AtTheCap_IsAtCap_AndNamesTheOpenSiblings()
    {
        var requester = Session(REQUESTER_ID, endeavourId: REQUESTER_ID);
        var first = Session("ai-orchestrator-8", endeavourId: REQUESTER_ID, bornFromHandover: $"{REQUESTER_ID}#3", workingPath: OTHER_WORKTREE, displayName: "AI-Orch · digest rework");
        var second = Session("ai-orchestrator-9", endeavourId: REQUESTER_ID, bornFromHandover: $"{REQUESTER_ID}#5", workingPath: Path.Combine(ROOT, "repo.worktrees", "menu"), displayName: "AI-Orch · menu wiring");

        var refusal = Decide(World(requester, [requester, first, second], maxOpenMembers: 3));

        Assert.Equal(SiblingRefusals.AT_CAP, refusal?.Label);
        Assert.Contains("ai-orchestrator-8", refusal?.Body);
        Assert.Contains("ai-orchestrator-9", refusal?.Body);
        Assert.Contains("close", refusal?.Body);
    }

    [Fact]
    public void OneBelowTheCap_IsAllowed()
    {
        var requester = Session(REQUESTER_ID, endeavourId: REQUESTER_ID);
        var first = Session("ai-orchestrator-8", endeavourId: REQUESTER_ID, bornFromHandover: $"{REQUESTER_ID}#3", workingPath: OTHER_WORKTREE);

        Assert.Null(Decide(World(requester, [requester, first], maxOpenMembers: 3)));
    }

    /// <summary>AN UNLINKED REQUESTER IS ONE OPEN MEMBER — it has no endeavour id yet, and it is still a session.</summary>
    [Fact]
    public void AnUnlinkedRequester_CountsAsOne()
    {
        var requester = Session(REQUESTER_ID);

        Assert.Equal(SiblingRefusals.AT_CAP, Decide(World(requester, [requester], maxOpenMembers: 1))?.Label);
        Assert.Null(Decide(World(requester, [requester], maxOpenMembers: 2)));
    }

    /// <summary>A CLOSED SIBLING HOLDS NO SEAT — the cap counts open members (O2).</summary>
    [Fact]
    public void AClosedSibling_DoesNotCountTowardsTheCap()
    {
        var requester = Session(REQUESTER_ID, endeavourId: REQUESTER_ID);
        var closed = Session("ai-orchestrator-8", endeavourId: REQUESTER_ID, bornFromHandover: $"{REQUESTER_ID}#3", closed: true);

        Assert.Null(Decide(World(requester, [requester, closed], maxOpenMembers: 2)));
    }

    // ---------------------------------------------------------------- the worktree

    /// <summary>
    /// A RELATIVE PATH IS REFUSED, never resolved (Task 6 carry, binding). Resolved against the app's
    /// current directory it names nobody's worktree, and stored it would be the cwd of every respawn.
    /// </summary>
    [Fact]
    public void ARelativeWorktree_IsNotAbsolute()
    {
        var requester = Session(REQUESTER_ID);

        var refusal = Decide(World(requester, [requester], worktreeExists: true), Request(worktree: Path.Combine("repo.worktrees", "limits")));

        Assert.Equal(SiblingRefusals.WORKTREE_NOT_ABSOLUTE, refusal?.Label);
        Assert.Contains("absolute", refusal?.Body);
    }

    [Fact]
    public void AMissingWorktree_IsWorktreeMissing()
    {
        var requester = Session(REQUESTER_ID);

        Assert.Equal(SiblingRefusals.WORKTREE_MISSING, Decide(World(requester, [requester], worktreeExists: false))?.Label);
    }

    [Fact]
    public void AWorktreeNotListedByGit_IsNotOfRepo()
    {
        var requester = Session(REQUESTER_ID);

        var refusal = Decide(World(requester, [requester], repoWorktrees: [REPO, OTHER_WORKTREE]));

        Assert.Equal(SiblingRefusals.WORKTREE_NOT_OF_REPO, refusal?.Label);
        Assert.Contains(OTHER_WORKTREE, refusal?.Body);
    }

    /// <summary>GIT'S SPELLING IS THE SAME TREE: forward slashes and another case do not make it foreign.</summary>
    [Fact]
    public void AWorktreeListedInGitsSpelling_IsOfRepo()
    {
        var requester = Session(REQUESTER_ID);

        Assert.Null(Decide(World(requester, [requester], repoWorktrees: [REPO, WORKTREE.Replace('\\', '/').ToUpperInvariant()])));
    }

    [Fact]
    public void TheRequestersOwnRepoPath_IsShared()
    {
        var requester = Session(REQUESTER_ID);

        var refusal = Decide(World(requester, [requester]), Request(worktree: REPO));

        Assert.Equal(SiblingRefusals.WORKTREE_SHARED, refusal?.Label);
        Assert.Contains("never share a tree", refusal?.Body);
    }

    [Fact]
    public void AnOpenSiblingsWorktree_IsShared()
    {
        var requester = Session(REQUESTER_ID, endeavourId: REQUESTER_ID);
        var sibling = Session("ai-orchestrator-8", endeavourId: REQUESTER_ID, bornFromHandover: $"{REQUESTER_ID}#3", workingPath: WORKTREE + Path.DirectorySeparatorChar);

        var refusal = Decide(World(requester, [requester, sibling]));

        Assert.Equal(SiblingRefusals.WORKTREE_SHARED, refusal?.Label);
        Assert.Contains("ai-orchestrator-8", refusal?.Body);
    }

    /// <summary>A FINISHED JOB'S TREE MAY BE REUSED — nobody is working in it any more.</summary>
    [Fact]
    public void AClosedSiblingsWorktree_IsNotShared()
    {
        var requester = Session(REQUESTER_ID, endeavourId: REQUESTER_ID);
        var closed = Session("ai-orchestrator-8", endeavourId: REQUESTER_ID, bornFromHandover: $"{REQUESTER_ID}#3", workingPath: WORKTREE, closed: true);

        Assert.Null(Decide(World(requester, [requester, closed])));
    }

    /// <summary>
    /// ANYONE'S OPEN TREE IS TAKEN, not only a sibling's (fix round 1, 2026-09-23). A session of another
    /// endeavour of the same repo is no less a second writer on that checkout.
    /// </summary>
    [Fact]
    public void AnotherEndeavoursOpenSession_HoldingTheTree_IsShared()
    {
        var requester = Session(REQUESTER_ID, endeavourId: REQUESTER_ID);
        var stranger = Session("ai-orchestrator-3", endeavourId: "ai-orchestrator-2", bornFromHandover: "ai-orchestrator-2#9", workingPath: WORKTREE);

        var refusal = Decide(World(requester, [requester, stranger]));

        Assert.Equal(SiblingRefusals.WORKTREE_SHARED, refusal?.Label);
        Assert.Contains("ai-orchestrator-3", refusal?.Body);
    }

    /// <summary>An unrelated open solo started straight on that folder (its RepoPath) holds it just the same.</summary>
    [Fact]
    public void AnUnrelatedOpenSolo_RunningInTheTree_IsShared()
    {
        var requester = Session(REQUESTER_ID);
        var unrelated = Session("ai-orchestrator-4", repoPath: WORKTREE);

        var refusal = Decide(World(requester, [requester, unrelated]));

        Assert.Equal(SiblingRefusals.WORKTREE_SHARED, refusal?.Label);
        Assert.Contains("ai-orchestrator-4", refusal?.Body);
    }

    /// <summary>
    /// GIT'S MAIN CHECKOUT IS ALWAYS TAKEN — even when the requester itself runs in a LINKED worktree, so
    /// its RepoPath is not the main checkout and no session in the world names it.
    /// </summary>
    [Fact]
    public void GitsMainCheckout_IsShared_WhenTheRequesterRunsInALinkedWorktree()
    {
        var linked = Path.Combine(ROOT, "repo.worktrees", "main-work");
        var requester = Session(REQUESTER_ID, repoPath: linked);

        var refusal = Decide(World(requester, [requester], repoWorktrees: [REPO, linked, WORKTREE]), Request(worktree: REPO));

        Assert.Equal(SiblingRefusals.WORKTREE_SHARED, refusal?.Label);
        Assert.Contains("main checkout", refusal?.Body);
    }

    // ---------------------------------------------------------------- name-taken

    /// <summary>
    /// THE PARENT'S OWN NAME, on a FIRST birth (fix round 1): the requester is unlinked, so it is in no
    /// endeavour's member list — and a child named like its parent is two topics the owner cannot tell apart.
    /// </summary>
    [Fact]
    public void AnUnlinkedRequestersOwnName_IsTaken()
    {
        var requester = Session(REQUESTER_ID, displayName: NAME);

        var refusal = Decide(World(requester, [requester]));

        Assert.Equal(SiblingRefusals.NAME_TAKEN, refusal?.Label);
        Assert.Contains(REQUESTER_ID, refusal?.Body);
    }

    [Fact]
    public void AnOpenSiblingsName_IsTaken()
    {
        var requester = Session(REQUESTER_ID, endeavourId: REQUESTER_ID);
        var sibling = Session("ai-orchestrator-8", endeavourId: REQUESTER_ID, bornFromHandover: $"{REQUESTER_ID}#3", workingPath: OTHER_WORKTREE, displayName: NAME);

        var refusal = Decide(World(requester, [requester, sibling]));

        Assert.Equal(SiblingRefusals.NAME_TAKEN, refusal?.Label);
        Assert.Contains("ai-orchestrator-8", refusal?.Body);
    }

    // ---------------------------------------------------------------- the whole table

    [Fact]
    public void AValidRequest_HasNoRefusal()
    {
        var requester = Session(REQUESTER_ID);

        Assert.Null(Decide(World(requester, [requester])));
    }

    /// <summary>EVERY REFUSAL TELLS THE SOLO NOTHING CHANGED — the promote precedent, and the one line that stops a re-drop loop.</summary>
    [Fact]
    public void EveryRefusalToTheRequester_SaysTheOwnerWasNotAsked()
    {
        var requester = Session(REQUESTER_ID, supervisorSpawned: true);

        Assert.EndsWith("The owner has NOT been asked and nothing was changed.", Decide(World(requester, [requester]))?.Body);
        Assert.EndsWith("The owner has NOT been asked and nothing was changed.", Decide(World(Session(REQUESTER_ID), [Session(REQUESTER_ID)], worktreeExists: false))?.Body);
    }

    /// <summary>
    /// THE IDEMPOTENT ANSWER BEATS THE CAP (see the ordering note on the validator): a retry after a
    /// birth that filled the endeavour. Checked in the spec's table order it would read "at-cap — close
    /// a sibling", sending the solo to ask the owner to close the very session its request just started.
    /// </summary>
    [Fact]
    public void ARetryAfterTheBirthThatFilledTheCap_IsAlreadyUsed_NotAtCap()
    {
        var requester = Session(REQUESTER_ID, endeavourId: REQUESTER_ID);
        var bornFromThisRequest = Session("ai-orchestrator-8", endeavourId: REQUESTER_ID, bornFromHandover: $"{REQUESTER_ID}#14", workingPath: WORKTREE);

        var refusal = Decide(World(requester, [requester, bornFromThisRequest], maxOpenMembers: 2));

        Assert.Equal(SiblingRefusals.HANDOVER_ALREADY_USED, refusal?.Label);
        Assert.Contains("already started as ai-orchestrator-8", refusal?.Body);
    }

    [Fact]
    public void Format_HandoverKey_IsOrchHashIndex()
    {
        Assert.Equal("ai-orchestrator-7#14", SiblingRequest_Validator.Format_HandoverKey("ai-orchestrator-7", 14));
    }

    // ---------------------------------------------------------------- builders

    static (string Label, string Subject, string Body)? Decide(SiblingWorld world, ISpawnSiblingRequest? request = null)
    {
        return SiblingRequest_Validator.Decide_Refusal_OrNull(request ?? Request(), world);
    }

    static ISpawnSiblingRequest Request(
        string orchId = REQUESTER_ID,
        string? worktree = null,
        string? sourceFilePath = null)
    {
        return SpawnSiblingRequest_Factory.Create(
            orchId, NAME, "Rework the usage-limit pause", 14, worktree ?? WORKTREE, "two jobs the owner wants to steer separately",
            sourceFilePath ?? Path.Combine(ROOT, ".requests", "sibling-this.json"));
    }

    static IOrchestrationSession Session(
        string orchId,
        string? endeavourId = null,
        string? bornFromHandover = null,
        string? workingPath = null,
        string? displayName = null,
        bool closed = false,
        bool supervisorSpawned = false,
        string? repoPath = null)
    {
        return OrchestrationSession_Factory.Create(
            orchId, "AIOrchestrator", repoPath ?? REPO, CREATED, null, null,
            supervisorSpawned ? CREATED : null, null, displayName, null, null, [],
            TelegramDeliveryModes.Normal, closed ? CREATED.AddHours(1) : null,
            endeavourId: endeavourId,
            bornFromOrchId: bornFromHandover == null ? null : REQUESTER_ID,
            bornFromHandover: bornFromHandover,
            workingPath: workingPath);
    }

    static IChannelEntry Entry(int index, ChannelAuthors author, string subject)
    {
        return ChannelEntry_Factory.Create(index, author, "2026-09-23 10:00", subject, "body", $"## [{index}] FROM {author} — 2026-09-23 10:00 — {subject}\n\nbody");
    }

    static SiblingWorld World(
        IOrchestrationSession? requester,
        IReadOnlyList<IOrchestrationSession> sessions,
        IReadOnlyList<IChannelEntry>? history = null,
        IReadOnlyList<ISpawnSiblingRequest>? otherParked = null,
        IReadOnlyList<string>? repoWorktrees = null,
        bool worktreeExists = true,
        int maxOpenMembers = 3)
    {
        return new SiblingWorld(
            requester,
            sessions,
            history ?? [Entry(14, ChannelAuthors.Solo, "HANDOVER — the limits job")],
            otherParked ?? [],
            repoWorktrees ?? [REPO, WORKTREE],
            worktreeExists,
            maxOpenMembers);
    }
}
