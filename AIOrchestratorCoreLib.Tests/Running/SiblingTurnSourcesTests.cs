using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.StatePack;
using AIOrchestratorCoreLib.Running.TurnCursor;
using AIOrchestratorCoreLib.Running.TurnSource;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// A BRIDGE-DRIVEN SOLO IS WOKEN BY ITS SIBLINGS' OUTBOXES (spec 2026-09-23 §5.4, plan Task 13): one
/// <see cref="TurnSourceKinds.Sibling"/> source per open sibling, on which — and only on which — another
/// solo's entry is inbound for a solo. Everything here is a function of session.json and the channel files:
/// a store and a temp tree, no dispatcher and no process. The dispatcher-level pins (the reply never lands
/// in a sibling's outbox, the cursor survives a turn taken while paused) are in
/// <see cref="SiblingPrintTurnTests"/>.
/// </summary>
public class SiblingTurnSourcesTests : IDisposable
{
    const string ENDEAVOUR = "aiorchestrator-1";
    const string FIRST = "aiorchestrator-1";
    const string SECOND = "aiorchestrator-2";
    const string THIRD = "aiorchestrator-3";
    const string SOLO_ID = "solo-1";

    readonly EndeavourTree _tree = new();

    public void Dispose()
    {
        _tree.Dispose();
    }

    [Fact]
    public void ALinkedSolo_IsWokenByEachOpenSiblingsOutbox()
    {
        Link_Three();

        var sources = TurnSources_Resolver.Resolve(_tree.Paths, _tree.Store, SessionRoles.Solo, FIRST, SOLO_ID);

        Assert.Equal(["owner", $"{TurnSource_Factory.SIBLING_KEY_PREFIX}{SECOND}", $"{TurnSource_Factory.SIBLING_KEY_PREFIX}{THIRD}"], sources.Select(source => source.Key));
        Assert.Equal([TurnSourceKinds.Owner, TurnSourceKinds.Sibling, TurnSourceKinds.Sibling], sources.Select(source => source.Kind));
        Assert.Equal(_tree.Paths.Get_OwnerChannelFile(FIRST), sources[0].ChannelFilePath);
        Assert.Equal(_tree.Paths.Get_SiblingOutboxFile(SECOND), sources[1].ChannelFilePath);
        Assert.Equal(_tree.Paths.Get_SiblingOutboxFile(THIRD), sources[2].ChannelFilePath);

        // IsOwnerChannel keeps meaning what it meant, so none of its callers moves.
        Assert.Equal([true, false, false], sources.Select(source => source.IsOwnerChannel));
    }

    [Fact]
    public void AnUnlinkedSolo_HasOnlyItsOwnSource()
    {
        _tree.Add_Solo(FIRST, "AI-Orch · settings work", endeavourId: null);
        _tree.Add_Solo(SECOND, "AI-Orch · limits rework", endeavourId: null);

        var source = Assert.Single(TurnSources_Resolver.Resolve(_tree.Paths, _tree.Store, SessionRoles.Solo, FIRST, SOLO_ID));

        Assert.Equal(TurnSourceKinds.Owner, source.Kind);
        Assert.Equal(_tree.Paths.Get_OwnerChannelFile(FIRST), source.ChannelFilePath);
    }

    /// <summary>
    /// THE PAUSE GATE LIVES HERE (§5.4, the CLAUDE.md PAUSE bullet's waker list): a paused solo resolves no
    /// sibling source, so no sibling entry starts its turn. Its cursor survives — the dispatcher never drops
    /// a cursor for a source that merely did not resolve — so the backlog arrives in one turn on unpause.
    /// </summary>
    [Fact]
    public void APausedLinkedSolo_ResolvesNoSiblingSources()
    {
        Link_Three();
        _tree.Store.Set_Paused(FIRST, true);

        var source = Assert.Single(TurnSources_Resolver.Resolve(_tree.Paths, _tree.Store, SessionRoles.Solo, FIRST, SOLO_ID));

        Assert.Equal(TurnSourceKinds.Owner, source.Kind);

        // A PAUSED SIBLING is still a source for a live one: pause is about waking the paused session, and
        // nothing a paused sibling does can write to its outbox anyway.
        _tree.Store.Set_Paused(FIRST, false);
        _tree.Store.Set_Paused(SECOND, true);

        Assert.Equal(3, TurnSources_Resolver.Resolve(_tree.Paths, _tree.Store, SessionRoles.Solo, FIRST, SOLO_ID).Count);
    }

    [Fact]
    public void AClosedSibling_IsNotASource()
    {
        Link_Three();
        _tree.Store.Close_Orchestration(THIRD);

        var sources = TurnSources_Resolver.Resolve(_tree.Paths, _tree.Store, SessionRoles.Solo, FIRST, SOLO_ID);

        Assert.Equal(["owner", $"{TurnSource_Factory.SIBLING_KEY_PREFIX}{SECOND}"], sources.Select(source => source.Key));

        // And a closed solo is woken by nothing but its own channel, whoever is still open beside it.
        _tree.Store.Close_Orchestration(FIRST);

        Assert.Single(TurnSources_Resolver.Resolve(_tree.Paths, _tree.Store, SessionRoles.Solo, FIRST, SOLO_ID));
    }

    [Fact]
    public void OnASiblingSource_ASoloEntryIsInbound()
    {
        Assert.True(PrintTurn_Trigger.Is_Inbound(SessionRoles.Solo, TurnSourceKinds.Sibling, ChannelAuthors.Solo));
    }

    /// <summary>"A turn started by the record of the previous turn would be a loop with one member in it."</summary>
    [Fact]
    public void OnTheOwnerSource_ASoloEntryIsStillNotInbound()
    {
        Assert.False(PrintTurn_Trigger.Is_Inbound(SessionRoles.Solo, TurnSourceKinds.Owner, ChannelAuthors.Solo));
        Assert.False(PrintTurn_Trigger.Is_Inbound(SessionRoles.Solo, ChannelAuthors.Solo));
        Assert.True(PrintTurn_Trigger.Is_Inbound(SessionRoles.Solo, TurnSourceKinds.Owner, ChannelAuthors.Owner));
    }

    /// <summary>Nobody but a sibling writes an outbox; anything else found there is not a wake.</summary>
    [Theory]
    [InlineData(ChannelAuthors.App)]
    [InlineData(ChannelAuthors.Owner)]
    [InlineData(ChannelAuthors.Supervisor)]
    [InlineData(ChannelAuthors.Implementer)]
    public void OnASiblingSource_AnAppOrOwnerEntryIsNotInbound(ChannelAuthors author)
    {
        Assert.False(PrintTurn_Trigger.Is_Inbound(SessionRoles.Solo, TurnSourceKinds.Sibling, author));
    }

    /// <summary>
    /// A CHILD'S FIRST SIGHT OF ITS PARENT'S OUTBOX IS A BASELINE: the HANDOVER entry is already there when
    /// the child registers, and the child reads its brief because the birth note names it. Woken by it a
    /// second time, it would start the job twice.
    /// </summary>
    [Fact]
    public void TheBaselineOfASiblingSource_AbsorbsItsExistingSoloEntries()
    {
        Link_Three();
        var outbox = _tree.Paths.Get_SiblingOutboxFile(SECOND);
        _tree.Append_Entry(outbox, ChannelAuthors.Solo, "HANDOVER — limits rework", "the job");

        var source = TurnSource_Factory.Create_Sibling(SECOND, outbox);
        var entries = ChannelEntry_Parser.Parse_All(File.ReadAllText(outbox));

        var cursor = TurnCursor_Factory.Create_Baseline(source, SessionRoles.Solo, entries);

        Assert.Contains(ChannelEntry_Digest.Compute(Assert.Single(entries)), cursor.Delivered);
        Assert.Empty(PrintTurn_Trigger.Select_Pending(SessionRoles.Solo, TurnSourceKinds.Sibling, entries, cursor));
    }

    /// <summary>
    /// RULING D: the cursor after a turn is pruned by the SOURCE'S kind. Pruned as an owner channel, a
    /// sibling's outbox holds no inbound entry at all, so the prune would take its "the read came back
    /// empty" branch on every turn and the set would never shed an identity compaction had archived.
    /// </summary>
    [Fact]
    public void AnAdvancedSiblingCursor_KeepsItsDeliveredIdentities_AndShedsTheArchivedOnes()
    {
        var outbox = _tree.Paths.Get_SiblingOutboxFile(SECOND);
        Directory.CreateDirectory(Path.GetDirectoryName(outbox)!);
        _tree.Append_Entry(outbox, ChannelAuthors.Solo, "CLAIM — parser", "mine");
        _tree.Append_Entry(outbox, ChannelAuthors.Solo, "FYI — tests", "green");
        var entries = ChannelEntry_Parser.Parse_All(File.ReadAllText(outbox));

        var archivedIdentity = "an-identity-compaction-moved-out";
        var before = TurnCursor_Factory.Create($"{TurnSource_Factory.SIBLING_KEY_PREFIX}{SECOND}", outbox, 1, new HashSet<string> { archivedIdentity, ChannelEntry_Digest.Compute(entries[0]) });

        var after = TurnCursor_Factory.CreateFrom_Delivered(before, SessionRoles.Solo, TurnSourceKinds.Sibling, entries, [entries[1]]);

        Assert.Empty(PrintTurn_Trigger.Select_Pending(SessionRoles.Solo, TurnSourceKinds.Sibling, entries, after));
        Assert.DoesNotContain(archivedIdentity, after.Delivered);
        Assert.Equal(2, after.Delivered.Count);
    }

    /// <summary>
    /// A SIBLING'S OUTBOX IS READ, NEVER WRITTEN, by anyone but that sibling. Offered as a reply target, a
    /// <c>TO: sibling:…</c> block would land in ANOTHER solo's outbox under the author word <c>solo</c> — read
    /// by every other sibling as that sibling's words, and read by the writer itself as inbound traffic on
    /// its own sibling source: a one-member wake loop. So the contract names only the channels a reply may
    /// go to, and a solo with sibling sources is still told the single-channel contract.
    /// </summary>
    [Fact]
    public void ASiblingSource_IsNeverOfferedAsAReplyTarget()
    {
        var own = TurnSource_Factory.Create_Owner(_tree.Paths.Get_OwnerChannelFile(FIRST));
        var sibling = TurnSource_Factory.Create_Sibling(SECOND, _tree.Paths.Get_SiblingOutboxFile(SECOND));

        Assert.Equal([own], TurnSources_Resolver.Select_ReplyTargets([own, sibling]));

        var contract = PrintTurnPrompt_Builder.Describe_Contract([own, sibling]);

        Assert.DoesNotContain("TO:", contract, StringComparison.Ordinal);
        Assert.StartsWith(PrintTurnPrompt_Builder.Describe_Contract([own]), contract, StringComparison.Ordinal);
        Assert.Contains("outbox", contract, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE SUPERVISOR'S DIGEST DOES NOT HOLD A SIBLING. It batches a crew's ordinary reports into one
    /// supervisor turn, and its own comment says it is confined to that "without a role test anywhere"
    /// because nobody else had a member author inbound. A solo on a sibling source now does, and a terminal
    /// solo's watcher fires on a sibling's append at once (§5.2, §5.3); held for the window, a CLAIM would
    /// arrive minutes after the file it claims was already being edited.
    /// </summary>
    [Fact]
    public void ASiblingsOrdinaryEntry_IsNotHeldForTheSupervisorsDigest()
    {
        var sibling = TurnSource_Factory.Create_Sibling(SECOND, _tree.Paths.Get_SiblingOutboxFile(SECOND));
        var spoke = TurnSource_Factory.Create_Spoke("imp-1", _tree.Paths.Get_ImplementerChannelFile(FIRST, "imp-1"));
        var now = DateTime.Now;

        Assert.NotNull(WakeUp_Policy.Resolve_WakeReason_OrNull([new PendingEntry(sibling, Entry(7, ChannelAuthors.Solo, "FYI — tests"))], [], now, now, TimeSpan.FromMinutes(5)));

        // The supervisor's own case is untouched: a member's ordinary report on a spoke is still held.
        Assert.Null(WakeUp_Policy.Resolve_WakeReason_OrNull([new PendingEntry(spoke, Entry(7, ChannelAuthors.Implementer, "REPORT — parser"))], [], now, now, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void ThePack_CarriesTheDigest_WhenTheFileExists()
    {
        Link_Three();
        const string DIGEST = "# Your open siblings\n\n## AI-Orch · limits rework (aiorchestrator-2) · /trees · branch ? · live\nno task ledger yet\n";
        File.WriteAllText(_tree.Paths.Get_EndeavourDigestFile(FIRST), DIGEST);

        var inputs = Read_Pack(FIRST);

        Assert.Equal(DIGEST, inputs.EndeavourDigest);
        Assert.Contains($"{StatePack_Builder.ENDEAVOUR_HEADING}\n\n{DIGEST}", StatePack_Builder.Build(inputs), StringComparison.Ordinal);
    }

    [Fact]
    public void ThePack_OfAnUnlinkedSolo_HasNoEndeavourSection()
    {
        _tree.Add_Solo(FIRST, "AI-Orch · settings work", endeavourId: null);

        var inputs = Read_Pack(FIRST);

        Assert.Null(inputs.EndeavourDigest);
        Assert.DoesNotContain(StatePack_Builder.ENDEAVOUR_HEADING, StatePack_Builder.Build(inputs), StringComparison.Ordinal);
        Assert.DoesNotContain(inputs.Unavailable, line => line.Contains("ENDEAVOUR", StringComparison.Ordinal));
    }

    /// <summary>A digest that exists and cannot be read is NAMED, never passed off as "no endeavour".</summary>
    [RequiresExclusiveOpenEnforcementFact]
    public void AnUnreadableDigest_IsNamedAsUnavailable()
    {
        Link_Three();
        var digest = _tree.Paths.Get_EndeavourDigestFile(FIRST);
        File.WriteAllText(digest, "# Your open siblings\n");

        StatePackInputs inputs;

        using (new FileStream(digest, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            inputs = Read_Pack(FIRST);

        Assert.Null(inputs.EndeavourDigest);
        Assert.Contains(inputs.Unavailable, line => line.StartsWith("ENDEAVOUR.md:", StringComparison.Ordinal));
    }

    void Link_Three()
    {
        _tree.Add_Solo(FIRST, "AI-Orch · settings work", ENDEAVOUR);
        _tree.Add_Solo(SECOND, "AI-Orch · limits rework", ENDEAVOUR);
        _tree.Add_Solo(THIRD, "AI-Orch · docs pass", ENDEAVOUR);
    }

    StatePackInputs Read_Pack(string orchId)
    {
        var owner = _tree.Paths.Get_OwnerChannelFile(orchId);
        var state = PrintSessionState_Factory.Create_New("sid", SessionRoles.Solo, orchId, SOLO_ID, Path.Combine(_tree.RepoPath, "not-a-repo"), null, owner, []);
        var sources = TurnSources_Resolver.Resolve(_tree.Paths, _tree.Store, SessionRoles.Solo, orchId, SOLO_ID);

        return StatePackInputs_Reader.Read(_tree.Paths, state, $"{orchId}/{SOLO_ID}/1", [], sources);
    }

    static IChannelEntry Entry(int index, ChannelAuthors author, string subject)
    {
        var word = ChannelAuthor_Words.Get_Word(author);
        return ChannelEntry_Factory.Create(index, author, "2026-09-23 10:00", subject, "body", $"## [{index}] FROM {word} — 2026-09-23 10:00 — {subject}\n\nbody");
    }
}
