# The brake that cannot be lifted — a spend guardrail with no release, and the wrong number under it

**Date:** 2026-09-11 · **Status:** PROPOSED, nothing built · **Owner:** Nathan
**Provenance:** a live incident on the VPS on 2026-09-11. The account was swapped, real weekly usage
fell to 70%, and dispatch stayed blocked on a reading of 95% taken from the **previous** account —
for a further 4 days 14 hours. Two AI-Orch sessions and one reviewer never started. It took six
hours and an operator with a shell to clear it.

**Copies read — re-stated 2026-09-12 00:31, because the first statement of it aged out in nine hours.**
As written, this spec read the branch at `dbb6e4e` and the daemon published from `9394971`. Both have
moved: `ours/integration` is now `66272a6` (37 commits, 14 of them authored by `AI Orchestrator bridge`
— the orchestrations committing their own work), and production was re-published from `66272a6` at
2026-09-12 00:18, service active since 00:27:06 [measured].

**The findings are unaffected, and that was checked rather than assumed** [measured]:
`git diff dbb6e4e..66272a6 -- AIOrchestratorCoreLib/Limits/DispatchPause_Gate.cs` is **empty**, and the
same range touches `BridgeEngineModel.cs` without changing a single line that mentions `DispatchPause`
(0 matching diff lines). `EngineStateSnapshot` / `EngineState_Serializer` gained 13 lines for an
unrelated persisted field. So every quotation below is still the running code — now at `66272a6`.

Live data: VPS `orch@159.195.254.120` — `.engine-state.json`, the seven
`.supervisor.limits.usage.json` probes, `journalctl -u aiorchestrator`, `/proc/<pid>/status`, `sudo -l`.

This paragraph is itself the point of §5 and of decision 18: a statement about which copy you read is
true when written and goes on being believed after it stops being true. This one was re-measured.

Every figure is tagged `[measured]` or `[estimate]`.

---

## 1. What happened

| time (CEST) | event |
|---|---|
| ~08:14 | dispatch pauses: `the rate_limits.seven_day.used_percentage window was at 95%`, until **2026-09-16 03:00 UTC** [measured] |
| 13:41 | owner asks for two sessions on AI-Orch. They queue and never start |
| 13:48–14:06 | the general supervisor misreads the cause twice — first Telegram congestion, then "resuming tonight at 03:00 UTC" |
| ~14:00 | the Claude account is swapped. Live probes fall to **70%** weekly [measured] |
| 14:09 | correct diagnosis reached: the stored instant is 16/9, not tonight |
| 14:13 | it edits `.engine-state.json` by hand. The daemon overwrites it |
| 14:16 | it reports it cannot restart the daemon — *no root, blocked by the kernel* |
| 14:24 | it reads the file again, sees 95% freshly written, and concludes **the block is probably correct** |
| 15:03 | an operator stops the daemon, nulls the two fields, starts it. Dispatch resumes [measured] |

Nothing in that sequence was a mistake of reasoning alone. Three defects in the app made the correct
conclusion unreachable from inside, and the correct action impossible from inside.

---

## 2. Defect 1 — while it holds, the pause never looks again

`BridgeEngineModel.Update_DispatchPause_Async` returns on its first line when the stored instant is
still in the future:

```csharp
if (Limits.DispatchPause_Gate.Is_Paused(pausedUntilUtc, nowUtc))
    return;
```

The comment above it explains why, and the reason is a real bug that was really fixed:

> Re-reading the probes here would let a still-high percentage extend the pause indefinitely past the
> reset it was measured against, which is how a five-hour pause becomes a permanent one.

**The guard is correct about EXTENDING and wrong about LIFTING.** It removed both directions to stop
one of them. So a pause is a promise made once, against a reading that may be minutes old — and an
account swap, a quota increase, or a simple misreading is unanswerable for as long as the promise
runs. Here that was 4 days 14 hours of blocked work on an account that was 25 points under the
threshold [measured].

**Change.** Re-evaluate on the throttled tick even while paused, and apply the result in one
direction only:

- a new decision that would resolve **earlier** than the stored instant replaces it;
- a decision that would resolve **later** is discarded, and the stored instant stands;
- **no reading at all** leaves the stored instant untouched (never lift on silence — §4).

The original defect stays fixed by construction: a still-high percentage can only ever re-propose a
later instant, which is discarded. A `pausedUntilUtc` is therefore a **ceiling**, never a floor.

---

## 3. Defect 2 — the only lever is outside the app, and no session can ever reach it

The general supervisor was right, and this was verified rather than taken on trust. The systemd unit
carries `NoNewPrivileges=yes`. That kernel bit is **inherited by every child and cannot be dropped**;
it neuters setuid, which is the only way `sudo` becomes root [measured]:

| process | `NoNewPrivs` |
|---|---|
| an operator's ssh shell | 0 |
| the daemon (PID 194340) | 1 |
| every spawned `claude` session | 1 |

```
$ setpriv --no-new-privs sudo -n systemctl status aiorchestrator
sudo: The "no new privileges" flag is set, which prevents sudo from running as root.
```

`orch` does hold a NOPASSWD sudoers entry for `systemctl {start|stop|restart|status} aiorchestrator`
[measured] — and **a session can never use it**, no matter what is added to sudoers. This is not a
misconfiguration to repair: it is the hardening working, and it is decision 21 restated in the
kernel — *a session can only ask; the app acts.*

It follows that **any capability the system needs must exist inside the app**, because the outside is
unreachable by design. Clearing this pause did not exist inside the app, so the only route was a
human with a shell.

It is also worse than it looks: the operator's fix was *stop → edit → start*, not *edit → restart*.
`Persist_EngineState()` is called from ~20 sites and rewrites the whole snapshot from memory, and the
daemon **drains in-flight turns while dying** (`TimeoutStopSec=2400`). An edit made while it lives is
overwritten by its own shutdown — which is exactly what happened at 14:13 and cost the diagnosis its
credibility twenty minutes later.

**Change.** A first-class action, `clear-dispatch-pause`, in the request-file protocol
(`OrchestrationRequests_Reader`, alongside `close-orchestration` / `set-telegram-muted`). The engine
nulls `_dispatchPausedUntilUtc` and `_dispatchPauseReason` under `_ownerStateLock`, calls
`Persist_EngineState()`, and confirms with a `FROM app` entry. **No root, no restart, no file editing
— the fields it needs are in its own memory.**

**It is owner-confirmed, not owner-only.** This lifts a guardrail that protects the bill, so an agent
may ask and only the owner may grant: the request parks and raises a confirmation button, the same
mechanism `close-orchestration` already uses (`CloseConfirmation_Parking`, `PendingConfirmations`).
The owner also gets a direct route with no agent involved — a **Lift the pause** button on the pause
alert itself, and `/resume-dispatch`. On a grant, the next tick re-decides from live probes: if the
account really is over threshold, it pauses again within the minute and says so. **The brake is not
bypassed — it is re-asked.**

---

## 4. Defect 3 (latent) — probe files outlive the account that wrote them

Probe files are never deleted, and the binding-window rule prefers the window whose reset is **latest**
outright, competing on percentage only within the same instance (`Read_CurrentLimitWindows`,
`WindowInstance_Order.Compare_Instance`).

An account swap breaks that rule's assumption. Measured now [measured]:

| probe | weekly reading | resets | file last written |
|---|---|---|---|
| `fincanva-2` | 60% | **16/9 03:00** | 10/9 16:29 — **old account** |
| `fincanva-5`, `-6` | 70% | 14/9 08:00 | today, 14:03 / 14:19 — new account |

The old account's window resets *later*, so the gate's current weekly reading is **60%, from a file two
days stale, written by an account that no longer exists** — not the 70% of today. Harmless at these
values; not harmless at all in combination with §2. Had the dead account read 99% with a later reset,
the auto-lift proposed above would have kept the pause alive on a number belonging to nothing.

**Change.** A probe is only evidence while it is current: ignore any usage file not written inside the
longest live window it claims (a stale-file cutoff), and never lift a pause from an empty reading —
silence is not a low number, which is why §2 leaves the stored instant standing when nothing is read.

---

## 5. Defect 4 — the message that caused the misdiagnosis

The pause instant is rendered `HH:mm` with **no date**, in two places:

- `DispatchPause_Gate.Describe_Pause` — `Resuming automatically at {resumeAtUtc:HH:mm} UTC.`
- `BridgeEngineModel:8592`, the `/limits` line — `Resuming at {pausedUntilUtc:HH:mm} UTC.`

So a pause **five days out** told the owner and the supervisor *"Resuming at 03:00 UTC"*, which both
read as tonight. That single missing date is what made the block look like a fifteen-hour wait instead
of a five-day one, and it is why nobody moved on it for six hours [measured].

**Change.** One formatter, dated and relative — `Resuming automatically at 2026-09-16 03:00 UTC (in
4 d 14 h)`. Note the two copies: this is decision 12's rule (*never add a second copy of a formatter*)
already violated, and the fix is to delete one, not to edit both.

---

## 6. What this does NOT change

- **The brake still brakes.** Threshold, the binding-window choice, the 429-refusal path
  (`Decide_PauseUntil_ForRateLimit`) and the fallback window are untouched.
- **Work in flight is still never killed**, and a lifted pause does not retro-start anything: the
  dispatcher simply stops refusing.
- **No new privileges anywhere.** `NoNewPrivileges=yes` stays. Nothing here asks a session to hold a
  right it cannot hold — that is the point.

---

## 7. Acceptance criteria

Done when each of these exits 0 / prints the stated line:

1. **Auto-lift.** Paused until T+5d; probes then read below threshold → within one
   `LIMIT_CHECK_INTERVAL_SECONDS` tick the state file shows `dispatchPausedUntilUtc: null` and the
   channel carries one `▶ Dispatch resumed`. *(Replays today's incident.)*
2. **No extension.** Paused until T+30m; probes read 99% → the stored instant is still T+30m, and no
   second pause message was sent. *(Protects the defect §2's guard was built for.)*
3. **Silence does not lift.** Paused; zero probe files → the stored instant is unchanged.
4. **Stale files are not evidence.** Paused; the only over-threshold reading comes from a file older
   than its own window → it is ignored, and the pause lifts.
5. **Lever, confirmed.** `{"action":"clear-dispatch-pause"}` → the request parks, the owner gets a
   button, a tap nulls both fields **with the daemon running** and the next tick re-decides. Untapped,
   nothing changes.
6. **Lever, refused.** Same request while probes genuinely read 99% → granted, then re-paused within
   one tick, and the owner is told it came straight back.
7. **The date is in the message.** `/limits` and the pause alert both print the full instant and the
   relative distance; `grep -c "HH:mm"` against the resume wording returns 0.
8. Full suite green (0 red, 9 skipped — compare the names, not the count), then trial-merge into
   `ours/integration`.

---

## 8. What was verified, and what was not

**Verified live on the VPS on 2026-09-11** [measured]: the stored instant and reason; the seven probe
files and their readings, resets and mtimes; the 95% reading is present in **no** surviving probe;
`NoNewPrivs` for shell, daemon and every session; the sudoers entry and its exact command strings;
that `.engine-state.json` is rewritten from memory while the daemon lives; that the running binary was
built from the commit whose source is quoted here; that clearing the two fields with the daemon stopped
lifts the block and holds — state still null after 2 minutes of uptime, 3 sessions respawned, turns
completing.

**Not verified, and deliberately so:** whether the 95% was itself correct when it was written at 08:14
— the file that carried it has since been overwritten, and no archive of it exists. This spec does not
need it to be wrong; it needs the pause to be **answerable**, which it was not.

**One operational footnote.** The sudoers entry matches the literal string `systemctl restart
aiorchestrator`. The instruction that reached the operator said `aiorchestrator.service`, which does
not match and would have prompted for a password. Trivial, and it would have cost another round trip.
