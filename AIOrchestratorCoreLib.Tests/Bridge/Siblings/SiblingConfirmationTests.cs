using System.Globalization;
using System.Text.Json;
using AIOrchestratorCoreLib.Bridge.Siblings;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.Spawning;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.Siblings;

/// <summary>
/// THE OWNER'S TAP (O1) AND THE BIRTH IT AUTHORISES, driven through the real engine (plan 2026-09-23
/// Task 9, spec §4.2 "checked at the tap", §4.3, §4.4, §7.3).
///
/// <para>
/// THE HAZARD THIS CLASS EXISTS FOR: the sibling tap runs through the shared close/promote confirmation
/// machinery, where the arm that is not named is an orchestration CLOSE in every earlier version of
/// that code. A "✅ Start it" that fell through would end the session that asked for help. So the
/// confirmed-tap tests assert the positive (one child, started) AND the negative (the requester still
/// open) — a test that only checked "not closed" would stay green with the execute arm deleted, because
/// the unknown-kind arm also closes nothing.
/// </para>
/// </summary>
public class SiblingConfirmationTests
{
    const string PARENT_NAME = "AI-Orch · settings work";
    const string SIBLING_NAME = "AI-Orch · limits rework";
    const string JOB = "Rework the usage-limit pause so a restored pause can be lifted per window";
    const string REASON = "two jobs the owner wants to steer separately; they touch disjoint files";
    const int WAIT_MILLISECONDS = 20_000;
    const int PAUSE_WAIT_MILLISECONDS = 90_000;

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AParkedRequest_PromptsInTheRequestersTopic_WithTheTwoButtons()
    {
        using var harness = new SiblingEngine_Harness();
        var (solo, _, _) = await Arrange_Asked_Async(harness);

        Assert.NotNull(harness.Telegram.Find_ButtonFor("Start it"));
        Assert.NotNull(harness.Telegram.Find_ButtonFor("Keep one session"));

        var prompt = harness.Telegram.Sent_InTopic(harness.TopicOf(solo)).Last(text => text.Contains(SIBLING_NAME, StringComparison.Ordinal));
        Assert.Contains(PARENT_NAME, prompt);
        Assert.Contains(JOB, prompt);
        Assert.Contains(REASON, prompt);
    }

    /// <summary>
    /// THE LINK EXISTS AT THE FIRST SPAWN: asserted on what the spawner was HANDED (the worktree and the
    /// window title), not on session.json afterwards — a field stamped after the spawn would be on disk
    /// and absent from the process.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TapYes_StartsOneChild_LinkedAndNamed_BeforeItsFirstSpawn()
    {
        using var harness = new SiblingEngine_Harness();
        var (solo, handover, _) = await Arrange_Asked_Async(harness);
        harness.Store.Set_ImplementerModelOverride(solo, "sonnet");
        var spawnsBefore = harness.Spawner.SpawnedCommands.Count;

        await harness.Tap_Async("Start it");
        var child = await Wait_ForChild_Async(harness, solo);

        Assert.Single(Children_Of(harness, solo));
        Assert.Equal(solo, child.EndeavourId);
        Assert.Equal(solo, child.BornFromOrchId);
        Assert.Equal(SiblingRequest_Validator.Format_HandoverKey(solo, handover), child.BornFromHandover);
        Assert.Equal(harness.WorktreePath, child.WorkingPath);
        Assert.Equal(SIBLING_NAME, child.DisplayName);
        Assert.Equal("sonnet", child.ImplementerModelOverride);
        Assert.Equal(solo, harness.Store.Get_Session(solo).EndeavourId);

        var spawn = harness.Spawner.SpawnedCommands.Skip(spawnsBefore).Single(command => command.WorkingDirectory == harness.WorktreePath);
        Assert.Equal(
            SessionWindowTitle_Builder.Build_Title(SessionWindowTitle_Builder.Build_ForMember("solo-1", child.OrchId), SIBLING_NAME),
            RecordingSpawner_Fake.Read_Argument_After(spawn, "--title"));
    }

    /// <summary>
    /// AFTER THE LAUNCH, NEVER BEFORE (Process_StartRequests' ordering rule, §4.3 step 4): a
    /// bridge-driven session baselines at registration, so an entry written first would be history
    /// it never answers. The birth note comes first, then the job as FROM owner.
    ///
    /// <para>
    /// AND THE NOTE REACHES THE PHONE (Task 7 carry). The tailer takes a channel it has never seen at its
    /// current length, so a birth note appended before the child's first poll would be absorbed as
    /// history — present in the file, never in the new topic. So the assertion is on Telegram: the job
    /// text arrives in the CHILD's topic, and in no other.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TapYes_WritesTheBirthNoteThenTheJob_AfterTheLaunch()
    {
        using var harness = new SiblingEngine_Harness();
        var (solo, _, _) = await Arrange_Asked_Async(harness);

        await harness.Tap_Async("Start it");
        var child = await Wait_ForChild_Async(harness, solo);

        var entries = ChannelEntry_Parser.Parse_All(harness.Channel(child.OrchId));
        var note = entries.FirstOrDefault(entry => entry.Author == ChannelAuthors.App && entry.Subject.Contains("🔗 Sibling of", StringComparison.Ordinal));
        Assert.True(note != null, $"no birth note in the child's channel:{Environment.NewLine}{harness.Channel(child.OrchId)}");
        Assert.False(AppEntryAudience_Tag.Is_AgentTagged(note!.Subject), "the birth note is the owner's first message in the new topic — Owner audience");
        Assert.Contains(PARENT_NAME, note.Subject);

        var job = entries.Single(entry => entry.Author == ChannelAuthors.Owner);
        Assert.Equal(JOB, job.Body.Trim());
        Assert.True(job.Index > note.Index, "the job must come AFTER the birth note");

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(
                () => harness.TopicOf(child.OrchId) != null
                    && harness.Telegram.Sent_InTopic(harness.TopicOf(child.OrchId)).Any(text => text.Contains(JOB.TrimEnd('.'), StringComparison.Ordinal)),
                WAIT_MILLISECONDS),
            $"the birth note never reached the child's topic — absorbed as history?{Environment.NewLine}{harness.Telegram.Dump_Sent()}{Environment.NewLine}{harness.Log.Dump()}");

        Assert.NotEqual(harness.TopicOf(solo), harness.TopicOf(child.OrchId));
    }

    /// <summary>The child is in owner debt: its first act is an OWNER REQUESTS row.</summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TapYes_RaisesTheOwnerWaitOnTheChild()
    {
        using var harness = new SiblingEngine_Harness();
        var (solo, _, _) = await Arrange_Asked_Async(harness);

        await harness.Tap_Async("Start it");
        var child = await Wait_ForChild_Async(harness, solo);

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => harness.EngineState.Load_OrEmpty().OwnerAwaitingAnswer.Contains(child.OrchId), WAIT_MILLISECONDS),
            $"the owner's wait was never raised on '{child.OrchId}'.{Environment.NewLine}{harness.Log.Dump()}");
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TapYes_TellsTheParent_AndGeneral()
    {
        using var harness = new SiblingEngine_Harness();
        var (solo, _, _) = await Arrange_Asked_Async(harness);

        await harness.Tap_Async("Start it");
        var child = await Wait_ForChild_Async(harness, solo);

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => Find_AppEntry_OrNull(harness.Paths.Get_OwnerChannelFile(solo), $"sibling '{child.OrchId}' started") != null, WAIT_MILLISECONDS),
            $"the parent was never told:{Environment.NewLine}{harness.Channel(solo)}");

        var parentLine = Find_AppEntry_OrNull(harness.Paths.Get_OwnerChannelFile(solo), $"sibling '{child.OrchId}' started")!;
        Assert.False(AppEntryAudience_Tag.Is_AgentTagged(parentLine.Subject), "the parent's 'started' line is what the owner tapped for — Owner audience");
        Assert.Contains(SIBLING_NAME, parentLine.Subject);

        var general = Find_AppEntry_OrNull(harness.Paths.GeneralChannelFile, $"orchestration '{child.OrchId}' started");
        Assert.True(general != null, $"General was never told:{Environment.NewLine}{Read_OrEmpty(harness.Paths.GeneralChannelFile)}");
        Assert.True(AppEntryAudience_Tag.Is_AgentTagged(general!.Subject), "the owner already has both other lines on the phone — General's is Agent (decision 15)");
    }

    /// <summary>"✅ Started" ONLY WHEN THE BIRTH RAN (ruling E) — archived and edited on the prompt.</summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TapYes_ArchivesAsStarted_AndThePromptSaysStarted()
    {
        using var harness = new SiblingEngine_Harness();
        var (solo, _, _) = await Arrange_Asked_Async(harness);

        await harness.Tap_Async("Start it");
        await Wait_ForChild_Async(harness, solo);

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => Has_Archived(harness, "started"), WAIT_MILLISECONDS),
            $"never archived as started. Archived: [{string.Join(", ", harness.Archived_Names())}]{Environment.NewLine}{harness.Log.Dump()}");

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => harness.Telegram.TextEdits.Any(edit => edit.Text.Contains("✅ Started", StringComparison.Ordinal)), WAIT_MILLISECONDS),
            $"the prompt was never edited to Started:{Environment.NewLine}{string.Join(Environment.NewLine, harness.Telegram.TextEdits.Select(edit => edit.Text))}");

        Assert.Empty(CloseConfirmation_Parking.Find_Parked(harness.Paths));
    }

    /// <summary>
    /// THE MUTATION TARGET (spec §11). A Sibling-kind file must never reach Execute_Close. Both halves are
    /// asserted, because each catches a different mutant: deleting the Sibling arm drops the tap into the
    /// unknown-kind arm (nothing started — the child assertion reddens), and routing it into the close
    /// arm ends the requester (the ClosedUtc assertion reddens).
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AConfirmedSibling_NeverClosesTheRequester()
    {
        using var harness = new SiblingEngine_Harness();
        var (solo, _, _) = await Arrange_Asked_Async(harness);

        await harness.Tap_Async("Start it");

        var started = await SiblingEngine_Harness.Wait_Until_Async(() => Children_Of(harness, solo).Count == 1, WAIT_MILLISECONDS);

        // Settle: whatever the tap was going to do has been archived by now.
        await SiblingEngine_Harness.Wait_Until_Async(() => harness.Archived_Names().Count > 0, WAIT_MILLISECONDS);

        Assert.Null(harness.Store.Get_Session(solo).ClosedUtc);
        Assert.DoesNotContain(harness.Telegram.TextEdits, edit => edit.Text.Contains("Closed", StringComparison.Ordinal));
        Assert.False(Find_AppEntry_OrNull(harness.Paths.GeneralChannelFile, $"orchestration '{solo}' closed") != null, "the requester was closed");
        Assert.True(started, $"the confirmed sibling tap started nothing. Archived: [{string.Join(", ", harness.Archived_Names())}]{Environment.NewLine}{harness.Log.Dump()}");
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TapNo_TellsTheRequester_AndStartsNothing()
    {
        using var harness = new SiblingEngine_Harness();
        var (solo, _, _) = await Arrange_Asked_Async(harness);
        var sessionsBefore = harness.Store.Load_All().Count;

        await harness.Tap_Async("Keep one session");

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => Has_Archived(harness, "declined"), WAIT_MILLISECONDS),
            $"never archived as declined. Archived: [{string.Join(", ", harness.Archived_Names())}]{Environment.NewLine}{harness.Log.Dump()}");

        var declined = Find_AppEntry_OrNull(harness.Paths.Get_OwnerChannelFile(solo), "DECLINED");
        Assert.True(declined != null, $"the requester was not told:{Environment.NewLine}{harness.Channel(solo)}");
        Assert.True(AppEntryAudience_Tag.Is_AgentTagged(declined!.Subject));
        Assert.Contains(SIBLING_NAME, declined.Subject);

        Assert.Equal(sessionsBefore, harness.Store.Load_All().Count);
        Assert.Empty(Children_Of(harness, solo));
        Assert.Null(harness.Store.Get_Session(solo).ClosedUtc);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AnUnansweredPrompt_LapsesAfterTwelveHours()
    {
        using var harness = new SiblingEngine_Harness();
        var (solo, _, _) = await Arrange_Asked_Async(harness);

        var parked = Assert.Single(CloseConfirmation_Parking.Find_Parked(harness.Paths));
        File.SetLastWriteTimeUtc(parked, DateTime.UtcNow.AddHours(-(CloseConfirmation_Parking.EXPIRY_HOURS + 1)));

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => Has_Archived(harness, "expired"), WAIT_MILLISECONDS),
            $"never lapsed. Archived: [{string.Join(", ", harness.Archived_Names())}]{Environment.NewLine}{harness.Log.Dump()}");

        var lapsed = Find_AppEntry_OrNull(harness.Paths.Get_OwnerChannelFile(solo), "LAPSED");
        Assert.True(lapsed != null, $"the requester was not told it lapsed:{Environment.NewLine}{harness.Channel(solo)}");
        Assert.Contains(SIBLING_NAME, lapsed!.Subject);
        Assert.Empty(Children_Of(harness, solo));
    }

    /// <summary>RE-VALIDATED AT THE TAP (§4.2): a request whose worktree became shared while it waited is refused, not born.</summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AWorktreeThatBecameSharedWhileParked_IsRefusedAtTheTap()
    {
        using var harness = new SiblingEngine_Harness();
        var (solo, _, _) = await Arrange_Asked_Async(harness);

        // Another open session now works in that tree — created without members, so nothing spawns.
        harness.Store.Create_Orchestration("aiorchestrator-77", SiblingEngine_Harness.REPO_NAME, harness.RepoPath);
        harness.Store.Set_SiblingLink("aiorchestrator-77", "aiorchestrator-77", "aiorchestrator-77", "aiorchestrator-77#1", harness.WorktreePath);

        await harness.Tap_Async("Start it");

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => Has_Archived(harness, SiblingRefusals.WORKTREE_SHARED), WAIT_MILLISECONDS),
            $"never refused at the tap. Archived: [{string.Join(", ", harness.Archived_Names())}]{Environment.NewLine}{harness.Log.Dump()}");

        var refusal = Find_AppEntry_OrNull(harness.Paths.Get_OwnerChannelFile(solo), "already in use");
        Assert.True(refusal != null, $"the requester was not told why:{Environment.NewLine}{harness.Channel(solo)}");
        Assert.True(AppEntryAudience_Tag.Is_AgentTagged(refusal!.Subject));

        Assert.Empty(Children_Of(harness, solo));
        Assert.Null(harness.Store.Get_Session(solo).ClosedUtc);

        // RULING E: the owner's only record of the tap must not say it worked.
        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => harness.Telegram.TextEdits.Any(edit => edit.Text.Contains("Not started", StringComparison.Ordinal)), WAIT_MILLISECONDS),
            $"the prompt was never edited to Not started:{Environment.NewLine}{string.Join(Environment.NewLine, harness.Telegram.TextEdits.Select(edit => edit.Text))}");
        Assert.DoesNotContain(harness.Telegram.TextEdits, edit => edit.Text.Contains("✅", StringComparison.Ordinal));
    }

    /// <summary>
    /// THE PROMPT-TIME RE-RUN OF THE TABLE (§4.2, the Sibling arm beside mootBecause): a requester
    /// promoted to a crew while its request waited is refused when the prompt would be drawn — the owner
    /// is never asked a question whose "yes" could only be refused.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ARequesterPromotedToACrewWhileParked_IsRefusedWhenThePromptIsDrawn()
    {
        using var harness = new SiblingEngine_Harness();

        // Starts the loop. The requester below gets no topic until the test gives it one, so the ask
        // sweep cannot reach its request while the world is being changed underneath it.
        await harness.Start_Solo_Async("AI-Orch · loop starter");

        var solo = harness.Launcher.Start_BasicOrchestration(SiblingEngine_Harness.REPO_NAME, harness.RepoPath).OrchId;
        harness.Store.Set_DisplayName(solo, PARENT_NAME);
        var handover = harness.Append_Outbox(solo, $"{HandoverEntry_Detector.HANDOVER_MARKER} limits");

        harness.Drop_Request(Build_Request(solo, handover, harness.WorktreePath));

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => CloseConfirmation_Parking.Find_Parked(harness.Paths).Count == 1, WAIT_MILLISECONDS),
            $"the request was never parked.{Environment.NewLine}{harness.Log.Dump()}");

        harness.Launcher.Promote_ToFullCrew(solo);
        harness.Store.Set_TelegramTopicId(solo, 7999);

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => Has_Archived(harness, SiblingRefusals.NOT_A_SOLO), WAIT_MILLISECONDS),
            $"never refused at prompt time. Archived: [{string.Join(", ", harness.Archived_Names())}]{Environment.NewLine}{harness.Log.Dump()}");

        var refusal = Find_AppEntry_OrNull(harness.Paths.Get_OwnerChannelFile(solo), "is a crew, not a solo");
        Assert.True(refusal != null, $"the requester was not told why:{Environment.NewLine}{harness.Channel(solo)}");
        Assert.True(AppEntryAudience_Tag.Is_AgentTagged(refusal!.Subject));

        await Task.Delay(BridgeTestTiming.Window_ForTicks(5));
        Assert.Null(harness.Telegram.Find_ButtonFor("Start it"));
    }

    /// <summary>Review Focus 3. One HANDOVER, one child, however many times it is asked.</summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ASecondRequestCitingTheSameHandover_IsAnsweredWithTheExistingChild()
    {
        using var harness = new SiblingEngine_Harness();
        var (solo, _, request) = await Arrange_Asked_Async(harness);

        await harness.Tap_Async("Start it");
        var child = await Wait_ForChild_Async(harness, solo);
        await SiblingEngine_Harness.Wait_Until_Async(() => Has_Archived(harness, "started"), WAIT_MILLISECONDS);

        harness.Drop_Request(request);

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => Has_Archived(harness, SiblingRefusals.HANDOVER_ALREADY_USED), WAIT_MILLISECONDS),
            $"the retry was not answered. Archived: [{string.Join(", ", harness.Archived_Names())}]{Environment.NewLine}{harness.Log.Dump()}");

        var answer = Find_AppEntry_OrNull(harness.Paths.Get_OwnerChannelFile(solo), "already started");
        Assert.True(answer != null, harness.Channel(solo));
        Assert.Contains(child.OrchId, answer!.Body);
        Assert.Single(Children_Of(harness, solo));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ASecondRequestWhileTheFirstIsParked_IsAnsweredHeld()
    {
        using var harness = new SiblingEngine_Harness();
        var (solo, _, request) = await Arrange_Asked_Async(harness);

        harness.Drop_Request(request);

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => Has_Archived(harness, SiblingRefusals.HANDOVER_ALREADY_USED), WAIT_MILLISECONDS),
            $"the re-drop was not answered. Archived: [{string.Join(", ", harness.Archived_Names())}]{Environment.NewLine}{harness.Log.Dump()}");

        Assert.True(Find_AppEntry_OrNull(harness.Paths.Get_OwnerChannelFile(solo), "already waiting for the owner") != null, harness.Channel(solo));
        Assert.Single(CloseConfirmation_Parking.Find_Parked(harness.Paths));
        Assert.Empty(Children_Of(harness, solo));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheSameTapDeliveredTwice_StartsOneChild()
    {
        using var harness = new SiblingEngine_Harness();
        var (solo, _, _) = await Arrange_Asked_Async(harness);

        var button = harness.Telegram.Find_ButtonMessage_OrNull("Start it")!.Value;
        const string CALLBACK_ID = "cbq-the-one-gesture";

        harness.Telegram.Queue_Updates(SiblingEngine_Harness.Tap_Json(button.Data, button.MessageId, button.ThreadId, 9501, CALLBACK_ID));
        harness.Telegram.Queue_Updates(SiblingEngine_Harness.Tap_Json(button.Data, button.MessageId, button.ThreadId, 9502, CALLBACK_ID));

        await Wait_ForChild_Async(harness, solo);
        await SiblingEngine_Harness.Wait_Until_Async(() => Has_Archived(harness, "started"), WAIT_MILLISECONDS);
        await Task.Delay(BridgeTestTiming.Window_ForTicks(5));

        Assert.Single(Children_Of(harness, solo));
    }

    /// <summary>
    /// §7.3: THE DISPATCH PAUSE HOLDS EVERY SPAWNING REQUEST, and a tap is one. HELD, NOT REFUSED: the
    /// owner's "yes" is kept and carried out when the pause lifts — the start-orchestration precedent,
    /// where the file waits on disk. The prompt says so in the meantime, and the positive control after
    /// the lift proves the wait measured the pause and not a tap nothing would ever act on.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ATapDuringADispatchPause_IsHeld_AndStartsWhenThePauseLifts()
    {
        using var harness = new SiblingEngine_Harness();
        var (solo, _, _) = await Arrange_Asked_Async(harness);

        Write_UsageProbe(harness, "seven_day", 99, DateTime.UtcNow.AddDays(1));

        // UP TO A MINUTE AND A HALF, and that is the engine's own throttle, not slack: the probes are
        // read at most once per LIMIT_CHECK_INTERVAL_SECONDS (60), and the first read was the loop's first
        // tick. The pause cannot come first here — it would hold the request's ARRIVAL too, and there
        // would be no prompt to tap.
        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => harness.EngineState.Load_OrEmpty().DispatchPausedUntilUtc != null, PAUSE_WAIT_MILLISECONDS),
            $"the engine never entered the dispatch pause, so this test would prove nothing.{Environment.NewLine}{harness.Log.Dump()}");

        await harness.Tap_Async("Start it");

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => harness.Telegram.TextEdits.Any(edit => edit.Text.Contains("usage-limit pause", StringComparison.Ordinal)), WAIT_MILLISECONDS),
            $"the prompt never said the start is held:{Environment.NewLine}{string.Join(Environment.NewLine, harness.Telegram.TextEdits.Select(edit => edit.Text))}{Environment.NewLine}{harness.Log.Dump()}");

        await Task.Delay(BridgeTestTiming.Window_ForTicks(10));

        Assert.Empty(Children_Of(harness, solo));
        Assert.Single(CloseConfirmation_Parking.Find_Parked(harness.Paths));
        Assert.Empty(harness.Archived_Names());
        Assert.Single(harness.Telegram.Sent_InTopic(harness.TopicOf(solo)), text => text.Contains("wants a sibling session", StringComparison.Ordinal));

        Assert.NotNull(harness.Engine.Lift_DispatchPause_ByOwner());

        await Wait_ForChild_Async(harness, solo);

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => Has_Archived(harness, "started"), WAIT_MILLISECONDS),
            $"never archived as started after the lift. Archived: [{string.Join(", ", harness.Archived_Names())}]{Environment.NewLine}{harness.Log.Dump()}");
    }

    /// <summary>
    /// A BIRTH THAT THROWS IS REPORTED IN ITS OWN WORDS and never filed as started (Task 7 carry). The
    /// spawn fails after the child was created, so the step's message names the child — the one fact the
    /// requester and General need — and it must arrive verbatim, not paraphrased into "it failed".
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ABirthThatThrows_ReportsItsCauseVerbatim_AndIsNeverArchivedAsStarted()
    {
        using var harness = new SiblingEngine_Harness();
        var (solo, _, _) = await Arrange_Asked_Async(harness);
        const string CAUSE = "scripted spawn failure: the terminal could not be opened";

        harness.Spawner.Fail_NextSpawnIn_With(harness.WorktreePath, new Exception(CAUSE));
        await harness.Tap_Async("Start it");

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => harness.Archived_Names().Count > 0, WAIT_MILLISECONDS),
            $"the tap was never archived.{Environment.NewLine}{harness.Log.Dump()}");

        Assert.DoesNotContain(harness.Archived_Names(), name => name.StartsWith("started-", StringComparison.Ordinal));
        Assert.True(Has_Archived(harness, "uncertain"), $"Archived: [{string.Join(", ", harness.Archived_Names())}]");

        var child = Assert.Single(Children_Of(harness, solo));
        var expected = $"Sibling '{child.OrchId}' of '{solo}' was created and linked; its session did not start: {CAUSE}";

        var toRequester = ChannelEntry_Parser.Parse_All(harness.Channel(solo))
            .LastOrDefault(entry => entry.Author == ChannelAuthors.App && entry.Body.Contains(expected, StringComparison.Ordinal));
        Assert.True(toRequester != null, $"the requester was not given the cause verbatim:{Environment.NewLine}{harness.Channel(solo)}");

        Assert.Contains(expected, Read_OrEmpty(harness.Paths.GeneralChannelFile));

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => harness.Telegram.TextEdits.Any(edit => edit.Text.Contains("did not start cleanly", StringComparison.Ordinal)), WAIT_MILLISECONDS),
            $"the prompt was never edited to the uncertain line:{Environment.NewLine}{string.Join(Environment.NewLine, harness.Telegram.TextEdits.Select(edit => edit.Text))}");
        Assert.DoesNotContain(harness.Telegram.TextEdits, edit => edit.Text.Contains("✅", StringComparison.Ordinal));
        Assert.Null(harness.Store.Get_Session(solo).ClosedUtc);
    }

    /// <summary>A solo with a HANDOVER entry, its request parked, and the prompt on the phone — returns the request JSON too, for re-drops.</summary>
    static async Task<(string Solo, int Handover, string Request)> Arrange_Asked_Async(SiblingEngine_Harness harness)
    {
        var solo = await harness.Start_Solo_Async(PARENT_NAME);
        var handover = harness.Append_Outbox(solo, $"{HandoverEntry_Detector.HANDOVER_MARKER} limits");
        var request = Build_Request(solo, handover, harness.WorktreePath);

        harness.Drop_Request(request);

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => harness.Telegram.Find_ButtonMessage_OrNull("Start it") != null, WAIT_MILLISECONDS),
            $"the parked sibling request was never put to the owner.{Environment.NewLine}{harness.Telegram.Dump_Sent()}{Environment.NewLine}{harness.Log.Dump()}");

        return (solo, handover, request);
    }

    /// <summary>
    /// The child, once the tap has been RESOLVED — archived. The child's session.json exists from the
    /// launcher's Create_Orchestration on, BEFORE its spawn and before the birth note, so waiting for the
    /// session alone would read a birth half-way through.
    /// </summary>
    static async Task<IOrchestrationSession> Wait_ForChild_Async(SiblingEngine_Harness harness, string parent)
    {
        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => Children_Of(harness, parent).Count > 0 && harness.Archived_Names().Count > 0, WAIT_MILLISECONDS),
            $"no child of '{parent}' was started. Archived: [{string.Join(", ", harness.Archived_Names())}]{Environment.NewLine}{harness.Log.Dump()}");

        return Children_Of(harness, parent)[0];
    }

    static IReadOnlyList<IOrchestrationSession> Children_Of(SiblingEngine_Harness harness, string parent)
    {
        return [.. harness.Store.Load_All().Where(session => session.BornFromOrchId == parent)];
    }

    static string Build_Request(string orchId, int handover, string worktree)
    {
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["action"] = OrchestrationRequests_Reader.SPAWN_SIBLING_ACTION,
            ["orchId"] = orchId,
            ["name"] = SIBLING_NAME,
            ["job"] = JOB,
            ["handover"] = handover,
            ["worktree"] = worktree,
            ["reason"] = REASON,
        });
    }

    static bool Has_Archived(SiblingEngine_Harness harness, string label)
    {
        return harness.Archived_Names().Any(name => name.StartsWith($"{label}-", StringComparison.Ordinal));
    }

    static IChannelEntry? Find_AppEntry_OrNull(string channelFilePath, string subjectFragment)
    {
        if (!File.Exists(channelFilePath))
            return null;

        return ChannelEntry_Parser.Parse_All(File.ReadAllText(channelFilePath))
            .LastOrDefault(entry => entry.Author == ChannelAuthors.App && entry.Subject.Contains(subjectFragment, StringComparison.Ordinal));
    }

    static string Read_OrEmpty(string path)
    {
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    /// <summary>The statusline probe shape <c>DispatchPauseAcrossRestartTests</c> writes.</summary>
    static void Write_UsageProbe(SiblingEngine_Harness harness, string windowKey, double percent, DateTime resetsAtUtc)
    {
        var unixSeconds = new DateTimeOffset(DateTime.SpecifyKind(resetsAtUtc, DateTimeKind.Utc), TimeSpan.Zero).ToUnixTimeSeconds();

        File.WriteAllText(
            Path.Combine(harness.Paths.Root, $"{Guid.NewGuid():N}.usage.json"),
            $"{{\"rate_limits\":{{\"{windowKey}\":{{\"used_percentage\":{percent.ToString(CultureInfo.InvariantCulture)},\"resets_at\":{unixSeconds}}}}}}}");
    }
}
