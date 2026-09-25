using AIOrchestratorCoreLib.Bridge.Siblings;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.Siblings;

/// <summary>
/// THE LIFECYCLE OF A LINKED ORCHESTRATION (plan 2026-09-23 Task 14, spec §2.3, §7.5, §7.6), on the real
/// engine through <see cref="SiblingEngine_Harness"/>: a close tells the survivors and closes nothing else,
/// the tick drops the closed one from their lists, and a linked orchestration can be neither promoted nor
/// <c>/switch</c>ed.
///
/// <para>
/// THE SURVIVOR NOTICE IS PINNED UNDER BOTH <c>topic.onClose</c> VALUES. Plan 03 Task 10 split the close in
/// two — delete the topic, or close it and keep it — and the post-step sits after that split; a notice that
/// fired in one branch only would pass a test that ran the shipped default alone.
/// </para>
/// </summary>
public class SiblingLifecycleTests : IDisposable
{
    const string SURVIVOR_FRAGMENT = "stay on disk";

    readonly SiblingEngine_Harness _harness = new();

    public void Dispose()
    {
        _harness.Dispose();
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(null)]
    [InlineData("close")]
    public async Task ClosingOneSibling_TellsEachSurvivor_AsAnAgentEntry(string? onClose)
    {
        if (onClose != null)
            Write_Config($",\"topic\":{{\"onClose\":\"{onClose}\"}}");

        var (settings, limits, reports) = await Start_ThreeLinkedSolos_Async();
        File.WriteAllText(_harness.Paths.Get_PlanFile(limits), "- [x] the counter\n- [>] the reset window\n- [ ] the phone line\n");

        _harness.Engine.Close_Orchestration_ByOwner(limits, "done for today");

        foreach (var survivor in new[] { settings, reports })
        {
            var channel = _harness.Channel(survivor);

            // THE [agent] TAG IS THE AUDIENCE, and the discriminating assert: an Owner-audience entry is written
            // without it. The phone-side check below is kept as the owner's half of decision 15, but it is NOT
            // what pins the audience — under the shipped push setting a plain app entry on a solo's channel is
            // held either way (a mutant appending as Owner stayed green on it alone, 2026-09-24).
            Assert.Contains($"{AppEntryAudience_Tag.AGENT_TAG} sibling 'AI-Orch · limits work' closed — its outbox and PLAN.md stay on disk", channel, StringComparison.Ordinal);
            Assert.Contains("unfinished lines:\n[>] the reset window\n[ ] the phone line", channel.Replace("\r\n", "\n"), StringComparison.Ordinal);
            Assert.DoesNotContain("the counter", channel, StringComparison.Ordinal);
        }

        // The positive control: the close's own General line is Owner-audience and reaches the phone, so the
        // mirror demonstrably ran after the notices were written.
        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => _harness.Telegram.Count_Sent_Containing($"orchestration '{limits}' closed") >= 1, 20_000),
            $"the close's General line never reached the phone, so the notice's absence would prove nothing.{Environment.NewLine}{_harness.Telegram.Dump_Sent()}{Environment.NewLine}{_harness.Log.Dump()}");

        await Task.Delay(BridgeTestTiming.Window_ForTicks(3));

        Assert.Equal(0, _harness.Telegram.Count_Sent_Containing(SURVIVOR_FRAGMENT));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ClosingOneSibling_NeverClosesAnother()
    {
        var (settings, limits, reports) = await Start_ThreeLinkedSolos_Async();

        _harness.Engine.Close_Orchestration_ByOwner(limits, "done");

        Assert.NotNull(_harness.Read_Session(limits).ClosedUtc);

        foreach (var survivor in new[] { settings, reports })
        {
            var session = _harness.Read_Session(survivor);

            Assert.Null(session.ClosedUtc);
            Assert.Null(session.TelegramTopicDeletePendingUtc);
            Assert.NotNull(session.EndeavourId);
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ClosingOneSibling_DropsItFromTheSurvivorsList_NextTick()
    {
        var (settings, limits, reports) = await Start_ThreeLinkedSolos_Async();
        var list = _harness.Paths.Get_SiblingsListFile(settings);

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => Read_OrNull(list)?.Contains(limits, StringComparison.Ordinal) == true, 20_000),
            $"the tick never listed '{limits}' in '{list}', so its absence later would prove nothing.{Environment.NewLine}{_harness.Log.Dump()}");

        _harness.Engine.Close_Orchestration_ByOwner(limits, "done");

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => Read_OrNull(list) is { } text && !text.Contains(limits, StringComparison.Ordinal), 20_000),
            $"'{list}' still names the closed sibling:{Environment.NewLine}{Read_OrNull(list)}");

        Assert.Contains(reports, Read_OrNull(list) ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// A PAUSED SURVIVOR IS NOT WOKEN (final review I1, spec §7.2, CLAUDE.md PAUSE: "miss one and dormancy is
    /// a word"). The notice is a foreign append to its owner channel, which the solo's watcher fires on, so a
    /// paused sibling would wake and act on it. The unpaused survivor beside it is the positive control: the
    /// post-step ran, and skipped only the sleeper.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ClosingOneSibling_WhileASurvivorIsPaused_WakesOnlyTheOneAwake()
    {
        var (settings, limits, reports) = await Start_ThreeLinkedSolos_Async();
        _harness.Store.Set_Paused(reports, true);
        var pausedBefore = _harness.Channel(reports);

        _harness.Engine.Close_Orchestration_ByOwner(limits, "done");

        Assert.Contains(SURVIVOR_FRAGMENT, _harness.Channel(settings), StringComparison.Ordinal);
        Assert.Equal(pausedBefore, _harness.Channel(reports));
    }

    /// <summary>
    /// THE WEDGE A CRASH MID-BIRTH LEAVES IS REPAIRED BY THE RETRY (final review M2). The child exists and is
    /// linked — the launcher links it before it spawns — but the app died before the parent was stamped, so
    /// the retry is refused <c>handover-already-used</c>, correctly, and that refusal now also links the
    /// parent, exactly as the birth would have.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ARetryAfterACrashMidBirth_IsRefused_AndLinksTheParent()
    {
        var parent = await _harness.Start_Solo_Async("AI-Orch · settings work");
        var child = await _harness.Start_Solo_Async("AI-Orch · limits work");
        var handover = _harness.Append_Outbox(parent, $"{HandoverEntry_Detector.HANDOVER_MARKER} limits");
        _harness.Store.Set_SiblingLink(child, parent, parent, $"{parent}#{handover}", _harness.WorktreePath);

        Assert.Null(_harness.Read_Session(parent).EndeavourId);

        _harness.Drop_Request(System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["action"] = OrchestrationRequests_Reader.SPAWN_SIBLING_ACTION,
            ["orchId"] = parent,
            ["name"] = "AI-Orch · limits work",
            ["job"] = "Rework the usage-limit pause",
            ["handover"] = handover,
            ["worktree"] = _harness.WorktreePath,
            ["reason"] = "the retry after a crash",
        }));

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => _harness.Archived_Names().Any(name => name.StartsWith($"{SiblingRefusals.HANDOVER_ALREADY_USED}-", StringComparison.Ordinal)), 20_000),
            $"the retry was never refused as {SiblingRefusals.HANDOVER_ALREADY_USED}: [{string.Join(", ", _harness.Archived_Names())}]{Environment.NewLine}{_harness.Log.Dump()}");

        Assert.Equal(parent, _harness.Read_Session(parent).EndeavourId);
        Assert.Contains($"linked now to endeavour '{parent}'", _harness.Log.Dump(), StringComparison.Ordinal);
    }

    /// <summary>§7.5: the owner may close anything — open ledger lines are reported, never a reason to refuse.</summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ClosingASiblingWithOpenLines_IsNotRefused()
    {
        var (settings, limits, _) = await Start_ThreeLinkedSolos_Async();
        File.WriteAllText(_harness.Paths.Get_PlanFile(limits), "- [ ] nothing done yet\n- [!] blocked on the API\n");

        _harness.Engine.Close_Orchestration_ByOwner(limits, "the owner changed their mind");

        Assert.NotNull(_harness.Read_Session(limits).ClosedUtc);
        Assert.True(
            _harness.Channel(settings).Contains("[ ] nothing done yet", StringComparison.Ordinal),
            $"the survivor was not told the closed sibling's open lines.{Environment.NewLine}{_harness.Channel(settings)}{Environment.NewLine}{_harness.Log.Dump()}");
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ClosingTheLastSibling_LeavesNoArtefacts()
    {
        var bystander = await _harness.Start_Solo_Async("AI-Orch · unrelated work");
        var (settings, limits) = await Start_TwoLinkedSolos_Async();

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => File.Exists(_harness.Paths.Get_SiblingsListFile(settings)), 20_000),
            "the tick never wrote .siblings for a linked pair, so its absence later would prove nothing.");

        _harness.Engine.Close_Orchestration_ByOwner(limits, "done");
        _harness.Engine.Close_Orchestration_ByOwner(settings, "done");

        string[] Artefacts() => [.. new[] { bystander, settings, limits }
            .SelectMany(orchId => new[] { _harness.Paths.Get_SiblingsListFile(orchId), _harness.Paths.Get_EndeavourDigestFile(orchId) })
            .Where(File.Exists)];

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => Artefacts().Length == 0, 20_000),
            $"artefacts left behind: {string.Join(", ", Artefacts())}");

        // The last sibling has nobody to tell: the only notice is the one the first close gave the second.
        Assert.DoesNotContain(SURVIVOR_FRAGMENT, _harness.Channel(limits), StringComparison.Ordinal);

        Assert.DoesNotContain("🔗", AIOrchestratorCoreLib.Telegram.ProgressReport_Builder.Build_OpenOrchestrationsText(_harness.Paths, _harness.Read_Sessions()), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task APromoteRequestFromALinkedSolo_IsRefusedLinkedOrchestration()
    {
        var (settings, _) = await Start_TwoLinkedSolos_Async();

        // A HANDOVER is filed, so the only refusal left for the request to meet is the one this test is about.
        _harness.Append_Solo(settings, $"{HandoverEntry_Detector.HANDOVER_MARKER} — where the work stands", "All of it.");

        _harness.Drop_Request($$"""{"action":"promote-orchestration","orchId":"{{settings}}","reason":"it needs a review gate"}""");

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => _harness.Archived_Names().Any(name => name.StartsWith($"{SiblingRefusals.LINKED_ORCHESTRATION}-", StringComparison.Ordinal)), 20_000),
            $"the request was never archived as {SiblingRefusals.LINKED_ORCHESTRATION}: [{string.Join(", ", _harness.Archived_Names())}]{Environment.NewLine}{_harness.Log.Dump()}");

        Assert.Contains(SiblingNotice_Wording.Describe_LinkedPromoteRefusal().Subject, _harness.Channel(settings), StringComparison.Ordinal);
        Assert.Empty(CloseConfirmation_Parking.Find_Parked(_harness.Paths));
        Assert.Null(_harness.Read_Session(settings).SupervisorSpawnedUtc);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task SwitchInALinkedTopic_RepliesAndChangesNothing()
    {
        var (settings, _) = await Start_TwoLinkedSolos_Async();
        _harness.Append_Solo(settings, $"{HandoverEntry_Detector.HANDOVER_MARKER} — where the work stands", "All of it.");
        var topic = _harness.TopicOf(settings);

        // TWICE: with a HANDOVER filed, an unlinked topic would arm on the first and PROMOTE on the second.
        await Type_Switch_Async(topic, 9301);
        await Type_Switch_Async(topic, 9302);

        Assert.Equal(2, _harness.Telegram.Sent_InTopic(topic).Count(text => text == SiblingNotice_Wording.Describe_LinkedSwitchRefusal()));
        // FINAL REVIEW M1: the §7.6 sentence said "close them", and closing them changes nothing — v1 never
        // unlinks, so the survivor's /switch answered it for ever. The reply now says what is true.
        Assert.Equal(
            "this topic is linked to siblings, and siblings stay linked for the life of the endeavour — close this one or keep working.",
            SiblingNotice_Wording.Describe_LinkedSwitchRefusal());
        Assert.DoesNotContain(_harness.Telegram.Sent_InTopic(topic), text => text.Contains("FULL CREW", StringComparison.Ordinal));
        Assert.Null(_harness.Read_Session(settings).SupervisorSpawnedUtc);
    }

    /// <summary>The guard is for LINKED topics only: an ordinary solo still reaches today's promote path (its arming reply).</summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task SwitchInAnUnlinkedTopic_StillWorks()
    {
        var solo = await _harness.Start_Solo_Async("AI-Orch · alone work");
        _harness.Append_Solo(solo, $"{HandoverEntry_Detector.HANDOVER_MARKER} — where the work stands", "All of it.");
        var topic = _harness.TopicOf(solo);

        await Type_Switch_Async(topic, 9401);

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => _harness.Telegram.Sent_InTopic(topic).Any(text => text.Contains("FULL CREW", StringComparison.Ordinal)), 20_000),
            $"/switch in an unlinked topic did not reach the promote path.{Environment.NewLine}{_harness.Telegram.Dump_Sent()}{Environment.NewLine}{_harness.Log.Dump()}");

        Assert.DoesNotContain(_harness.Telegram.Sent_InTopic(topic), text => text == SiblingNotice_Wording.Describe_LinkedSwitchRefusal());
    }

    /// <summary>Types <c>/switch</c> in <paramref name="topic"/> and waits for the one reply it gets there.</summary>
    async Task Type_Switch_Async(long? topic, int updateId)
    {
        var before = _harness.Telegram.Sent_InTopic(topic).Count;

        _harness.Telegram.Queue_Updates(
            $"{{\"ok\":true,\"result\":[{{\"update_id\":{updateId},\"message\":{{\"message_id\":{updateId},"
            + $"\"message_thread_id\":{topic},\"from\":{{\"id\":{SiblingEngine_Harness.OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SiblingEngine_Harness.SUPERGROUP_CHAT_ID}}},\"text\":\"/switch\"}}}}]}}");

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => _harness.Telegram.Sent_InTopic(topic).Count > before, 20_000),
            $"/switch got no reply.{Environment.NewLine}{_harness.Telegram.Dump_Sent()}{Environment.NewLine}{_harness.Log.Dump()}");
    }

    async Task<(string Settings, string Limits)> Start_TwoLinkedSolos_Async()
    {
        var settings = await _harness.Start_Solo_Async("AI-Orch · settings work");
        var limits = await _harness.Start_Solo_Async("AI-Orch · limits work");

        _harness.Store.Set_SiblingLink(settings, settings, settings, $"{settings}#1", _harness.WorktreePath);
        _harness.Store.Set_SiblingLink(limits, settings, settings, $"{settings}#1", _harness.WorktreePath);

        return (settings, limits);
    }

    async Task<(string Settings, string Limits, string Reports)> Start_ThreeLinkedSolos_Async()
    {
        var (settings, limits) = await Start_TwoLinkedSolos_Async();
        var reports = await _harness.Start_Solo_Async("AI-Orch · reports work");

        _harness.Store.Set_SiblingLink(reports, settings, settings, $"{settings}#2", _harness.WorktreePath);

        return (settings, limits, reports);
    }

    /// <summary>The harness's config, as it writes it, plus <paramref name="extra"/> — the provider re-reads it on its write stamp.</summary>
    void Write_Config(string extra)
    {
        File.WriteAllText(
            _harness.Paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SiblingEngine_Harness.SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{SiblingEngine_Harness.OWNER_USER_ID}{extra}}}");
    }

    /// <summary>Null while the tick is rewriting the file — "unknown", never read as "it names nobody".</summary>
    static string? Read_OrNull(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
