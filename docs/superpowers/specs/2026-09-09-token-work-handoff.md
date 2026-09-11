# Token work — what this session changed, what it will change next

**For the other agents on this app.** Read this before touching anything it names: on 2026-09-09 two
lines of work ran in parallel on the same files and the same afternoon (speed: `stage/7*`; tokens:
`stage/4*`), and one feature was **built twice** — my `stage/4c` and its `stage/7f` are the same
closing turn. This file exists so that does not happen again.

**Written 2026-09-09 22:00 CEST.** Copies read: branch source `ours/integration` (Mac checkout), the
VPS clone and the running binary at `/opt/aiorchestrator`, the installed kit under
`~/.claude/plugins/cache/aiorch-local`. The plan and every measurement live in
`2026-09-08-token-efficiency-design.md` (§5b–§5g are today's status, gates and schedule).

---

## 1. Mine, merged and live

| branch | commit / merge | what it does | files it owns |
|---|---|---|---|
| `stage/4b-state-pack` | `bd6f942..0b7dec5`, merged `c9a36a8`, deployed 11:00 | **The state pack.** On a fresh turn the bridge writes `<orch>/<member>/pack.md` — pending entries, the brief (found across live channel **and** archive), the member's last own report, its `PLAN.md` lines, `git status/log -3`; for supervisor/solo also the whole `PLAN.md` and the last 5 owner entries. **Nothing on stdin** (measured: the CLI appends stdin into the slash command's `$ARGUMENTS`). Every print role's skill gained one boot sentence: "if `pack.md` exists, read it first". `executed_turns[]` gained `session_id`. | `Running/StatePack/*` (5 types), `Running/PrintTurnPrompt_Builder.cs` (two public renderers + the dead `Build_FreshSession`), `Running/TurnExecutor/PrintTurnExecutorModel.cs`, `Running/ExecutedTurn/*`, `Running/PrintSessionState/PrintSessionState_Store.cs`, `kit/skills/*/SKILL.md` (one sentence each), `tools/token-gate/*`, the live CLI contract test |
| `stage/4e-host-shutdown-timeout` | `ec631d2`, merged `abf04bd`, deployed 21:36 | **The drain is no longer cut at 30 s.** `HostOptions.ShutdownTimeout` was never set, so every stop killed in-flight turns 30 s after announcing a 31-minute drain (measured 17:24/17:25/17:29: 5 fresh sessions, **15.2 M tokens**, one turn restarted 4×). `ShutdownGrace_Rule` is now the single source for drain < engine grace < host timeout (39 min). | `Running/ShutdownGrace_Rule.cs` (new), `AIOrchestrator.Daemon/Program.cs`, `AIOrchestrator.Daemon/BridgeHost_Service.cs`, `deploy/systemd/aiorchestrator.service` (2400 s) |
| `stage/4f-general-model-refresh` | `c51068b`, merged `f5d6b76`, deploying 21:47 | **The general supervisor's model is re-read.** It was frozen at first registration: the watchdog exempts a print-run general (no process to check), so `Spawn_GeneralSupervisor` never runs twice and `BridgeDrivenRunnerModel`'s refresh path is unreachable — `config.json` said sonnet, the session ran haiku since the day before. The dispatcher reconciles it at the start of a turn. Members untouched (their model is a per-member choice). | `Running/PrintTurnDispatcher/PrintTurnDispatcherModel.cs` (`Refresh_GeneralModel_IfChanged`), dispatcher tests, the harness's `Set_ConfigValue` |

**Config, not code (already applied):** VPS `config.json` — `implementer`/`reviewer` `resume: fresh`
(round 1, 8 Sep 20:34Z), `generalSupervisorModel: sonnet`, `communicatorModel: sonnet`; supervisor and
the implementer/reviewer fallback stay `opus`. Backups: `config.json.pre-models-*`.
**Recipe, not code:** `00_Infrastructure/aiorch-vps/03-aiorch.sh` step 7 wrote four `// "haiku"`
defaults that beat the app's own on a fresh box — now `opus/opus/sonnet/sonnet`, Mac and VPS copies
byte-identical. **Root, not code:** the VPS drop-in `drain.conf` 2100 → **2400 s** (backup kept).

## 2. Mine, parked — do not merge

`stage/4c-closing-turn` (`b87e87d`, pushed, **not merged**): the same closing turn as `stage/7f`, built
in parallel before either of us knew. `7f` won on merit (bounded budget, timeout derived from the turn
timeout, the attempt not spent, every fallback logged) and is live. `4c` is kept only as a record;
**if you touch the closing turn, work from `7f`'s `Running/ClosingTurn/*`, never from `4c`.**

## 3. What I intend to build next — claim before you start

Schedule and rationale in the spec's §5g; ownership below so nobody builds it twice. **If you want any
of these, say so in the owner's channel first and I will drop it.**

| when | mine | what I will touch |
|---|---|---|
| 10 Sep morning | **C9 — soft boundary inside a turn** (advice near call ~40: "reach a stable point, commit, report"; the deadline stays the hard net). 41 % of tonight's member tokens are in turns that ran to the deadline. | `kit/hooks/` (a new PreToolUse advisory), `kit/skills/implementer|reviewer/SKILL.md`, possibly `Running/` for the sensor if the hook cannot read its own transcript |
| 10 Sep after Gate 3 | **C4 — wake-up digest for the supervisor** (owner immediate; member entries at stage end or every 5 min; mechanical acknowledgements handled by the app) | `Running/PrintTurnDispatcher/*`, `Running/PendingTraffic/*`, `Running/TurnSource/*` |
| 11 Sep morning | **C6 — `MODEL:` read from the brief**, and the reviewer's model key split from the implementer's (they share `implementerModel` today) | `Configuration/OrchestratorConfig*`, `Launching/OrchestrationLauncher*`, `Running/PrintTurnDispatcher/*` |
| 11 Sep afternoon | **Supervisor fresh with the pack** (the pack for it is already built and inert) | VPS `config.json` + verification; no new code expected |
| later | **C3** app bookkeeping out of the channel · **C7** the token counter inside the app · **`STATE:`** only if a gate shows rework | `Channels/*`, `Usage/*`, `Running/StatePack/*` |

## 4. Where we will collide, and the rule I am following

- **`PrintTurnDispatcherModel.cs`** is the shared road: 4c/7f, 7b, 7e and my 4f all landed in it today.
  Rebase onto `origin/ours/integration` **before** writing, and re-run a trial merge before you commit.
- **`kit/skills/*/SKILL.md`**: one sentence per skill was added for the pack. The kit rule holds —
  reorganise, never rewrite a rule; a test counts normative sentences.
- **The deploy path**: use the recipe `~/aiorch-vps/03-aiorch.sh`. It reinstalls the plugin when the
  installed commit differs; the manual `git pull` + `cp -a` route in the speed report does **not**, so
  sessions keep reading stale skills. One procedure, not two.
- **Restarting the VPS is no longer free but it is now safe**: the running binary drains in-flight
  turns (up to 36 min) instead of killing them. Do not add a shorter grace anywhere.
- **Two corrections to `2026-09-09-report-lavoro-e-runbook.md`**, both verified: its open item #1 blamed
  `TimeoutStopSec` = 90 s — that is the **`--user`** unit; the system unit had 35 min and the real cut
  was the .NET host (§1, `stage/4e`). And `--max-budget-usd` not capping spend is real but harmless for
  the closing turn, whose real bound is the derived timeout.

## 4b. Parked, with where it went instead — 2026-09-10

- **Collapsing the `QUESTION:` literals** (two survive in `Bridge/`): **delivered by E3**, not by this
  line. The E3 author folded it in as a requirement on 2026-09-10 (`ff24382`): writer and every
  recogniser — entry parser, digest, state pack, legacy-marker path — read one `ChannelGrammar`
  constant, with a grep test forbidding a marker literal anywhere else. Parked here with that pointer;
  doing it separately now would be work E3 has to undo.
- **The role-bound author** and **selecting the brief by declared type instead of the subject's first
  word**: same commit, same reason. Nothing in this line's current work depends on E3, which starts
  only after brief C is in production.
- **A held report dying with its member**: logged since `98d4184` (`UndeliveredSpokeTraffic_Reporter`),
  which is a record of a loss rather than a guarantee. Whether a close should WAIT for unread traffic
  instead of reporting it is the owner's decision, not a defect.
- **`IMPLEMENTER_NUDGE_MINUTES` has no shared home**, so the digest ceiling restates `8` in prose; and
  the config-refusal line would be better read at daemon startup than on the dispatcher's first tick.
  Both small, both in files this line does not own.

## 5. What this line hands to E3 (typed channel entries) — 2026-09-10

E3 replaces hand-written marker prose with a validating CLI, and its stated aim — *"the marker grammar
appears in ONE place (the tool), not in three skills"* — is the same aim as this line's smaller
consolidation of the `QUESTION:` literal. Sent to the E3 author by the owner on 2026-09-10; recorded
here because a chat message is not the record. Two requirements and two absorptions, each with the
measurement behind it.

**Requirement 1 — one place to WRITE is not one place to KNOW.** The transition rule keeps the bridge
parsing today's prose, and since 2026-09-10 the wake-up digest reads the same vocabulary (a member
declaring `BLOCKED ON OWNER` or asking a question is never held). So there are two roles by
construction: whoever writes and whoever recognises. "One place" must therefore mean **the tool and
the matcher read the same constant**, not two spellings that resemble each other. The failure is not
hypothetical: on 2026-09-10 the word was spelled four times in this tree — `Bridge/OwnerPush_Policy`,
`Bridge/OwnerMessage_Contract` (private), `Bridge/Decisions/OwnerQuestion_Contract` (bare, no colon)
and a fourth added by the digest itself, in the colon-less form, which a report merely mentioning
"the open question" then defeated. Two of the four were collapsed onto `Status/MemberState_Resolver`;
two survive, and the bare one is the interesting one — its consumer appends the colon, so collapsing
it is a judgement about where the colon lives, not a substitution.

**Requirement 2 — the tool is the moment the author stops being a claim.** `kit/bin/channel-append.sh
--author <word>` takes any word, with no check against `AIORCH_ROLE`, and the implementer protocol
tells members to call exactly that helper. Proven on 2026-09-10: a member signed `supervisor` with one
documented command. This is why the model-per-task mechanism was deleted rather than fixed — it
granted a privilege on the strength of that signature. When the tool becomes the only sanctioned
writer it can refuse an author that does not match the session's role: one line inside work already
planned, against a package of its own later. (Bridge-written entries are already safe: the app
neutralises header lines echoed inside a body — `PrintTurnEntry_Splitter` — so the open door is the
terminal-mode helper.)

**Absorption 1 — a declared type is a better key than a guessed word.** The state pack finds a
member's brief by looking for the last supervisor entry whose subject OPENS with a task marker
(`BRIEF`, `REVIEW`, `FINDINGS`…) and, failing that, the longest recent supervisor entry. That is a
guess about prose. If entries carry a type (`--question`, `--report`, `--state`), `Brief_Finder` reads
the type instead, and the failure it is exposed to today — a brief quoted inside a report becoming
*the* brief in the next pack — disappears with no further work.

**Absorption 2 — the index is already the tool's; the timestamp is the new part.** `channel-append.sh`
computes and prints the index today, so that half is not new. The timestamp is, and it is the field
CLAUDE.md decision 12 records as untrusted (a supervisor stamped `01:34` on an entry written at
`15:20` the day before). Worth saying out loud in E3's own done-when, because a tool that computes the
index and still takes a model's timestamp closes the smaller of the two holes.

**One thing E3 should NOT inherit from this line.** The digest's escalation vocabulary is matched
through `MemberState_Resolver.Contains_Marker` (whole token, decoration stripped, quotation refused),
while `OwnerPush_Policy.Carries_Question` is a raw `Contains`. Sharing a constant between them makes
one rule look like it governs two matchers. If E3 unifies the vocabulary, it should say which matcher
is canonical — or the next reader will assume both behave the same, which they do not.

## 6. What is measured, and how to measure it yourself

`tools/token-gate/` (read-only, metadata only — `usage`, timestamps, model, tool names; never message
text). `gate_turns.py` compares calls per turn / first-call context / tokens per turn across the
experiment windows; `gate_delivery.py` splits the bill into members / supervisors / sub-agents and
reports production vs orientation calls per closed ledger line. Run them on the VPS from `/tmp`.
Headline so far (spec §5f): **tokens per call for members 345 k → ~100 k and stable**; per delivery
4.7× on short tasks; the remaining cost has moved **inside** long turns, which is what C9 attacks.

*Nothing here is certified by its author: the suite numbers are `dotnet test AIOrchestratorCoreLib.Tests`
(2 756 passed / 9 skipped / 0 red on `f5d6b76`), the VPS facts are reads of the running box, and the
before/after that proves the plan is the gate in §5f–§5g.*
