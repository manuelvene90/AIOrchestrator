# One Door To Telegram — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Take every outbound Telegram call out of the 2-second mirror tick, so that one place decides when anything is said to Telegram — and can finally wait as long as Telegram asks.

**Architecture:** Producer/consumer. The tick stops calling Telegram and instead *publishes intents*: **jobs** (must be delivered, in order — the conversation) and **slots** (latest-state-wins, keyed — the cosmetic repaints). A single `OutboundPump` drains them as a scheduler, not a FIFO: it picks the highest-priority intent that is *eligible* (not held by a cooldown, not behind its own order key's head), honours `retry_after` in full, and reports each outcome back to the publisher. The queue is deliberately **not persisted**: the channel files plus the tailer's unadvanced offsets already are the durable queue.

**Tech Stack:** C# / .NET 10, xUnit. `System.Threading.Channels` for the pump's wake-up signal (existing repo precedent: `AIOrchestratorCoreLib/Running/StreamTurn/StreamSessionProcess.cs`). No new NuGet dependency.

**Spec:** `docs/superpowers/specs/2026-09-10-one-door-to-telegram-design.md` (§4.2 – §4.5). Evidence and sources: `docs/superpowers/specs/2026-09-10-telegram-429-study.md`.

## Global Constraints

- **Priority order (owner decision, 2026-09-10):** `Tap` → `Conversation` → `Cosmetic`. Value decides who spends the allowance, never arrival order.
- **Only a superseded cosmetic repaint may be discarded.** A conversation message is never lost, even at the cost of delay.
- **All outbound traffic goes through the door.** The single exception is `Answer_CallbackQuery_Async`, which stays inline: Telegram invalidates a callback query after about ten seconds, so queueing it guarantees lateness.
- **The tick must never `await` a Telegram call.** This is the invariant the whole plan exists to establish; any task that leaves one behind has failed.
- **No queue persistence.** The tailer's offset advances only on a *pump-confirmed* delivery; that is what makes a crash lose nothing. See Task 8 — it is the one correctness constraint that cannot be compromised.
- **`retry_after` is honoured in full**, clamped by the existing `TokenBucket_Gate.MAXIMUM_HONOURED_RETRY_AFTER_SECONDS` (300). The 2 s / 10 s inline ceilings exist only because a tick was being held; the pump is not in the tick, so it does not use them.
- **Pure policy, thin shell.** `BridgeEngineModel` is `internal sealed` with no `InternalsVisibleTo`, so every decision that deserves a test lives in a pure static class over injected `nowUtc`, exactly like `TokenBucket_Gate`, `TelegramAttempt_Gate` and `OutboundCooldown_Gate`.
- **One supergroup, therefore one pump.** Telegram's per-chat ceiling applies to the whole supergroup, topics included, so a single serial pump is correct. `ChatKey` is carried anyway so a second chat would need no redesign.
- **House style:** long XML-doc/comment blocks stating WHY, with dated measured evidence. Read a neighbouring file before writing a new one.
- **Measured facts available for comments** (VPS, build `481efc9`): 388 HTTP 429 in the hour to 22:38 on 2026-09-10 and 378 in the hour to 09:27 on 2026-09-11 — twelve hours unabated, of which ~96% are two topic status lines re-attempted at the tick rate against a `retry_after` of 20–34 s.
- **Do not merge Task 6 and later into `ours/integration` until slice 1 (`stage/17`) has been observed in production.** Production still runs the 2026-09-10 20:56 build, which predates every fix; landing the migration on top of an unobserved slice 1 would make production jump two large steps at once. Tasks 1–5 add no behaviour and may merge freely.

---

## File Structure

**New** — all under `AIOrchestratorCoreLib/Telegram/Outbound/`:

| File | Responsibility |
|---|---|
| `OutboundBands.cs` | The three priority bands, ordered so that `<` means "more important". |
| `OutboundKinds.cs` | `Job` vs `Slot` — the one distinction the whole design turns on. |
| `OutboundIntent.cs` | An immutable description of one thing to say, plus the delegate that says it and the callback that reports the outcome. |
| `OutboundOutcome.cs` | What happened to an intent: delivered (with the new message id, if any) or failed (with the exception and the attempt count). |
| `OutboundQueue.cs` | The store. Jobs FIFO per order key; slots a dictionary keyed by slot key, where publishing overwrites. Stateful, locked, no clock of its own. |
| `OutboundQueue_Planner.cs` | **Pure.** Given the queue's shape and `nowUtc`, which intent goes next — or none. |
| `OutboundPump.cs` | The only caller of `ITelegramApiClient` for queued traffic. Drains, honours cooldowns and buckets, retries on `retry_after`, reports outcomes. |
| `IOutboundPublisher.cs` | The two-method surface the engine sees: `Publish(intent)` and `Publish_Slot(intent)`. Keeps the engine ignorant of the queue's internals. |

**Modified:**

| File | Change |
|---|---|
| `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` | Pump lifecycle (Task 5); then one migration per surface (Tasks 6–10). |
| `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngine_Factory.cs` | Construct the queue + pump and hand the publisher to the engine (Task 5). |

**Tests** mirror the source paths under `AIOrchestratorCoreLib.Tests/Telegram/Outbound/`, plus engine-level behaviour in `AIOrchestratorCoreLib.Tests/Bridge/TheDoorHoldsTheLineTests.cs` (Task 11).

---

### Task 1: The vocabulary — bands, kinds, intent, outcome

Four small types, no behaviour. They exist first because every later task's signatures name them.

**Files:**
- Create: `AIOrchestratorCoreLib/Telegram/Outbound/OutboundBands.cs`
- Create: `AIOrchestratorCoreLib/Telegram/Outbound/OutboundKinds.cs`
- Create: `AIOrchestratorCoreLib/Telegram/Outbound/OutboundOutcome.cs`
- Create: `AIOrchestratorCoreLib/Telegram/Outbound/OutboundIntent.cs`
- Test: `AIOrchestratorCoreLib.Tests/Telegram/Outbound/OutboundBandsTests.cs`

**Interfaces:**
- Consumes: `ITelegramApiClient` (existing), `OutboundCooldown_Keys` (existing, from stage/17).
- Produces: `OutboundBands`, `OutboundKinds`, `OutboundOutcome`, `OutboundIntent` — used by every task below.

- [ ] **Step 1: Write the failing test**

`AIOrchestratorCoreLib.Tests/Telegram/Outbound/OutboundBandsTests.cs`:

```csharp
using Xunit;
using AIOrchestratorCoreLib.Telegram.Outbound;

namespace AIOrchestratorCoreLib.Tests.Telegram.Outbound;

/// <summary>
/// THE ORDER IS THE OWNER'S DECISION (2026-09-10), and it is pinned as a number rather than left to
/// a comment, because the scheduler compares these values directly: a band renumbered by a later
/// hand would silently invert the priority this whole change exists to establish.
/// </summary>
public class OutboundBandsTests
{
    [Fact]
    public void TheOwnersTapOutranksEverything()
    {
        Assert.True(OutboundBands.Tap < OutboundBands.Conversation);
        Assert.True(OutboundBands.Conversation < OutboundBands.Cosmetic);
    }

    [Fact]
    public void TheBandsAreExactlyThree()
    {
        Assert.Equal(3, Enum.GetValues<OutboundBands>().Length);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~OutboundBands"`
Expected: FAIL — `CS0246: The type or namespace name 'OutboundBands' could not be found`.

- [ ] **Step 3: Write minimal implementation**

`OutboundBands.cs`:

```csharp
namespace AIOrchestratorCoreLib.Telegram.Outbound;

/// <summary>
/// WHO GOES FIRST WHEN THE ALLOWANCE IS SCARCE — the owner's ruling of 2026-09-10, and the reason
/// the door exists at all.
///
/// <para>
/// LOWER IS MORE IMPORTANT, so the scheduler can compare these directly. The order is not a
/// preference: measured on the VPS on 2026-09-10, the owner's tap — one human event, worth more
/// than anything else on the wire — was handled one-shot and gave up on its first refusal, while a
/// status line repaint worth nothing when the content has not moved retried every 2 s for twelve
/// hours. The surface with no value held the allowance open and the surface the owner was looking
/// at got what was left of it, which during a cooldown window is nothing.
/// </para>
/// </summary>
public enum OutboundBands
{
    /// <summary>The owner just did something and is looking at the screen: a tap's rewrite, its receipt.</summary>
    Tap = 0,

    /// <summary>The conversation itself — agents' messages, alerts the owner must see. Never dropped.</summary>
    Conversation = 1,

    /// <summary>Status lines, topic names, the dashboard. Useful; worthless when superseded.</summary>
    Cosmetic = 2,
}
```

`OutboundKinds.cs`:

```csharp
namespace AIOrchestratorCoreLib.Telegram.Outbound;

/// <summary>
/// THE ONE DISTINCTION THE DESIGN TURNS ON.
///
/// <para>
/// A JOB must be delivered, and in order: it is a thing that happened, and the record of it is the
/// channel file, which is why losing one is not recoverable by repainting. A SLOT is the current
/// value of something — a status line, a topic name, the dashboard — so a newer one makes an older
/// one WORTHLESS rather than late. Publishing a slot therefore overwrites any intent still waiting
/// under the same key, which is coalescing expressed as a data structure rather than as a rule
/// somebody has to remember.
/// </para>
/// </summary>
public enum OutboundKinds
{
    Job,
    Slot,
}
```

`OutboundOutcome.cs`:

```csharp
namespace AIOrchestratorCoreLib.Telegram.Outbound;

/// <summary>
/// WHAT HAPPENED TO ONE INTENT, reported by the pump to whoever published it.
///
/// <para>
/// THIS IS THE LOAD-BEARING TYPE FOR CRASH SAFETY. The tailer's offset advances only when a
/// delivery is CONFIRMED, and after the migration the confirmation arrives here rather than from
/// the tick's own return value. Publishing is not delivering: a version of this change that settles
/// the tailer on a successful enqueue loses messages on a restart, silently, and only on the day it
/// matters.
/// </para>
/// </summary>
public sealed record OutboundOutcome(
    bool Delivered,
    long? MessageId,
    Exception? Failure,
    int Attempts);
```

`OutboundIntent.cs`:

```csharp
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;

namespace AIOrchestratorCoreLib.Telegram.Outbound;

/// <summary>
/// ONE THING TO SAY TO TELEGRAM, and everything the scheduler needs to decide when to say it.
///
/// <para>
/// THE CALL RIDES AS A DELEGATE, deliberately. Re-encoding fifteen API methods as data would be a
/// second description of the wire that can drift from the first; instead the intent carries the
/// call untouched, and <see cref="OutboundQueue_Planner"/> — which is the part worth testing —
/// decides purely over the descriptive fields and never looks at <see cref="Send"/>.
/// </para>
/// </summary>
/// <param name="Band">Priority. See <see cref="OutboundBands"/>.</param>
/// <param name="Kind">Whether a newer intent makes this one worthless (<see cref="OutboundKinds.Slot"/>) or not.</param>
/// <param name="ChatKey">
/// The serialisation key. One supergroup means one key today; it is carried so a second chat needs
/// no redesign, and so the cooldown for a send has something to be keyed on.
/// </param>
/// <param name="OrderKey">
/// For a job: what it must stay in order behind — the channel file path for a mirrored append. Two
/// appends on one channel must arrive in the order they were written; two appends on DIFFERENT
/// channels must not block each other, which a single global FIFO could not express.
/// </param>
/// <param name="SlotKey">For a slot: the key a newer intent overwrites. Null for a job.</param>
/// <param name="CooldownTarget">
/// The door this call knocks on, from <see cref="OutboundCooldown_Keys"/> — the message for an
/// edit, the chat for a send.
/// </param>
/// <param name="Send">The call itself. Returns the new message id when the call mints one.</param>
/// <param name="OnOutcome">
/// Reported after the pump has finished with this intent, delivered or not. This is where a message
/// id is remembered and where a tailer offset is settled.
/// </param>
/// <param name="EnqueuedUtc">When it was published, for the age diagnostics and for FIFO ties.</param>
public sealed record OutboundIntent(
    OutboundBands Band,
    OutboundKinds Kind,
    string ChatKey,
    string? OrderKey,
    string? SlotKey,
    string CooldownTarget,
    Func<ITelegramApiClient, CancellationToken, Task<long?>> Send,
    Action<OutboundOutcome>? OnOutcome,
    DateTime EnqueuedUtc)
{
    /// <summary>A job in the conversation band, ordered behind everything on the same channel.</summary>
    public static OutboundIntent Job(
        OutboundBands band,
        string chatKey,
        string orderKey,
        string cooldownTarget,
        Func<ITelegramApiClient, CancellationToken, Task<long?>> send,
        Action<OutboundOutcome>? onOutcome,
        DateTime nowUtc)
    {
        return new OutboundIntent(band, OutboundKinds.Job, chatKey, orderKey, SlotKey: null, cooldownTarget, send, onOutcome, nowUtc);
    }

    /// <summary>A slot: the current value of a surface, which a newer value replaces outright.</summary>
    public static OutboundIntent Slot(
        OutboundBands band,
        string chatKey,
        string slotKey,
        string cooldownTarget,
        Func<ITelegramApiClient, CancellationToken, Task<long?>> send,
        Action<OutboundOutcome>? onOutcome,
        DateTime nowUtc)
    {
        return new OutboundIntent(band, OutboundKinds.Slot, chatKey, OrderKey: null, slotKey, cooldownTarget, send, onOutcome, nowUtc);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~OutboundBands"`
Expected: PASS — 2 tests.

- [ ] **Step 5: Commit**

```bash
git add AIOrchestratorCoreLib/Telegram/Outbound AIOrchestratorCoreLib.Tests/Telegram/Outbound
git commit -m "feat(telegram): the vocabulary of the door — bands, jobs, slots, outcomes"
```

---

### Task 2: The store — jobs queue, slots overwrite

**Files:**
- Create: `AIOrchestratorCoreLib/Telegram/Outbound/OutboundQueue.cs`
- Test: `AIOrchestratorCoreLib.Tests/Telegram/Outbound/OutboundQueueTests.cs`

**Interfaces:**
- Consumes: `OutboundIntent`, `OutboundKinds`, `OutboundBands` (Task 1).
- Produces:
  - `void Publish(OutboundIntent intent)` — appends a job, or overwrites a slot.
  - `IReadOnlyList<OutboundIntent> Snapshot()` — every intent currently waiting: for each order key its **head only**, plus every slot. The planner sees only what is actually eligible to go next, which is what makes head-of-line blocking a property of the store rather than a rule in the scheduler.
  - `bool Remove(OutboundIntent intent)` — drops a delivered or abandoned intent. Returns false when a slot was already overwritten while in flight, which is not an error: it means a newer value won.
  - `int Count_Waiting()` — for diagnostics.
  - `int Count_Dropped_BySupersession()` — how many slots were overwritten before they ever went out. This is the number that proves coalescing is working.

- [ ] **Step 1: Write the failing test**

```csharp
using Xunit;
using AIOrchestratorCoreLib.Telegram.Outbound;

namespace AIOrchestratorCoreLib.Tests.Telegram.Outbound;

public class OutboundQueueTests
{
    static readonly DateTime NOW = new(2026, 9, 11, 9, 27, 0, DateTimeKind.Utc);

    static OutboundIntent Slot(string key, DateTime at, OutboundBands band = OutboundBands.Cosmetic) =>
        OutboundIntent.Slot(band, "chat:1", key, "msg:1", (_, _) => Task.FromResult<long?>(null), null, at);

    static OutboundIntent Job(string orderKey, DateTime at, OutboundBands band = OutboundBands.Conversation) =>
        OutboundIntent.Job(band, "chat:1", orderKey, "chat:1", (_, _) => Task.FromResult<long?>(null), null, at);

    [Fact]
    public void A_NEWER_SLOT_REPLACES_AN_OLDER_ONE_THAT_HAS_NOT_GONE_OUT()
    {
        // The coalescing that makes N topics cost at most N pending repaints instead of a backlog
        // that grows with the tick rate. Painting a superseded state is always waste.
        var queue = new OutboundQueue();

        queue.Publish(Slot("statusline:fincanva-5", NOW));
        queue.Publish(Slot("statusline:fincanva-5", NOW.AddSeconds(2)));

        var waiting = queue.Snapshot();

        Assert.Single(waiting);
        Assert.Equal(NOW.AddSeconds(2), waiting[0].EnqueuedUtc);
        Assert.Equal(1, queue.Count_Dropped_BySupersession());
    }

    [Fact]
    public void TWO_JOBS_ON_ONE_CHANNEL_KEEP_THEIR_ORDER_AND_ONLY_THE_HEAD_IS_OFFERED()
    {
        // A message that retries must not be overtaken by the next one on the same channel.
        var queue = new OutboundQueue();

        queue.Publish(Job("/channel/a.md", NOW));
        queue.Publish(Job("/channel/a.md", NOW.AddSeconds(1)));

        var waiting = queue.Snapshot();

        Assert.Single(waiting);
        Assert.Equal(NOW, waiting[0].EnqueuedUtc);
        Assert.Equal(2, queue.Count_Waiting());
    }

    [Fact]
    public void TWO_JOBS_ON_DIFFERENT_CHANNELS_DO_NOT_BLOCK_EACH_OTHER()
    {
        var queue = new OutboundQueue();

        queue.Publish(Job("/channel/a.md", NOW));
        queue.Publish(Job("/channel/b.md", NOW));

        Assert.Equal(2, queue.Snapshot().Count);
    }

    [Fact]
    public void A_JOB_IS_NEVER_DROPPED_BY_SUPERSESSION()
    {
        // The owner's second decision (2026-09-10): only a superseded cosmetic repaint may be
        // discarded. A conversation message is never lost, even at the cost of delay.
        var queue = new OutboundQueue();

        queue.Publish(Job("/channel/a.md", NOW));
        queue.Publish(Job("/channel/a.md", NOW.AddSeconds(1)));

        Assert.Equal(0, queue.Count_Dropped_BySupersession());
    }

    [Fact]
    public void REMOVING_THE_HEAD_OFFERS_THE_NEXT_ONE()
    {
        var queue = new OutboundQueue();
        var first = Job("/channel/a.md", NOW);

        queue.Publish(first);
        queue.Publish(Job("/channel/a.md", NOW.AddSeconds(1)));

        Assert.True(queue.Remove(first));
        Assert.Equal(NOW.AddSeconds(1), queue.Snapshot()[0].EnqueuedUtc);
    }

    [Fact]
    public void REMOVING_A_SLOT_THAT_WAS_OVERWRITTEN_IN_FLIGHT_IS_NOT_AN_ERROR()
    {
        // The pump takes a slot, and while it is on the wire a newer value is published. Removing
        // the one it sent must not delete the newer one, and must not throw: it simply lost.
        var queue = new OutboundQueue();
        var taken = Slot("statusline:fincanva-5", NOW);

        queue.Publish(taken);
        queue.Publish(Slot("statusline:fincanva-5", NOW.AddSeconds(2)));

        Assert.False(queue.Remove(taken));
        Assert.Single(queue.Snapshot());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~OutboundQueueTests"`
Expected: FAIL — `CS0246: 'OutboundQueue' could not be found`.

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace AIOrchestratorCoreLib.Telegram.Outbound;

/// <summary>
/// WHAT IS WAITING TO BE SAID. Jobs keep their order; slots keep only their latest value.
///
/// <para>
/// NOT PERSISTED, and that is a design choice rather than an omission. The durable record of the
/// conversation is the channel files, and the tailer's offset advances only on a pump-confirmed
/// delivery (see <see cref="OutboundOutcome"/>), so a process that dies with a full queue re-emits
/// exactly what did not go out. A second durable queue could only disagree with the first.
/// </para>
/// <para>
/// SNAPSHOT OFFERS ONLY WHAT IS ELIGIBLE — each order key's HEAD, plus every slot — so head-of-line
/// blocking is a property of this class and not a rule the scheduler has to remember.
/// </para>
/// <para>
/// LOCKED: the mirror tick, the inbound loop and the pump all touch this at once.
/// </para>
/// </summary>
public sealed class OutboundQueue
{
    readonly Lock _lock = new();
    readonly Dictionary<string, List<OutboundIntent>> _jobsByOrderKey = [];
    readonly Dictionary<string, OutboundIntent> _slotsByKey = [];

    int _droppedBySupersession;

    public void Publish(OutboundIntent intent)
    {
        lock (_lock)
        {
            if (intent.Kind == OutboundKinds.Slot)
            {
                var key = intent.SlotKey
                    ?? throw new ArgumentException("a slot must carry a slot key", nameof(intent));

                if (_slotsByKey.ContainsKey(key))
                    _droppedBySupersession++;

                _slotsByKey[key] = intent;

                return;
            }

            var orderKey = intent.OrderKey
                ?? throw new ArgumentException("a job must carry an order key", nameof(intent));

            if (!_jobsByOrderKey.TryGetValue(orderKey, out var queued))
                _jobsByOrderKey[orderKey] = queued = [];

            queued.Add(intent);
        }
    }

    public IReadOnlyList<OutboundIntent> Snapshot()
    {
        lock (_lock)
        {
            List<OutboundIntent> eligible = [.. _slotsByKey.Values];

            foreach (var queued in _jobsByOrderKey.Values)
            {
                if (queued.Count > 0)
                    eligible.Add(queued[0]);
            }

            return eligible;
        }
    }

    public bool Remove(OutboundIntent intent)
    {
        lock (_lock)
        {
            if (intent.Kind == OutboundKinds.Slot)
            {
                var key = intent.SlotKey!;

                // ReferenceEquals, not a value comparison: two repaints of an unchanged surface are
                // equal records, and removing "an equal one" would delete a newer value that has
                // not been sent. What is being removed is THIS intent, the one that went out.
                if (_slotsByKey.TryGetValue(key, out var current) && ReferenceEquals(current, intent))
                {
                    _slotsByKey.Remove(key);

                    return true;
                }

                return false;
            }

            var orderKey = intent.OrderKey!;

            if (_jobsByOrderKey.TryGetValue(orderKey, out var queued) && queued.Count > 0 && ReferenceEquals(queued[0], intent))
            {
                queued.RemoveAt(0);

                if (queued.Count == 0)
                    _jobsByOrderKey.Remove(orderKey);

                return true;
            }

            return false;
        }
    }

    public int Count_Waiting()
    {
        lock (_lock)
            return _slotsByKey.Count + _jobsByOrderKey.Values.Sum(queued => queued.Count);
    }

    /// <summary>
    /// How many repaints were replaced before they ever went out — the number that proves coalescing
    /// is doing its job, and the one to watch if the cosmetic band ever looks slow.
    /// </summary>
    public int Count_Dropped_BySupersession()
    {
        lock (_lock)
            return _droppedBySupersession;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~OutboundQueueTests"`
Expected: PASS — 6 tests.

- [ ] **Step 5: Commit**

```bash
git add AIOrchestratorCoreLib/Telegram/Outbound/OutboundQueue.cs AIOrchestratorCoreLib.Tests/Telegram/Outbound/OutboundQueueTests.cs
git commit -m "feat(telegram): the door's store — jobs keep their order, slots keep only their latest value"
```

---

### Task 3: The scheduler — pure, and the band exception lives here

**Files:**
- Create: `AIOrchestratorCoreLib/Telegram/Outbound/OutboundQueue_Planner.cs`
- Test: `AIOrchestratorCoreLib.Tests/Telegram/Outbound/OutboundQueuePlannerTests.cs`

**Interfaces:**
- Consumes: `OutboundIntent`, `OutboundBands` (Task 1); `OutboundCooldowns.Is_Held` (existing, stage/17).
- Produces: `static OutboundIntent? Decide_Next(IReadOnlyList<OutboundIntent> eligible, Func<string, bool> isHeld, OutboundBands? bandThatCausedTheHold, DateTime nowUtc)`.

**The rule, in words.** Among the intents whose cooldown target is free, take the most important; ties go to the oldest, so a band cannot starve itself. An intent whose target is held is skipped — **except** that a band strictly above `bandThatCausedTheHold` may still be chosen once, which is what lets the owner's tap through while a status line is in punishment. That exception is the spec's one explicit bet: it is **[unconfirmed]** whether attempting during a flood wait extends it, so one high-value call is allowed and thirty low-value ones a minute are not.

- [ ] **Step 1: Write the failing test**

```csharp
using Xunit;
using AIOrchestratorCoreLib.Telegram.Outbound;

namespace AIOrchestratorCoreLib.Tests.Telegram.Outbound;

public class OutboundQueuePlannerTests
{
    static readonly DateTime NOW = new(2026, 9, 11, 9, 27, 0, DateTimeKind.Utc);

    static OutboundIntent Intent(OutboundBands band, string target, DateTime at) =>
        OutboundIntent.Slot(band, "chat:1", $"slot:{band}:{target}", target, (_, _) => Task.FromResult<long?>(null), null, at);

    static bool NothingHeld(string _) => false;

    [Fact]
    public void THE_MOST_IMPORTANT_GOES_FIRST()
    {
        var chosen = OutboundQueue_Planner.Decide_Next(
            [Intent(OutboundBands.Cosmetic, "msg:1", NOW), Intent(OutboundBands.Tap, "msg:2", NOW.AddSeconds(5))],
            NothingHeld,
            bandThatCausedTheHold: null,
            NOW.AddSeconds(6));

        Assert.Equal(OutboundBands.Tap, chosen!.Band);
    }

    [Fact]
    public void WITHIN_A_BAND_THE_OLDEST_GOES_FIRST()
    {
        // Without this a band can starve itself: a surface that republishes constantly would always
        // present a fresher intent than the one that has been waiting.
        var chosen = OutboundQueue_Planner.Decide_Next(
            [Intent(OutboundBands.Cosmetic, "msg:2", NOW.AddSeconds(5)), Intent(OutboundBands.Cosmetic, "msg:1", NOW)],
            NothingHeld,
            bandThatCausedTheHold: null,
            NOW.AddSeconds(6));

        Assert.Equal("msg:1", chosen!.CooldownTarget);
    }

    [Fact]
    public void A_HELD_TARGET_IS_SKIPPED_AND_THE_NEXT_ONE_GOES()
    {
        var chosen = OutboundQueue_Planner.Decide_Next(
            [Intent(OutboundBands.Cosmetic, "msg:held", NOW), Intent(OutboundBands.Cosmetic, "msg:free", NOW.AddSeconds(1))],
            target => target == "msg:held",
            bandThatCausedTheHold: OutboundBands.Cosmetic,
            NOW.AddSeconds(2));

        Assert.Equal("msg:free", chosen!.CooldownTarget);
    }

    [Fact]
    public void NOTHING_ELIGIBLE_MEANS_NOTHING_GOES()
    {
        Assert.Null(OutboundQueue_Planner.Decide_Next(
            [Intent(OutboundBands.Cosmetic, "msg:held", NOW)],
            target => true,
            bandThatCausedTheHold: OutboundBands.Cosmetic,
            NOW));
    }

    [Fact]
    public void A_HIGHER_BAND_MAY_STILL_KNOCK_ON_A_HELD_DOOR()
    {
        // THE OWNER'S TAP GETS THROUGH while a status line is in punishment — the inversion this
        // whole change exists to remove. One attempt, no retries: it is [unconfirmed] whether
        // knocking during a flood wait extends it, and one high-value call is a risk worth taking
        // where thirty low-value ones a minute are not.
        var chosen = OutboundQueue_Planner.Decide_Next(
            [Intent(OutboundBands.Tap, "msg:held", NOW)],
            target => true,
            bandThatCausedTheHold: OutboundBands.Cosmetic,
            NOW);

        Assert.Equal(OutboundBands.Tap, chosen!.Band);
    }

    [Fact]
    public void A_LOWER_OR_EQUAL_BAND_MAY_NOT()
    {
        Assert.Null(OutboundQueue_Planner.Decide_Next(
            [Intent(OutboundBands.Cosmetic, "msg:held", NOW)],
            target => true,
            bandThatCausedTheHold: OutboundBands.Conversation,
            NOW));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~OutboundQueuePlanner"`
Expected: FAIL — `CS0246: 'OutboundQueue_Planner' could not be found`.

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace AIOrchestratorCoreLib.Telegram.Outbound;

/// <summary>
/// WHAT GOES NEXT — pure, so the whole priority rule can be tested without a clock, a queue or a
/// network, which is the only way anything in this engine has ever been testable.
/// </summary>
public static class OutboundQueue_Planner
{
    /// <summary>
    /// The most important intent whose door is open; ties to the oldest, so a band that republishes
    /// constantly cannot starve its own backlog.
    ///
    /// <para>
    /// THE BAND EXCEPTION. An intent whose target is held is skipped, unless its band is strictly
    /// above <paramref name="bandThatCausedTheHold"/> — the owner's tap goes out while a status
    /// line's punishment runs. It is [unconfirmed] whether knocking during a flood wait extends it;
    /// one high-value call is a risk worth taking, thirty low-value ones a minute are not. If the
    /// measurement ever shows windows lengthening, this is the line to revisit.
    /// </para>
    /// </summary>
    public static OutboundIntent? Decide_Next(
        IReadOnlyList<OutboundIntent> eligible,
        Func<string, bool> isHeld,
        OutboundBands? bandThatCausedTheHold,
        DateTime nowUtc)
    {
        OutboundIntent? best = null;

        foreach (var intent in eligible)
        {
            if (isHeld(intent.CooldownTarget) && !May_KnockAnyway(intent.Band, bandThatCausedTheHold))
                continue;

            if (best == null
                || intent.Band < best.Band
                || (intent.Band == best.Band && intent.EnqueuedUtc < best.EnqueuedUtc))
            {
                best = intent;
            }
        }

        return best;
    }

    static bool May_KnockAnyway(OutboundBands band, OutboundBands? bandThatCausedTheHold)
    {
        return bandThatCausedTheHold != null && band < bandThatCausedTheHold.Value;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~OutboundQueuePlanner"`
Expected: PASS — 6 tests.

- [ ] **Step 5: Commit**

```bash
git add AIOrchestratorCoreLib/Telegram/Outbound/OutboundQueue_Planner.cs AIOrchestratorCoreLib.Tests/Telegram/Outbound/OutboundQueuePlannerTests.cs
git commit -m "feat(telegram): the door's scheduler, and the band that may knock anyway"
```

---

### Task 4: The pump — the only thing that calls Telegram, and it may finally wait

**Files:**
- Create: `AIOrchestratorCoreLib/Telegram/Outbound/IOutboundPublisher.cs`
- Create: `AIOrchestratorCoreLib/Telegram/Outbound/OutboundPump.cs`
- Test: `AIOrchestratorCoreLib.Tests/Telegram/Outbound/OutboundPumpTests.cs`

**Interfaces:**
- Consumes: `OutboundQueue` (Task 2), `OutboundQueue_Planner` (Task 3), `OutboundCooldowns` (stage/17), `ITelegramApiClient`, `ITelegramSendBudget`.
- Produces:
  - `interface IOutboundPublisher { void Publish(OutboundIntent intent); int Count_Waiting(); }`
  - `sealed class OutboundPump : IOutboundPublisher, IAsyncDisposable` with `Task Run_Async(CancellationToken cancellationToken)`.

**The rules the pump obeys**, each with the reason it is not negotiable:

1. **It honours `retry_after` in full** (clamped to 300 s by `TokenBucket_Gate.Read_RetryAfter`). The 2 s and 10 s inline ceilings in the client exist only because a tick was being held; nothing is held here.
2. **A refusal opens a cooldown and records the band that caused it**, so the planner's band exception has something to compare against.
3. **A job is retried until it is delivered or the process stops** — never dropped. A slot is attempted once per value: if it fails, a newer value will come next tick anyway, and re-sending a stale paint is the waste this design removes.
4. **Every intent reports its outcome exactly once**, delivered or not. Task 8 depends on this being exactly once: a duplicate "delivered" would advance a tailer offset twice.
5. **It sleeps when there is nothing to do**, woken by a `Channel` signal on publish — not a poll.

- [ ] **Step 1: Write the failing test**

```csharp
using Xunit;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.Outbound;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;

namespace AIOrchestratorCoreLib.Tests.Telegram.Outbound;

public class OutboundPumpTests
{
    [Fact]
    public async Task AN_INTENT_IS_SENT_AND_ITS_OUTCOME_IS_REPORTED_ONCE()
    {
        var cooldowns = new OutboundCooldowns();
        var queue = new OutboundQueue();
        using var stop = new CancellationTokenSource();

        var outcomes = new List<OutboundOutcome>();
        var sent = 0;

        var pump = new OutboundPump(queue, cooldowns, _ => Task.CompletedTask);
        var running = pump.Run_Async(stop.Token);

        pump.Publish(OutboundIntent.Slot(
            OutboundBands.Cosmetic, "chat:1", "statusline:a", "msg:1",
            (_, _) => { sent++; return Task.FromResult<long?>(7); },
            outcome => outcomes.Add(outcome),
            DateTime.UtcNow));

        await Wait_Until_Async(() => outcomes.Count == 1);
        await stop.CancelAsync();
        await running;

        Assert.Equal(1, sent);
        Assert.True(outcomes[0].Delivered);
        Assert.Equal(7, outcomes[0].MessageId);
    }

    [Fact]
    public async Task A_REFUSAL_OPENS_A_COOLDOWN_ON_THAT_TARGET()
    {
        // The note on the door is written by the pump now, from the pump's own refusal, so a second
        // surface aiming at the same message reads it instead of ringing the bell.
        var cooldowns = new OutboundCooldowns();
        var queue = new OutboundQueue();
        using var stop = new CancellationTokenSource();

        var outcomes = new List<OutboundOutcome>();
        var pump = new OutboundPump(queue, cooldowns, _ => Task.CompletedTask);
        var running = pump.Run_Async(stop.Token);

        pump.Publish(OutboundIntent.Slot(
            OutboundBands.Cosmetic, "chat:1", "statusline:a", "msg:1",
            (_, _) => throw new TelegramApiException(429, "Too Many Requests", retryAfterSeconds: 24),
            outcome => outcomes.Add(outcome),
            DateTime.UtcNow));

        await Wait_Until_Async(() => outcomes.Count == 1);
        await stop.CancelAsync();
        await running;

        Assert.False(outcomes[0].Delivered);
        Assert.True(cooldowns.Is_Held("msg:1", DateTime.UtcNow, out _));
    }

    [Fact]
    public async Task A_SLOT_IS_NOT_RETRIED_BUT_A_JOB_IS()
    {
        // A stale paint is worth nothing; a message of the conversation is worth everything. The
        // owner's second decision (2026-09-10), expressed in the pump.
        var cooldowns = new OutboundCooldowns();
        var queue = new OutboundQueue();
        using var stop = new CancellationTokenSource();

        var slotAttempts = 0;
        var jobAttempts = 0;

        var pump = new OutboundPump(queue, cooldowns, _ => Task.CompletedTask);
        var running = pump.Run_Async(stop.Token);

        pump.Publish(OutboundIntent.Slot(
            OutboundBands.Cosmetic, "chat:1", "statusline:a", "msg:slot",
            (_, _) => { slotAttempts++; throw new TelegramApiException(500, "Internal Server Error"); },
            null, DateTime.UtcNow));

        pump.Publish(OutboundIntent.Job(
            OutboundBands.Conversation, "chat:1", "/channel/a.md", "chat:1",
            (_, _) =>
            {
                jobAttempts++;

                if (jobAttempts < 3)
                    throw new TelegramApiException(500, "Internal Server Error");

                return Task.FromResult<long?>(1);
            },
            null, DateTime.UtcNow));

        await Wait_Until_Async(() => jobAttempts >= 3);
        await stop.CancelAsync();
        await running;

        Assert.Equal(1, slotAttempts);
        Assert.Equal(3, jobAttempts);
    }

    static async Task Wait_Until_Async(Func<bool> condition)
    {
        // The suite's own convention for a background worker: poll a predicate with a ceiling rather
        // than sleeping a guessed interval. See AIOrchestratorCoreLib.Tests for the existing helper
        // of the same name and reuse it if it is accessible from here.
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (condition())
                return;

            await Task.Delay(25);
        }

        throw new TimeoutException("the pump did not reach the expected state within 5 s");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~OutboundPump"`
Expected: FAIL — `CS0246: 'OutboundPump' could not be found`.

- [ ] **Step 3: Write minimal implementation**

`IOutboundPublisher.cs`:

```csharp
namespace AIOrchestratorCoreLib.Telegram.Outbound;

/// <summary>
/// WHAT THE ENGINE SEES OF THE DOOR: somewhere to leave an intention, and a depth it can report.
/// Deliberately two methods — the engine must not be able to reach the queue's internals, or the
/// forty call sites would start making scheduling decisions again, which is the defect this
/// replaces.
/// </summary>
public interface IOutboundPublisher
{
    void Publish(OutboundIntent intent);

    int Count_Waiting();
}
```

`OutboundPump.cs`:

```csharp
using System.Threading.Channels;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;

namespace AIOrchestratorCoreLib.Telegram.Outbound;

/// <summary>
/// THE ONLY THING THAT SAYS ANYTHING TO TELEGRAM for queued traffic — and the only thing in this
/// process allowed to wait as long as Telegram asks.
///
/// <para>
/// WHY IT EXISTS. Every outbound call used to be made inside the 2 s mirror tick, a sequential list
/// of some twenty steps. Telegram answers a rate limit with 20-34 s; the tick cannot wait that long
/// without stopping the mirror, the owner's deliveries and the deadline sweep, so the client capped
/// how long it would honour Telegram's own answer (2 s for edits, 10 s for sends) and threw the rest
/// to "the caller, where the per-channel backoff already lives". There was no per-channel backoff:
/// there were forty call sites with forty opinions, three of which had none at all. Measured on the
/// VPS: 388 HTTP 429 in the hour to 22:38 on 2026-09-10 and 378 in the hour to 09:27 on 09-11,
/// twelve hours unabated, ~96% of them two status lines re-attempted at the tick rate.
/// </para>
/// <para>
/// ONE PUMP, NOT ONE PER CHAT. Telegram's ceiling applies to the whole supergroup, topics included,
/// so serialising everything is correct rather than merely simple — and it is what makes the
/// priority bands meaningful, since a scarce allowance is only scarce if somebody has to queue for
/// it. It is a SCHEDULER, not a FIFO: an intent whose door is shut is skipped, not waited on.
/// </para>
/// </summary>
public sealed class OutboundPump : IOutboundPublisher
{
    readonly OutboundQueue _queue;
    readonly OutboundCooldowns _cooldowns;
    readonly Func<OutboundIntent, Task> _dispatch;
    readonly Channel<byte> _wakeUp = Channel.CreateBounded<byte>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    /// <summary>
    /// The band whose refusal opened the most recent window. The planner compares against it so a
    /// more important band may still knock; null when nothing is held.
    /// </summary>
    OutboundBands? _bandThatCausedTheHold;

    /// <summary>
    /// <paramref name="dispatch"/> is the seam: in production it calls
    /// <see cref="OutboundIntent.Send"/> with the real client, and a test hands in a fake without
    /// needing an <see cref="ITelegramApiClient"/> at all.
    /// </summary>
    public OutboundPump(OutboundQueue queue, OutboundCooldowns cooldowns, Func<OutboundIntent, Task> dispatch)
    {
        _queue = queue;
        _cooldowns = cooldowns;
        _dispatch = dispatch;
    }

    public void Publish(OutboundIntent intent)
    {
        _queue.Publish(intent);
        _wakeUp.Writer.TryWrite(0);
    }

    public int Count_Waiting() => _queue.Count_Waiting();

    public async Task Run_Async(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var next = OutboundQueue_Planner.Decide_Next(
                    _queue.Snapshot(),
                    target => _cooldowns.Is_Held(target, DateTime.UtcNow, out _),
                    _bandThatCausedTheHold,
                    DateTime.UtcNow);

                if (next == null)
                {
                    // NOTHING ELIGIBLE. Either the queue is empty — wait to be woken — or everything
                    // in it is behind a shut door, in which case waiting for the shortest window to
                    // expire is the only useful thing to do. One second is short enough that a door
                    // opening is not felt and long enough that a held queue is not a spin.
                    await Wait_ForWorkOrASecond_Async(cancellationToken);

                    continue;
                }

                await Attempt_Async(next, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown. Whatever is still queued was never confirmed, so the tailer offsets that
            // would have advanced did not: the restart re-emits it.
        }
    }

    async Task Attempt_Async(OutboundIntent intent, CancellationToken cancellationToken)
    {
        var attempts = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            attempts++;

            try
            {
                await _dispatch(intent);

                _queue.Remove(intent);
                _bandThatCausedTheHold = null;
                intent.OnOutcome?.Invoke(new OutboundOutcome(Delivered: true, MessageId: null, Failure: null, attempts));

                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception failure)
            {
                if (failure is TelegramApiException { StatusCode: 429 } rateLimited)
                {
                    _cooldowns.Note_RateLimited(intent.CooldownTarget, rateLimited.RetryAfterSeconds, DateTime.UtcNow);
                    _bandThatCausedTheHold = intent.Band;
                }

                // A SLOT IS ATTEMPTED ONCE PER VALUE. A newer one is already on its way from the
                // next tick, and re-sending a paint that is about to be superseded is exactly the
                // waste this design removes. A JOB stays where it is and is tried again: it is a
                // thing that happened, and the owner's ruling is that it is never lost.
                if (intent.Kind == OutboundKinds.Slot)
                {
                    _queue.Remove(intent);
                    intent.OnOutcome?.Invoke(new OutboundOutcome(Delivered: false, MessageId: null, failure, attempts));

                    return;
                }

                intent.OnOutcome?.Invoke(new OutboundOutcome(Delivered: false, MessageId: null, failure, attempts));

                return;
            }
        }
    }

    async Task Wait_ForWorkOrASecond_Async(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        timeout.CancelAfter(TimeSpan.FromSeconds(1));

        try
        {
            await _wakeUp.Reader.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The one-second ceiling, not a shutdown.
        }
    }
}
```

> **Note for the implementer — read this before writing the file.** The `Attempt_Async` above
> reports the outcome and returns after a single failure even for a job, which contradicts rule 3
> and the test `A_SLOT_IS_NOT_RETRIED_BUT_A_JOB_IS`. That is deliberate: the retry loop for jobs is
> the one piece of this class worth deriving from its own failing test rather than copying. Make the
> test pass by keeping the job in the queue and looping — honouring
> `TokenBucket_Gate.Read_RetryAfter(rateLimited.RetryAfterSeconds)` as the delay before the next
> attempt, and a short fixed delay for a non-429 failure — and report the outcome exactly once, when
> the job is finally delivered or the pump is stopped. Rule 4 ("exactly once") is what Task 8
> depends on; a job that reports a failed outcome per attempt would settle a tailer offset early.

- [ ] **Step 4: Run test to verify it passes**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~OutboundPump"`
Expected: PASS — 3 tests.

- [ ] **Step 5: Commit**

```bash
git add AIOrchestratorCoreLib/Telegram/Outbound AIOrchestratorCoreLib.Tests/Telegram/Outbound
git commit -m "feat(telegram): the pump — one door, and it may finally wait as long as Telegram asks"
```

---

### Task 5: Lifecycle — the pump lives and dies with the bridge, and changes nothing yet

**Files:**
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngine_Factory.cs`
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs`
- Test: `AIOrchestratorCoreLib.Tests/Bridge/TheDoorHoldsTheLineTests.cs` (create; one test only at this task)

**Interfaces:**
- Consumes: `OutboundPump`, `OutboundQueue`, `IOutboundPublisher` (Task 4).
- Produces: a `_outbound` field of type `IOutboundPublisher` on the engine, and a pump task started with the mirror loop and stopped with it. **No call site is migrated in this task** — the queue stays empty and behaviour is unchanged, which is what makes this task independently reviewable.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task THE_PUMP_STOPS_WHEN_THE_BRIDGE_STOPS()
{
    // A pump that outlives the engine would keep a cancelled process alive and keep sending after
    // a shutdown had been promised. Started and stopped with the mirror loop, no separate switch.
    var engine = Build_Engine();

    using var stop = new CancellationTokenSource();
    var running = engine.Run_Async(stop.Token);

    await stop.CancelAsync();
    await running;

    Assert.True(running.IsCompleted);
}
```

Follow the construction used by the existing engine tests (`ScriptedInbound_Fake` + `BridgeEngine_Factory.Create` with `Create_Custom` timing) — copy `Build_Engine` from `AIOrchestratorCoreLib.Tests/Bridge/TheBridgeNeverLiesAboutDeliveryTests.cs` rather than inventing one.

- [ ] **Step 2: Run test to verify it fails**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj --filter "FullyQualifiedName~TheDoorHoldsTheLine"`
Expected: FAIL — the test file does not compile until `Build_Engine` exists.

- [ ] **Step 3: Write minimal implementation**

In `BridgeEngine_Factory`, construct `new OutboundQueue()`, then `new OutboundPump(queue, cooldowns, intent => intent.Send(client, ct))` and pass it to the engine's constructor as `IOutboundPublisher`. The cooldown book currently lives inside `TelegramApiClientModel` (stage/17); move it out to the factory and inject it into both the client and the pump, so the note on the door is one book rather than two. That move is the whole substance of this task and it must not change the client's behaviour: the client keeps reading and writing the same book, just one it was handed.

In `BridgeEngineModel`, start the pump where the mirror loop starts and await it on shutdown, using the same cancellation token.

- [ ] **Step 4: Run the whole suite**

Run: `export PATH="$HOME/.dotnet:$PATH" && dotnet test AIOrchestratorCoreLib.Tests/AIOrchestratorCoreLib.Tests.csproj`
Expected: 0 failed, 9 skipped — **compare the skipped names, not the count** (the documented set: two session-durability, two channel-compactor, one meeting-flag, one atomic-writer, three live-smoke).

- [ ] **Step 5: Commit**

```bash
git add AIOrchestratorCoreLib/Bridge/BridgeEngine AIOrchestratorCoreLib.Tests/Bridge/TheDoorHoldsTheLineTests.cs
git commit -m "feat(bridge): the door is built and wired, and nothing goes through it yet"
```

---

### Task 6: The status line goes through the door

**Files:**
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` (`Refresh_TopicStatusLines_Async`)
- Test: `AIOrchestratorCoreLib.Tests/Bridge/TheDoorHoldsTheLineTests.cs`

**Interfaces:**
- Consumes: `IOutboundPublisher` (Task 5), `OutboundIntent.Slot` (Task 1).
- Produces: nothing new. The status line stops awaiting Telegram.

This is the first migration and the one that pays for the whole change: 96% of the measured 429s are this surface. It is also the safest, because a status line is a slot — worthless when superseded — so the failure mode of a mistake here is a late repaint, not a lost message.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task A_STATUS_LINE_REFUSED_ONCE_IS_NOT_ATTEMPTED_AGAIN_BEFORE_ITS_DEADLINE()
{
    // The measured defect, pinned end to end at last: 376 of 388 refusals in one hour were this
    // surface asking again every 2 s against a window of 20-34 s. Needs the 429-scripting seam
    // added in stage/17 (`Rate_Limit_Edits_Of`), which is why no earlier test could express it.
    var engine = Build_Engine();

    _telegram.Rate_Limit_Edits_Of(messageId: <the status line's id>, retryAfterSeconds: 24);

    using var stop = new CancellationTokenSource();
    var running = engine.Run_Async(stop.Token);

    await Wait_Until_Async(() => _telegram.Count_EditAttempts_Of(<id>) >= 1);
    await Task.Delay(TimeSpan.FromSeconds(6));

    Assert.Equal(1, _telegram.Count_EditAttempts_Of(<id>));

    await stop.CancelAsync();
    await running;
}
```

Resolve `<the status line's id>` from the fake's recorded sends the way the neighbouring tests in `TheBridgeNeverLiesAboutDeliveryTests.cs` do; do not hard-code a literal.

- [ ] **Step 2: Run test to verify it fails**

Expected: FAIL — the count is greater than 1, because the surface still attempts on every tick.

- [ ] **Step 3: Write minimal implementation**

Replace the three awaited client calls in `Refresh_TopicStatusLines_Async` (the edit, the repost's delete, the send) with `_outbound.Publish(OutboundIntent.Slot(...))`, band `Cosmetic`, slot key `$"statusline:{session.OrchId}"`, cooldown target `OutboundCooldown_Keys.For_Message(messageId)` for an edit and `For_Chat(...)` for a send. Move what the `try`/`catch` did into the `OnOutcome` callback: remembering the render key on success, `Forget_StatusLineMessage` on a message-gone failure, stamping `_statusLineFailedAtByOrchId` otherwise. The planner's own back-off and the `attemptDue` promotion guard stay exactly as they are — they decide whether to *publish*, which is now a cheap local decision.

- [ ] **Step 4: Run test to verify it passes, then the whole suite**

Run the filtered test, then the full suite (0 failed, 9 skipped, names compared).

- [ ] **Step 5: Commit**

```bash
git add AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs AIOrchestratorCoreLib.Tests/Bridge/TheDoorHoldsTheLineTests.cs
git commit -m "feat(bridge): the status line leaves its intention at the door instead of holding the tick"
```

---

### Task 7: Topic names and the dashboard follow

**Files:**
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` (`Sync_TopicNames_Inside_Gate_Async`, `Sync_GeneralTopicName_BestEffort_Async`, `Push_GeneralDashboard_Async`)
- Test: `AIOrchestratorCoreLib.Tests/Bridge/TheDoorHoldsTheLineTests.cs`

**Interfaces:** as Task 6.

Three more cosmetic slots, same shape. Slot keys: `$"topicname:{orchId}"`, `"topicname:general"`, `"dashboard"`. Note that `Push_GeneralDashboard_Async` already has the correct shape (its own failure back-off consulted *before* the call, and a decider that returns `None` when the text has not moved) — it is the model citizen of this file, so migrating it is mechanical and its existing guards stay.

- [ ] **Step 1: Write the failing test** — assert that a rate-limited topic-name sync does not re-attempt within its window, mirroring Task 6's test with `Rate_Limit_Sends_Containing` / the topic-name fake hook.
- [ ] **Step 2: Run it and watch it fail.**
- [ ] **Step 3: Migrate the three surfaces** to `Publish(OutboundIntent.Slot(...))`, moving each `try`/`catch` body into `OnOutcome`.
- [ ] **Step 4: Filtered test, then the full suite.**
- [ ] **Step 5: Commit** — `feat(bridge): the topic names and the dashboard go through the door`.

---

### Task 8: The conversation — jobs, and the offset that must not move early

**Files:**
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` (`Mirror_Append_Async:3673`, `Settle_MirrorAttempt_Async:1837`, the tick's `foreach (var append in pollResult.CompletedAppends)`)
- Test: `AIOrchestratorCoreLib.Tests/Bridge/TheDoorHoldsTheLineTests.cs`

**Interfaces:**
- Consumes: `OutboundIntent.Job`, `OutboundOutcome` (Task 1).
- Produces: nothing new.

**This is the task that can lose the owner's data, and the only one in the plan where a mistake is not recoverable by a repaint.** Today the tick calls `Mirror_Append_Async` (which returns `bool delivered`) and then `Settle_MirrorAttempt_Async(append, delivered, ct)`, which advances or holds the tailer's offset. After this task the *pump* decides `delivered`, and the settle must happen in `OnOutcome`. Order key is the channel file path, so two appends on one channel keep their order and two channels never block each other.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task A_MIRRORED_APPEND_THAT_WAS_NEVER_SENT_IS_RE_DELIVERED_AFTER_A_RESTART()
{
    // The invariant the whole no-persistence design rests on: publishing is not delivering. Script
    // the send to fail forever, stop the engine with the job still queued, build a second engine on
    // the same folders, and assert the append is offered again — i.e. the offset never moved.
}

[Fact]
public async Task TWO_APPENDS_ON_ONE_CHANNEL_ARRIVE_IN_ORDER_EVEN_WHEN_THE_FIRST_RETRIED()
{
    // Script the first send to fail twice then succeed; assert the recorded order of the two texts
    // is the order they were written, not the order they happened to succeed in.
}
```

- [ ] **Step 2: Run them and watch them fail.**
- [ ] **Step 3: Migrate.** In the tick, replace `var delivered = await Mirror_Append_Async(...)` + `await Settle_MirrorAttempt_Async(append, delivered, ct)` with a `Publish(OutboundIntent.Job(...))` whose `Send` does what `Mirror_Append_Async` did and whose `OnOutcome` calls `Settle_MirrorAttempt_Async(append, outcome.Delivered, ct)`. Keep `Is_MirrorAttemptDue` and `Raise_OrchestrationActivity` where they are: the first decides whether to publish, the second is local bookkeeping.
- [ ] **Step 4: Both tests, then the full suite.**
- [ ] **Step 5: Commit** — `fix(bridge): the tailer's offset moves when the pump confirms, not when the tick asks`.

---

### Task 9: The three alerts that never backed off at all

**Files:**
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` (`Send_CrashLoopAlerts_Async`, `Send_StallAlerts_Async`, `Send_BudgetAlerts_Async`)
- Test: `AIOrchestratorCoreLib.Tests/Bridge/TheDoorHoldsTheLineTests.cs`

These three set their "alerted" flag only on a confirmed send, so a failing send re-fires **every 2 s with no back-off of any kind** — a defect no earlier fix touches, because it is not a status line and nobody measured it. They become `Conversation` jobs; the flag is set in `OnOutcome` when `Delivered` is true, which preserves today's honest "never claim an alert was delivered when it was not" while removing the hammering.

Also fix, in passing and stated in the commit: `Send_BudgetAlerts_Async` iterates several thresholds inside one `try`, so a failure on the 90% alert can swallow the 100% one — its own comment says so. Publishing makes each threshold its own intent, which dissolves the problem rather than papering over it.

- [ ] **Step 1: Write the failing test** — a scripted send failure must produce exactly one attempt per tick-window, not one per tick; and a swallowed higher threshold must still be published.
- [ ] **Step 2: Run and watch fail.**
- [ ] **Step 3: Migrate the three, one intent per alert.**
- [ ] **Step 4: Filtered, then full suite.**
- [ ] **Step 5: Commit** — `fix(bridge): the three alerts that hammered now queue, and a swallowed threshold is its own intent`.

---

### Task 10: The owner's band, and the exception that stays inline

**Files:**
- Modify: `AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` (`Record_AnsweredQuestion_BestEffort_Async`, `Retry_AnsweredQuestionEdit_Async`, `Remove_Buttons_BestEffort_Async`, the hold receipts)
- Test: `AIOrchestratorCoreLib.Tests/Bridge/TheDoorHoldsTheLineTests.cs`

The tap's rewrite and its receipts become `Tap` band intents — which is what makes the planner's band exception real: they go out while a status line's window holds. `RateLimitedRetry_Policy` and the detached retry task can then be **deleted**, because the pump does that job for every band: keep the constants' reasoning in the pump's comment so the measured evidence is not lost with the file.

**`Answer_CallbackQuery_Async` stays inline and is not published.** Telegram invalidates a callback query after about ten seconds. Add a guard test that fails if it ever acquires a `Publish` call, in the shape of the existing source-reading guard `TelegramApiClientThrowSiteTests` — a rule that is only a comment is a rule that gets undone by the next person who is tidying up.

- [ ] **Step 1: Write the failing tests** — a tap's rewrite goes out while a cosmetic window holds; and the source guard for `answerCallbackQuery`.
- [ ] **Step 2: Run and watch fail.**
- [ ] **Step 3: Migrate the tap surfaces; delete the bespoke retry; add the guard.**
- [ ] **Step 4: Filtered, then full suite.**
- [ ] **Step 5: Commit** — `feat(bridge): the owner's band goes first, and the callback answer stays inline on purpose`.

---

### Task 11: The invariant nobody can break by accident

**Files:**
- Create: `AIOrchestratorCoreLib.Tests/Bridge/TheTickNeverWaitsOnTelegramTests.cs`

**Interfaces:** consumes nothing; reads source text.

The plan's whole point is one sentence — *the tick must never await a Telegram call* — and after Tasks 6–10 it is true. A source-reading guard in the shape of `TelegramApiClientThrowSiteTests` keeps it true: read `BridgeEngineModel.cs`, find the mirror tick's method bodies, and fail if any of them contains `await _telegramClient.` or `await client.`. Allow the documented exception (`Answer_CallbackQuery_Async`) by name, in a list whose every entry carries a comment saying why.

- [ ] **Step 1: Write the test** (it should pass immediately if Tasks 6–10 are complete; if it fails, it has found a surface the migration missed — that is the point).
- [ ] **Step 2: Run it.** Expected: PASS, or a precise list of the call sites still in the tick.
- [ ] **Step 3: Migrate anything it names.**
- [ ] **Step 4: Full suite.**
- [ ] **Step 5: Commit** — `test(bridge): the tick may not await Telegram, and now it cannot`.

---

### Task 12: Say it in the record

**Files:**
- Modify: `docs/MODIFICHE-DEL-FORK.md`
- Modify: `docs/superpowers/specs/2026-09-10-one-door-to-telegram-design.md`

- [ ] **Step 1:** Add one entry to section 1 of the fork record, in the file's own form (`Com'era` / `Cos'è adesso` / `Perché` / `Cosa cambia` / `Dove`), in Italian, with no class or file names — the rules are at the top of that file, read them first. State the price: with many topics the cosmetic surfaces lag.
- [ ] **Step 2:** Move the door out of the record's section 7 ("what we are doing now") and into the real entry, per that section's own rule.
- [ ] **Step 3:** Mark §4.2–§4.5 of the design spec as implemented, and record the one deviation the implementation chose: the chat-wide hold from §4.1 is expressed as the planner's band exception rather than as a second cooldown scope.
- [ ] **Step 4:** Full suite, then commit — `docs: the door, in the record`.

---

## Self-Review

**Spec coverage.** §4.1's remaining half (chat-wide hold + band exception) → Task 3, expressed as the planner's band comparison; §4.2's intents/queue/pump → Tasks 1–4; the per-chat serialisation → Task 4 (one pump, reasoned); §4.3's two invariants → Task 8 (offset) and Task 2 (order); §4.4's exception and the DND non-change → Task 10 (guard) and untouched by construction, since the tick still does not tail under mute; §4.5's scalability claim → Task 2's supersession counter is what would prove it; §5's five behavioural tests → Tasks 6, 8 (×2), 10, 11; §6's acceptance criteria → the full-suite step in every task, plus the production measurement, which is Nathan's.

**Gap found and closed while reviewing:** the spec never said what happens to `RateLimitedRetry_Policy` once the pump retries for every band. Task 10 now deletes it and requires its measured reasoning to be carried into the pump's comment, so the evidence is not lost with the file.

**Gap acknowledged and deliberately left open:** Tasks 7, 9, 10, 11 give test *intent* and migration shape rather than complete test code. Their code depends on identifiers (the fake's hooks, the engine's builders) that Tasks 5 and 6 establish; writing literal code now would mean inventing names that the earlier tasks will fix. An executor must write those tests first and watch them fail, as Tasks 1–6 show in full.

**Type consistency.** `OutboundBands`, `OutboundKinds`, `OutboundIntent.Job/Slot`, `OutboundOutcome(Delivered, MessageId, Failure, Attempts)`, `OutboundQueue.Publish/Snapshot/Remove/Count_Waiting/Count_Dropped_BySupersession`, `OutboundQueue_Planner.Decide_Next(eligible, isHeld, bandThatCausedTheHold, nowUtc)`, `IOutboundPublisher.Publish/Count_Waiting`, `OutboundPump(queue, cooldowns, dispatch)` + `Run_Async(ct)` — checked against every later use in Tasks 5–11.

**One risk named for the executor.** Task 5 moves `OutboundCooldowns` out of `TelegramApiClientModel` and into the factory. That is a behaviour-preserving move on paper, and it is the kind of move this repo has a documented history of getting subtly wrong (a clock left behind, a map that is never cleared). Run the stage/17 tests — `OutboundCooldownsTests`, `TelegramSendBudgetTests`, `TelegramApiClientWireTests` — as part of Task 5's step 4, not only the full suite, and read their names in the output.
