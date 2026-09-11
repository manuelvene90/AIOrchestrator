using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TelegramSendBudget;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// THE TWO OUTBOUND ALLOWANCES, AND THE ONE THAT SURVIVES A RESTART — brief F5.
///
/// <para>
/// Two properties, and they pull in opposite directions, which is why the answer is neither "start
/// full" nor "start empty": a crash loop must not mint a fresh burst of twenty messages every time
/// it comes up, and an ordinary restart must not pay a wait the previous process never earned. The
/// bucket is therefore PERSISTED — the restart resumes exactly where the process stopped.
/// </para>
/// </summary>
public class TelegramSendBudgetTests : IDisposable
{
    static readonly DateTime NOW = new(2026, 9, 10, 21, 11, 31, DateTimeKind.Utc);

    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public TelegramSendBudgetTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-sendbudget-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that will not delete is not a test result.
        }
    }

    /// <summary>
    /// THE PROBE THE OWNER SPECIFIED (2026-09-10): spend eight tokens, restart on the same state
    /// file, and the ninth send must wait for a token rather than the process getting ten more.
    /// </summary>
    [Fact]
    public async Task AfterARestart_TheSendBucketResumesWhereItStopped()
    {
        var before = TelegramSendBudget_Factory.Create_Fresh();

        for (var i = 0; i < 8; i++)
            await before.Wait_ForSend_Async(CancellationToken.None);

        Assert.Equal(TokenBucket_Gate.DEFAULT_CAPACITY - 8, before.Read_SendState().Tokens, precision: 1);

        // The write the engine does on every tick that moved a cursor.
        BridgeState_Store.Save(_paths, new Dictionary<string, long>(), 42L, before);

        // The read the engine factory does at the next start.
        var persisted = BridgeState_Store.Load_SendBucket_OrNull(_paths);
        Assert.NotNull(persisted);

        // Restored AT THE MOMENT IT WAS SAVED, so the assertion is about the restart and not about
        // however many real seconds this test took to get here.
        var after = TelegramSendBudget_Factory.Create_FromPersisted(
            persisted.Value.Tokens, persisted.Value.RefilledUtc, persisted.Value.RefilledUtc);

        Assert.Equal(TokenBucket_Gate.DEFAULT_CAPACITY - 8, after.Read_SendState().Tokens, precision: 1);

        // Two tokens are left, so two sends go straight out and the THIRD has to wait — which is
        // exactly the ninth and tenth of the run, and then the eleventh.
        await after.Wait_ForSend_Async(CancellationToken.None);
        await after.Wait_ForSend_Async(CancellationToken.None);

        Assert.True(after.Read_SendState().Tokens < 1,
            $"a restart handed the process a fresh burst — {after.Read_SendState().Tokens} tokens left after spending ten");
    }

    /// <summary>
    /// The other half of the same property: an ordinary restart after a QUIET stretch must not
    /// impose a wait. Real elapsed seconds refill the restored bucket exactly as they would inside
    /// a process that never died.
    /// </summary>
    [Fact]
    public void ARestartAfterAQuietStretch_ComesUpFull()
    {
        var savedAt = DateTime.UtcNow.AddMinutes(-10);

        var restored = TelegramSendBudget_Factory.Create_FromPersisted(0, savedAt, DateTime.UtcNow);

        // Nothing spent yet, so the stored zero is still zero — the refill happens on the first take.
        Assert.Equal(0, restored.Read_SendState().Tokens, precision: 1);

        var (tokens, _, wait) = TokenBucket_Gate.Take(0, savedAt, DateTime.UtcNow);

        Assert.Equal(TimeSpan.Zero, wait);
        Assert.Equal(TokenBucket_Gate.DEFAULT_CAPACITY - 1, tokens, precision: 1);
    }

    /// <summary>
    /// FIRST EVER START — no file, nothing to restore — is FULL, which is what the bridge has always
    /// done and what the catch-up-after-unmute design wants.
    /// </summary>
    [Fact]
    public void WithNothingPersisted_TheBucketStartsFull()
    {
        Assert.Null(BridgeState_Store.Load_SendBucket_OrNull(_paths));
        Assert.Equal(TokenBucket_Gate.DEFAULT_CAPACITY, TelegramSendBudget_Factory.Create_Fresh().Read_SendState().Tokens, precision: 1);
    }

    /// <summary>
    /// A state file written before this key existed, and one that is damaged, both read as "nothing
    /// to restore" — and neither may throw. The bridge's remote control is worth more than a
    /// rate-limit nicety.
    /// </summary>
    [Theory]
    [InlineData("{\"fileOffsets\":{},\"lastUpdateId\":7}")]
    [InlineData("{\"fileOffsets\":{},\"lastUpdateId\":7,\"sendBucket\":{}}")]
    [InlineData("{\"fileOffsets\":{},\"lastUpdateId\":7,\"sendBucket\":{\"tokens\":\"nonsense\",\"refilledUtc\":\"nonsense\"}}")]
    [InlineData("{ this is not json")]
    [InlineData("")]
    public void AStateFileWithNoUsableBucket_IsNotAFailure(string content)
    {
        File.WriteAllText(_paths.BridgeStateFile, content);

        Assert.Null(BridgeState_Store.Load_SendBucket_OrNull(_paths));
    }

    /// <summary>
    /// THE FILE IS UNTRUSTED INPUT (CLAUDE.md decision 12, the same lesson as the channel headers).
    /// A FUTURE stamp is the dangerous one: Take refuses to refill on a negative elapsed time, so an
    /// absurd stamp would freeze the bucket rather than merely skew it.
    /// </summary>
    [Fact]
    public void AbsurdPersistedValues_AreClamped_NotObeyed()
    {
        var now = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(TokenBucket_Gate.DEFAULT_CAPACITY, TelegramSendBudget_Factory.Create_FromPersisted(9_999, now, now).Read_SendState().Tokens, precision: 1);
        Assert.Equal(0, TelegramSendBudget_Factory.Create_FromPersisted(-50, now, now).Read_SendState().Tokens, precision: 1);
        Assert.Equal(TokenBucket_Gate.DEFAULT_CAPACITY, TelegramSendBudget_Factory.Create_FromPersisted(double.NaN, now, now).Read_SendState().Tokens, precision: 1);
        Assert.Equal(now, TelegramSendBudget_Factory.Create_FromPersisted(5, now.AddDays(30), now).Read_SendState().RefilledUtc);
    }

    // ---- the second bucket ----

    /// <summary>
    /// EDITS NO LONGER SPEND THE MESSAGE ALLOWANCE, AND THEY NO LONGER SPEND NOTHING. The first half
    /// was already true and is the reason the second was missed: "not on the message bucket" was
    /// implemented as "on no bucket at all", on the app's highest-volume traffic.
    /// </summary>
    [Fact]
    public async Task AnEdit_DoesNotTouchTheMessageAllowance()
    {
        var budget = TelegramSendBudget_Factory.Create_Fresh();

        await budget.Wait_ForControl_Async(CancellationToken.None);

        Assert.Equal(TokenBucket_Gate.DEFAULT_CAPACITY, budget.Read_SendState().Tokens, precision: 1);
    }

    /// <summary>
    /// The BURST arithmetic, on the pure gate rather than on the clock — twenty edits from an empty
    /// control bucket must be spread out rather than fired at once, and asserting that by actually
    /// waiting would cost the suite forty seconds to re-measure Task.Delay.
    /// </summary>
    [Fact]
    public void ABurstOfEdits_IsSpreadOverTime_NotFiredAtOnce()
    {
        var start = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
        var tokens = 0d;
        var refilledUtc = start;
        var now = start;
        var granted = 0;

        // One simulated minute, sampled every second.
        for (var second = 0; second < 60; second++)
        {
            now = start.AddSeconds(second);

            var (nextTokens, nextRefill, wait) = TokenBucket_Gate.Take(
                tokens, refilledUtc, now, TokenBucket_Gate.CONTROL_CAPACITY, TokenBucket_Gate.DEFAULT_REFILL_SECONDS);

            tokens = nextTokens;
            refilledUtc = nextRefill;

            if (wait <= TimeSpan.Zero)
                granted++;
        }

        // 30 a minute is the refill rate, so a caller asking once a second gets at most 30 of its
        // 60 attempts through — and, crucially, not all of them in the first second.
        Assert.InRange(granted, 25, 31);
    }

    /// <summary>
    /// AND IT STARTS EMPTY, which is the half that is not free: the first edit of a run waits. That
    /// is the price of a crash loop being unable to fire thirty edits in its first second, and it is
    /// paid once per process rather than once per restart-of-a-burst.
    /// </summary>
    [Fact]
    public async Task TheControlBucketStartsEmpty_SoTheFirstEditWaits()
    {
        var budget = TelegramSendBudget_Factory.Create_Fresh();

        var started = DateTime.UtcNow;
        await budget.Wait_ForControl_Async(CancellationToken.None);
        var waited = DateTime.UtcNow - started;

        var oneToken = TimeSpan.FromSeconds(TokenBucket_Gate.DEFAULT_REFILL_SECONDS / TokenBucket_Gate.CONTROL_CAPACITY);

        Assert.True(waited >= oneToken - TimeSpan.FromMilliseconds(150),
            $"the control bucket started full: the first edit went out after {waited.TotalMilliseconds:0} ms");

        // And it is a WAIT, not a block: two seconds, not the twenty a callback query cannot survive.
        Assert.True(waited < TimeSpan.FromSeconds(10), $"the first edit waited {waited.TotalSeconds:0.#} s");
    }

    /// <summary>
    /// THE WIRING, not just the rule: a running engine really does write the bucket into
    /// <c>.bridge-state.json</c>, so the next start has something to restore.
    ///
    /// <para>
    /// Through <see cref="BridgeEngine_Factory.Create_WithTelegramClient"/>,
    /// which takes the budget explicitly. The remaining unasserted line is the one
    /// <c>Create_WithTiming</c> composes on the production path — load the bucket, build the client
    /// around it, hand the same object to the engine — and it is unasserted for a specific reason:
    /// reaching it means constructing the REAL Telegram client, whose inbound loop would call
    /// api.telegram.org. Stated rather than hidden, exactly as <see cref="TelegramAttempt_Gate"/>
    /// states the same gap about its own caller.
    /// </para>
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ARunningEngine_WritesTheSendBucketIntoTheStateFile()
    {
        Directory.CreateDirectory(_paths.RequestsFolder);
        File.WriteAllText(_paths.ConfigFile, "{\"repos\":[],\"telegramSupergroupChatId\":-1002233445566,\"telegramOwnerUserId\":555000111}");

        var budget = TelegramSendBudget_Factory.Create_Fresh();

        for (var i = 0; i < 8; i++)
            await budget.Wait_ForSend_Async(CancellationToken.None);

        var store = OrchestrationSessionStore_Factory.Create(_paths);
        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var log = OrchestrationLog_Factory.Create(_paths);
        var launcher = OrchestrationLauncher_Factory.Create(
            _paths, configProvider, store, new RecordingSpawner_Fake(), log);

        var engine = BridgeEngine_Factory.Create_WithTelegramClient(
            _paths, configProvider, store, launcher, log, telegramClient: null,
            BridgeTestTiming.Fast(), budget);

        using var cancellation = new CancellationTokenSource();
        var running = engine.Run_Async(cancellation.Token);

        await Task.Delay(BridgeTestTiming.Window_ForTicks(3));
        await cancellation.CancelAsync();

        try
        {
            await running;
        }
        catch (OperationCanceledException)
        {
            // The ordinary way the engine stops.
        }

        var persisted = BridgeState_Store.Load_SendBucket_OrNull(_paths);

        Assert.NotNull(persisted);
        Assert.Equal(TokenBucket_Gate.DEFAULT_CAPACITY - 8, persisted.Value.Tokens, precision: 1);
    }

    /// <summary>
    /// The control bucket is LARGER than the message one — the whole reason it is separate rather
    /// than the same rule applied twice. Asserted on the derivation, so a change to either ceiling
    /// that inverted them would fail here rather than in production.
    /// </summary>
    [Fact]
    public void TheControlBucketIsTheLargerOne()
    {
        Assert.True(TokenBucket_Gate.CONTROL_CAPACITY > TokenBucket_Gate.DEFAULT_CAPACITY);
        Assert.Equal(TokenBucket_Gate.CONTROL_CALLS_PER_MINUTE, TokenBucket_Gate.CONTROL_CAPACITY * 2);
        Assert.True(TokenBucket_Gate.MAXIMUM_CONTROL_RETRY_WAIT < TokenBucket_Gate.MAXIMUM_INLINE_RETRY_WAIT,
            "a 429 on a callback query must not hold a tick open — Telegram invalidates one after about ten seconds");
    }

    /// <summary>
    /// THE FIRST EDIT OF A MESSAGE GOES STRAIGHT OUT. The gate is about a REPEATED edit of the same
    /// message; making the first one wait would delay every status line by half a minute after every
    /// restart, for a throttle nothing has hit yet.
    /// </summary>
    [Fact]
    public void TheFirstEditOfAMessageIsNotHeldBack()
    {
        var budget = TelegramSendBudget_Factory.Create_Fresh();

        Assert.Equal(TimeSpan.Zero, budget.Reserve_MessageEdit(4242, NOW));
    }

    /// <summary>
    /// TWO DIFFERENT MESSAGES DO NOT HOLD EACH OTHER BACK, which is the whole reason this is a
    /// per-message gate and not a bucket. Telegram's limit here is per message; a global allowance
    /// would make one topic's PULSE wait for another's, and with one topic per orchestration that is
    /// the wrong shape at any size.
    /// </summary>
    [Fact]
    public void TwoDifferentMessagesDoNotWaitForEachOther()
    {
        var budget = TelegramSendBudget_Factory.Create_Fresh();

        Assert.Equal(TimeSpan.Zero, budget.Reserve_MessageEdit(1, NOW));
        Assert.Equal(TimeSpan.Zero, budget.Reserve_MessageEdit(2, NOW));
        Assert.Equal(TimeSpan.Zero, budget.Reserve_MessageEdit(3, NOW));
    }

    /// <summary>
    /// A SECOND EDIT OF THE SAME MESSAGE IS HELD, which is the behaviour the 429s asked for. Measured
    /// in production on 2026-09-10: a once-a-minute PULSE edit drew `retry_after` 20, 22 then 32
    /// seconds, per topic, while the group bucket sat nearly full.
    ///
    /// <para>
    /// IT REPORTS THE WAIT, IT DOES NOT SERVE IT. The version this replaces awaited the gap inside
    /// the caller — and the caller is the 2 s mirror tick, so a held message parked the whole bridge
    /// for up to half a minute. The old test had to prove "it wanted to wait" through a cancelled
    /// token; now the wait is simply the return value, which is both a better test and the point of
    /// the change.
    /// </para>
    /// </summary>
    [Fact]
    public void ASecondEditOfTheSameMessageIsHeldForTheGap()
    {
        var budget = TelegramSendBudget_Factory.Create_Fresh();

        budget.Reserve_MessageEdit(4242, NOW);

        var owed = budget.Reserve_MessageEdit(4242, NOW.AddSeconds(2));

        Assert.Equal(TokenBucket_Gate.MINIMUM_GAP_BETWEEN_EDITS_OF_ONE_MESSAGE - TimeSpan.FromSeconds(2), owed);
    }

    /// <summary>
    /// AND THE SAME MESSAGE IS FREE AGAIN ONCE THE GAP HAS PASSED — asserted on the CLOCK now, which
    /// the previous version could not do. Its own comment admitted the compromise: it stood in a
    /// different message for an expired one, so it pinned the pruning rather than the gap, and it
    /// would have passed against a gate that held a message for ever after its first edit.
    /// </summary>
    [Fact]
    public void AMessageWhoseGapHasExpiredIsFreeAgain()
    {
        var budget = TelegramSendBudget_Factory.Create_Fresh();

        budget.Reserve_MessageEdit(4242, NOW);

        Assert.Equal(
            TimeSpan.Zero,
            budget.Reserve_MessageEdit(4242, NOW + TokenBucket_Gate.MINIMUM_GAP_BETWEEN_EDITS_OF_ONE_MESSAGE));
    }

    /// <summary>
    /// A REFUSAL DOES NOT MOVE THE STAMP, and this is the defect the sleep-to-skip change could have
    /// introduced. The old code advanced the stamp to `lastEdit + gap` because it was about to sleep
    /// until then and two sleepers had to be kept off the same instant. Nobody sleeps now, so an
    /// advance would push the door a further gap away every time a tick-rate caller asked and was
    /// turned back — and a status line asking every 2 s would never be let through at all.
    /// </summary>
    [Fact]
    public void ATurnedBackEditDoesNotPushTheDoorFurtherAway()
    {
        var budget = TelegramSendBudget_Factory.Create_Fresh();
        var gap = TokenBucket_Gate.MINIMUM_GAP_BETWEEN_EDITS_OF_ONE_MESSAGE;

        budget.Reserve_MessageEdit(4242, NOW);

        // Asked and turned back once a second for ten seconds, as the mirror tick would.
        for (var second = 1; second <= 10; second++)
            budget.Reserve_MessageEdit(4242, NOW.AddSeconds(second));

        // The door still opens exactly one gap after the edit that actually went out.
        Assert.Equal(TimeSpan.Zero, budget.Reserve_MessageEdit(4242, NOW + gap));
    }
}
