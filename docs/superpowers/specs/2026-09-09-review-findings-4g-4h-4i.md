# Three changes, three reviews, three stops — 2026-09-09, night

**Status: none of the three is merged.** Each was written by one agent, verified by me (real diff read, suite re-run), and then broken by an independent adversarial reviewer with a probe. This file is the to-do list, so the findings are not lost with the session that produced them.

The pattern is the evening's real result: **three reviews, three changes with proven defects, every one of them behind a green suite and a read diff.** The morning's audit found defects in 3 of 3 live-behaviour changes; the night found the same rate. Review before merge, always — my own reading is not a substitute.

Branches, all pushed, none on `ours/integration`:

| branch | commit | what it was meant to do |
|---|---|---|
| `stage/4g-soft-boundary` | `b89543a` | past ~35 tool calls, one advisory in a long turn: reach a stable point, save, report |
| `stage/4h-wakeup-digest` | `601c178` | the supervisor woken to decide, not to take note: member entries digested, owner immediate |
| `stage/4i-model-per-task` | `cea0c64` | `MODEL:` in a brief chooses the model for the next stage + the reviewer stops sharing the implementer's key |

---

## `stage/4g` — the soft boundary

**CRITICAL — the advisory never fires.** `IFS=$'\t' read -r S A C <<< "$RAW"`: tab is IFS *whitespace*, so consecutive tabs collapse and an empty field vanishes. `agent_type` is optional in the PreToolUse payload and **absent on the sessions this app spawns** (no `--agent`, no agents in the kit — the CLI's own schema says it appears only for a sub-agent or a `--agent` session), so `AGENT` receives the `tool_use_id` and `CALL_ID` receives nothing. The sub-agent gate then compares two different `tool_use_id`s on every call and exits. Probe: the shipped script, 45 calls, threshold 35, real payload shape → **0 advisories**; the marker file contains `toolu_0001`.
**And all 21 tests pass**, because the fixture hard-codes `agent_type` on every payload — decision 20 in its purest form. The author's live probe said otherwise because *this Mac* has a plugin supplying a default agent, so `agent_type` was present: a payload captured here is not a payload from a VPS member session.

**HIGH — the count is per session, and per-turn only in fresh mode.** `RoleRunnerConfig_Factory.Create_Default` gives `Fresh` to the general supervisor only; implementer, reviewer and solo default to `Terminal` + `Transcript`. Probe: one session id, four 40-call turns → advisories `1, 0, 0, 0`. The header claims the resumed case "advises earlier, never later, and is the safe direction" — it advises **never**, for the life of the session, and the latch makes it permanent.

**HIGH — the reason given to the model is a clock the hook never read.** The advisory and all three skill sentences cite the 30-minute deadline; the hook counts calls. 35 fast reads can land two minutes in. Worse, the deadline is *print-runner only*, and terminal spawns also set `AIORCH_ROLE`, so a session that is never killed at 30 minutes is told it is about to be.

**MEDIUM** — the 24 h sweep deletes `<session>.agent` and `<session>.fired` but keeps `.calls` (whose mtime is refreshed), so a live session older than a day is un-latched and the next advisory can go to a read-only sub-agent. **MEDIUM** — `ASubAgentsCallsCount_But…` passes with sub-agent counting removed. **MEDIUM** — `TheShippedHook_IsExecutable` returns early on Windows (a silent pass) and pins the only 755 hook in a kit whose other hooks are 644. **LOW** — a numeric-but-huge threshold fires on call 1; case-differing session ids share a counter on macOS; a retried `tool_use_id` is counted 40 times (a consequence of the critical); "no exit code but 0" is false for a closed stdout.

**Judgement, on the words** (not a probe): "AT OR **NEAR** a stable point … reach it" invites nominating the nearest thing that looks complete; "your next turn resumes with a pack, so stopping here loses nothing" is unconditional and false in transcript mode (there is no pack) and whenever the remaining work would have closed the ledger line; and the skills bolt "the one exception" onto a paragraph that says *"'I have reached a natural boundary' is not a reason at all"*. The counterweights are strong and tested, but the reviewer sentence is the weakest of the three.

**Not broken:** it can never block or fail a tool call (attacked ~15 ways: unset/unwritable/file `TMPDIR`, no `python3`, truncated `hook-log.sh`, 8 MB payload, path-shaped session id — always `rc=0`, empty stdout, no denial field); the `mkdir` latch is atomic under 12 parallel crossings; delivery through `kit/install.sh` works and the 755 mode survives.

## `stage/4h` — the wake-up digest

**HIGH — the digest fires once per session, then never again.** `DigestHeldSince` is written in exactly two places and cleared only on a tick whose pending set is non-empty and non-digestable. The tick after a completed turn has an **empty** set and returns early, above that line — so the `??=` keeps the first report's instant for the life of the process and every later report reads as already past the window. Probe red, plus a green control probe with an owner message inserted between the two cycles (the one path that reaches the clearing line) — cause isolated. **Consequence: in the steady state the 247-wake-up saving is not delivered, and nothing says so.**

**HIGH — a restart restarts the window.** The tracker is per-process; the first tick after a restart stamps `nowLocal` on traffic that has already waited. Probe: a report filed at T+1 on a 5-minute window went out at **T+11**. The docstring claims a restart "costs one early delivery"; it costs a late one, once per restart, unbounded if restarts repeat.

**MEDIUM** — `"QUESTION"` is a bare word searched anywhere in a subject and is a *fourth* hard-coded spelling of that vocabulary (the repo already has three, all with the colon): `REPORT — the open question about the parser is settled` defeats the hold. Safe direction, but it shrinks the saving by an unmeasured amount. **MEDIUM** — the nudge coupling is real and unguarded: at `memberDigestMinutes` 8+ the app tells the supervisor it is late on a verdict for a report the app itself is holding, and spends that quiet spell's single nudge token on the false alarm (decision 15 is **not** breached — the nudge is agent-audience, the owner sees nothing). **MEDIUM** — a fresh member's `imp-N online` greeting is held, so every `add-implementer` costs up to one window of dead time, undisclosed in the commit message. **MEDIUM** — 20 of the 22 new cases still pass with the gate bypassed, and flipping the harness default to digest-OFF removes the suite's only pre-existing coverage of member→supervisor delivery at the production window. **LOW** — a negative `memberDigestMinutes` restores the 5-minute default instead of meaning off, contradicting the factory's own docstring three lines away.

**Not broken:** the owner is never delayed (attacked five ways, including a solo writing into the owner channel and an owner entry inside a held batch — not one tick); the app's own bookkeeping genuinely wakes nobody and is neither dropped nor replayed (verified independently of the author's two tests, which do not prove it); clock pathologies (backwards clock, future stamp, zero window) all resolve to deliver; escalation detection cannot be defeated in the dangerous direction; the gate really is the last one before `Start_Turn`.

## `stage/4i` — the model per task

**CRITICAL as reviewed, downgraded on my own check — read both halves.** The reviewer proved that a member can sign as the supervisor and thereby choose its own model: `ChannelEntry_Parser` matches `^## [n] FROM <author>` **per line, with no fence awareness**, so a header quoted inside any body becomes an entry with the named author. Probe: `Resolve_ForTurn => 'opus'` where `'sonnet'` was expected.
**But** I then verified the write path myself: `PrintTurnEntry_Splitter.Neutralise_HeaderLines` prefixes `> ` to every header-shaped line in a body, and it is on the path the dispatcher uses for **both** print and stream (`Write_Reply_Async` → `Split`). So for every session the bridge writes for — which is every member and supervisor in production today — the forgery does not survive the write. The reviewer's probe wrote the channel file by hand.
**The door that is genuinely open** is `kit/bin/channel-append.sh --author <word>`: a free parameter, used by **terminal-mode** sessions (the *code* default for implementer/reviewer/solo), with no check against `AIORCH_ROLE` and no neutralisation of the body. Proven: a member signed `supervisor` with one documented command.
**Same weakness, wider blast radius:** `Brief_Finder` (shipped in `stage/4b`, live since 11:00) trusts `entry.Author == Supervisor` to decide what a member's brief is. Bridge-written entries are safe for the same reason; a terminal-mode member could make a quoted brief become *the* brief in the next pack. Not malice — confusion, and the agent then works on the wrong task.

**HIGH — "the last marker wins" is decided by the agent-written timestamp.** At the intended call site the entries arrive from `PendingTraffic_Orderer`, which sorts by `DateText` — the field decision 12 records as untrusted (a supervisor once stamped `01:34` on an entry written at `15:20` the day before). Probe: a future-stamped `MODEL: opus` beat the supervisor's later `MODEL: haiku`.

**HIGH — the fence rule is defeated four ways**, two of them member-controlled: tilde fences, a quoted report whose own ``` line closes the supervisor's fence, nested fences, and a fence spanning two entries (`insideFence` is a per-entry local, so the file's rendering and the rule's reading disagree).

**MEDIUM** — a wrong-typed value in either new key (`"reviewerModel": 5`) throws out of `Get_String_OrNull` and takes the whole config load down, on the startup path and on every tick — while its neighbour `Get_Long_OrNull` was deliberately made tolerant for exactly this reason. **MEDIUM** — rule 6 ("never silence") fails twice: a member's `MODEL:` ask is declined with no reason logged (and a test pins that silence), and an unclosed fence swallows a supervisor's real ask silently. **MEDIUM** — a second route to a per-role default survives (`PrintTurnDispatcherModel` reads `.GeneralSupervisorModel` directly), against the one-reader claim. **LOW** — `"reviewerModel": ""` spawns with no `--model` flag at all; the two new keys accept `fable` and anything else from config.json; two new tests pin nothing.

**Not broken:** `fable` is refused on the channel across ten attack spellings (an allowlist is the shape that makes it unbreakable); **an existing `config.json` does not change behaviour** — the highest-stakes claim, attacked with absent keys, `implementerModel` only, explicit null, and a `Save()` round-trip, and it held; no misbinding among the 14 positional parameters (all six call sites pinned with distinct sentinels, WPF included).

---

## What I would do with each, in the morning

1. **`4g`**: fix the field split (do not rely on IFS for possibly-empty fields), decide the count's unit honestly — per turn means per session **only in fresh mode**, so either gate the hook on fresh or key the count on something that is per turn in both modes — rewrite the fixture to the **real** payload shape (no `agent_type`) and re-run every case against it, make the sweep remove `.calls` with its markers or none of them, and soften the wording where the reviewer is right (`AT OR NEAR`, "loses nothing", "the one exception").
2. **`4h`**: clear the hold stamp where a turn actually starts (not on a tick that may never come), make the stamp survive a restart or refuse to re-stamp what has already waited, use the repo's existing `QUESTION:` spelling instead of a fourth one, exempt a member's first entry, couple `memberDigestMinutes` to the nudge constant (refuse or warn at ≥ 8), treat a negative value as off, and set the harness default to the production window so the suite observes what ships.
3. **`4i`**: keep the config split (the half that survived) after fixing the typed-value crash and the empty string; drop `MODEL:`-in-a-brief as the mechanism — the sanctioned path is the request file the supervisor already uses, authenticated by *where* it is written rather than by a word in the text. Then, separately and with the owner's decision: close `channel-append.sh --author` (refuse a word that does not match `AIORCH_ROLE`), which is the last door for both this and `Brief_Finder`.

*Nothing in this file is certified by the sessions that produced the changes. Each finding names its probe; the three "not broken" lists are as much of the result as the findings.*
