using System.Globalization;
using System.Text.Json;
using AIOrchestratorCoreLib.Bridge.Siblings;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.Siblings;

/// <summary>
/// A <c>spawn-sibling</c> request's ARRIVAL, driven through the real engine (plan 2026-09-23 Task 8,
/// spec §4.2, §7.3): refused to the solo with its own reason, or parked for the owner's tap — and left on
/// disk while a usage-limit pause holds. The tap itself is Task 9's.
///
/// <para>
/// REFUSALS NEVER REACH THE OWNER (spec §4.2, decision 15): each is an <c>[agent]</c> entry and an
/// archive label, with no prompt and no spawn. The owner's tap answers "should this sibling exist",
/// never "is this request well-formed".
/// </para>
/// </summary>
public class SpawnSiblingArrivalTests
{
    const string PARENT_NAME = "AI-Orch · settings work";
    const string SIBLING_NAME = "AI-Orch · limits rework";
    const string JOB = "Rework the usage-limit pause so a restored pause can be lifted per window";
    const string REASON = "two jobs the owner wants to steer separately; they touch disjoint files";
    const int WAIT_MILLISECONDS = 20_000;

    /// <summary>
    /// EVERY ROW OF THE REFUSAL TABLE ARRIVES THE SAME WAY: an agent entry naming the refusal, the file
    /// archived under the row's label, no button on the phone, nothing spawned, nothing parked.
    /// <c>unspawnable</c> has no requester to tell, so its entry goes to the GENERAL channel — as
    /// <see cref="AppEntryAudiences.Agent"/>, a deliberate departure from the promote precedent's Owner
    /// audience (decision 15: the owner cannot act on a missing orchestration's request).
    /// </summary>
    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(SiblingRefusals.UNSPAWNABLE)]
    [InlineData(SiblingRefusals.NOT_A_SOLO)]
    [InlineData(SiblingRefusals.NO_HANDOVER_ENTRY)]
    [InlineData(SiblingRefusals.AT_CAP)]
    [InlineData(SiblingRefusals.WORKTREE_MISSING)]
    [InlineData(SiblingRefusals.WORKTREE_NOT_OF_REPO)]
    [InlineData(SiblingRefusals.WORKTREE_SHARED)]
    [InlineData(SiblingRefusals.NAME_TAKEN)]
    public async Task EachRefusal_IsAnAgentEntry_AnArchiveLabel_AndNoPrompt(string label)
    {
        using var harness = new SiblingEngine_Harness();

        var solo = await harness.Start_Solo_Async(PARENT_NAME);
        var (requester, request, expectedSubject) = Arrange_Refusal(harness, solo, label);

        var spawnsBefore = harness.Spawner.SpawnedCommands.Count;
        var sessionsBefore = harness.Store.Load_All().Count;

        harness.Drop_Request(request);

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => Has_Archived(harness, label), WAIT_MILLISECONDS),
            $"the request was never archived as '{label}'. Archived: [{string.Join(", ", harness.Archived_Names())}]{Environment.NewLine}{harness.Log.Dump()}");

        var channel = requester == null ? harness.Paths.GeneralChannelFile : harness.Paths.Get_OwnerChannelFile(requester);
        var entry = Find_AppEntry_OrNull(channel, expectedSubject);

        Assert.True(entry != null, $"no FROM app entry naming '{expectedSubject}' in '{channel}':{Environment.NewLine}{File.ReadAllText(channel)}");
        Assert.True(AppEntryAudience_Tag.Is_AgentTagged(entry!.Subject), $"the refusal must be an [agent] entry (decision 15), got '{entry.Subject}'");

        // "Never" needs a window: long enough for the ask sweep to have reached a parked file.
        await Task.Delay(BridgeTestTiming.Window_ForTicks(10));

        Assert.Null(harness.Telegram.Find_ButtonFor("Start it"));
        Assert.Empty(CloseConfirmation_Parking.Find_Parked(harness.Paths));
        Assert.Equal(spawnsBefore, harness.Spawner.SpawnedCommands.Count);
        Assert.Equal(sessionsBefore, harness.Store.Load_All().Count);
    }

    /// <summary>
    /// A VALID REQUEST IS HELD, NOT STARTED: parked in <c>awaiting-owner/</c>, the requester told it is
    /// HELD as an agent entry, and nothing spawned.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AValidRequest_IsParked_AndTheRequesterIsToldItIsHeld()
    {
        using var harness = new SiblingEngine_Harness();

        var solo = await harness.Start_Solo_Async(PARENT_NAME);
        var handover = harness.Append_Outbox(solo, $"{HandoverEntry_Detector.HANDOVER_MARKER} limits");
        var spawnsBefore = harness.Spawner.SpawnedCommands.Count;
        var sessionsBefore = harness.Store.Load_All().Count;

        var dropped = harness.Drop_Request(Build_Request(solo, SIBLING_NAME, handover, harness.WorktreePath));

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => CloseConfirmation_Parking.Find_Parked(harness.Paths).Count == 1, WAIT_MILLISECONDS),
            $"the request was never parked.{Environment.NewLine}{harness.Log.Dump()}");

        Assert.False(File.Exists(dropped));

        var held = Find_AppEntry_OrNull(harness.Paths.Get_OwnerChannelFile(solo), "sibling HELD");
        Assert.True(held != null, $"the requester was not told its request is HELD:{Environment.NewLine}{harness.Channel(solo)}");
        Assert.True(AppEntryAudience_Tag.Is_AgentTagged(held!.Subject), $"the HELD notice is for the solo, not the owner's phone (decision 15), got '{held.Subject}'");

        Assert.Equal(spawnsBefore, harness.Spawner.SpawnedCommands.Count);
        Assert.Equal(sessionsBefore, harness.Store.Load_All().Count);
    }

    /// <summary>
    /// PRE-FLIGHT RULING A: THE PER-TICK ASK SWEEP REACHES A PARKED SIBLING WITH NO TAP. The requester
    /// has a topic, so the sweep builds the prompt on the very next tick — a prompt arm that threw "until
    /// Task 9" would throw here, on the 2-second tick, with nobody tapping anything. So the prompt is
    /// real from this task on: in the requester's topic, naming the requester, the new topic's name, the
    /// job and the reason, with the two sibling buttons — and the request stays parked.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AParkedRequest_IsAskedOnTheTick_WithTheSiblingPrompt_AndNothingThrows()
    {
        using var harness = new SiblingEngine_Harness();

        var solo = await harness.Start_Solo_Async(PARENT_NAME);
        var handover = harness.Append_Outbox(solo, $"{HandoverEntry_Detector.HANDOVER_MARKER} limits");

        harness.Drop_Request(Build_Request(solo, SIBLING_NAME, handover, harness.WorktreePath));

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => harness.Telegram.Find_ButtonMessage_OrNull("Start it") != null, WAIT_MILLISECONDS),
            $"the parked sibling request was never put to the owner.{Environment.NewLine}{harness.Telegram.Dump_Sent()}{Environment.NewLine}{harness.Log.Dump()}");

        var confirm = harness.Telegram.Find_ButtonMessage_OrNull("Start it")!.Value;
        var decline = harness.Telegram.Find_ButtonMessage_OrNull("Keep one session");

        Assert.Equal(harness.TopicOf(solo), confirm.ThreadId);
        Assert.NotNull(decline);
        Assert.Equal(confirm.MessageId, decline!.Value.MessageId);

        var prompt = harness.Telegram.Sent_InTopic(harness.TopicOf(solo)).Last(text => text.Contains(SIBLING_NAME, StringComparison.Ordinal));
        Assert.Contains(PARENT_NAME, prompt);
        Assert.Contains(JOB, prompt);
        Assert.Contains(REASON, prompt);

        Assert.Single(CloseConfirmation_Parking.Find_Parked(harness.Paths));
        Assert.Empty(harness.Archived_Names());
        Assert.False(harness.Log.Has_Line_Containing("unhandled close kind"), harness.Log.Dump());
    }

    /// <summary>
    /// A SPAWN DURING A USAGE-LIMIT PAUSE STAYS ON DISK (§4.2, §7.3). It joins the three that spawn,
    /// in the !dispatchPaused group, for the reason Process_PendingRequests records: a pause that
    /// spawned anyway is not a pause.
    ///
    /// <para>
    /// The pause is the engine's own, reached the way the live one is: a usage probe over the threshold
    /// (the <c>DispatchPauseAcrossRestartTests</c> probe shape), and lifted the way the owner lifts it
    /// (<c>Lift_DispatchPause_ByOwner</c>, behind /resume). After the lift the same window does not
    /// re-pause, so the file must leave the requests folder — parked, which is the positive control that
    /// the wait before it measured the pause and not a request nothing would ever read.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task UnderADispatchPause_TheFileStaysOnDisk_AndRunsAfterTheLift()
    {
        using var harness = new SiblingEngine_Harness();

        // Before the loop starts, so the first tick's pause check reads it.
        Write_UsageProbe(harness, "seven_day", 99, DateTime.UtcNow.AddDays(1));

        var solo = await harness.Start_Solo_Async(PARENT_NAME);

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(() => harness.EngineState.Load_OrEmpty().DispatchPausedUntilUtc != null, WAIT_MILLISECONDS),
            $"the engine never entered the dispatch pause, so this test would prove nothing.{Environment.NewLine}{harness.Log.Dump()}");

        var handover = harness.Append_Outbox(solo, $"{HandoverEntry_Detector.HANDOVER_MARKER} limits");
        var dropped = harness.Drop_Request(Build_Request(solo, SIBLING_NAME, handover, harness.WorktreePath));

        await Task.Delay(BridgeTestTiming.Window_ForTicks(20));

        Assert.True(File.Exists(dropped), $"a spawn-sibling request was consumed during a usage-limit pause.{Environment.NewLine}{harness.Log.Dump()}");
        Assert.Empty(CloseConfirmation_Parking.Find_Parked(harness.Paths));
        Assert.Empty(harness.Archived_Names());

        Assert.NotNull(harness.Engine.Lift_DispatchPause_ByOwner());

        Assert.True(
            await SiblingEngine_Harness.Wait_Until_Async(
                () => !File.Exists(dropped) && CloseConfirmation_Parking.Find_Parked(harness.Paths).Count == 1,
                WAIT_MILLISECONDS),
            $"the request did not run after the pause was lifted.{Environment.NewLine}{harness.Log.Dump()}");
    }

    /// <summary>
    /// The world for one refusal row: returns the requester whose channel is told (null = the general
    /// channel), the request JSON, and a fragment of the refusal's subject.
    /// </summary>
    static (string? Requester, string Request, string Subject) Arrange_Refusal(SiblingEngine_Harness harness, string solo, string label)
    {
        int Handover() => harness.Append_Outbox(solo, $"{HandoverEntry_Detector.HANDOVER_MARKER} limits");

        switch (label)
        {
            case SiblingRefusals.UNSPAWNABLE:
                return (null, Build_Request("aiorchestrator-404", SIBLING_NAME, 1, harness.WorktreePath), "spawn-sibling FAILED");

            case SiblingRefusals.NOT_A_SOLO:
            {
                var crew = harness.Launcher.Start_Orchestration(SiblingEngine_Harness.REPO_NAME, harness.RepoPath).OrchId;
                return (crew, Build_Request(crew, SIBLING_NAME, 1, harness.WorktreePath), "is a crew, not a solo");
            }

            case SiblingRefusals.NO_HANDOVER_ENTRY:
                return (solo, Build_Request(solo, SIBLING_NAME, 7, harness.WorktreePath), "no HANDOVER entry");

            case SiblingRefusals.AT_CAP:
            {
                // Two open members already linked to the solo's endeavour, plus the solo itself: three,
                // the shipped cap (O2). Created without members, so nothing here can be spawned.
                for (var n = 0; n < 2; n++)
                {
                    var linked = $"aiorchestrator-9{n}";
                    harness.Store.Create_Orchestration(linked, SiblingEngine_Harness.REPO_NAME, harness.RepoPath);
                    harness.Store.Set_SiblingLink(linked, solo, solo, $"{solo}#9{n}", Path.Combine(harness.Paths.Root, $"tree-{n}"));
                }

                return (solo, Build_Request(solo, SIBLING_NAME, Handover(), harness.WorktreePath), "the cap");
            }

            case SiblingRefusals.WORKTREE_MISSING:
                return (solo, Build_Request(solo, SIBLING_NAME, Handover(), Path.Combine(harness.Paths.Root, "no-such-tree")), "does not exist");

            case SiblingRefusals.WORKTREE_NOT_OF_REPO:
            {
                var stray = Path.Combine(Path.GetDirectoryName(harness.RepoPath)!, "stray");
                Directory.CreateDirectory(stray);
                return (solo, Build_Request(solo, SIBLING_NAME, Handover(), stray), "not a worktree of your repo");
            }

            case SiblingRefusals.WORKTREE_SHARED:
                return (solo, Build_Request(solo, SIBLING_NAME, Handover(), harness.RepoPath), "already in use");

            case SiblingRefusals.NAME_TAKEN:
                return (solo, Build_Request(solo, PARENT_NAME, Handover(), harness.WorktreePath), "is taken");

            default:
                throw new ArgumentException($"no arrangement for refusal '{label}'");
        }
    }

    static string Build_Request(string orchId, string name, int handover, string worktree)
    {
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["action"] = OrchestrationRequests_Reader.SPAWN_SIBLING_ACTION,
            ["orchId"] = orchId,
            ["name"] = name,
            ["job"] = JOB,
            ["handover"] = handover,
            ["worktree"] = worktree,
            ["reason"] = REASON,
        });
    }

    static bool Has_Archived(SiblingEngine_Harness harness, string label)
    {
        return harness.Archived_Names().Any(name => name.StartsWith($"{label}-sibling-", StringComparison.Ordinal));
    }

    static IChannelEntry? Find_AppEntry_OrNull(string channelFilePath, string subjectFragment)
    {
        if (!File.Exists(channelFilePath))
            return null;

        return ChannelEntry_Parser.Parse_All(File.ReadAllText(channelFilePath))
            .LastOrDefault(entry => entry.Author == ChannelAuthors.App && entry.Subject.Contains(subjectFragment, StringComparison.Ordinal));
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
