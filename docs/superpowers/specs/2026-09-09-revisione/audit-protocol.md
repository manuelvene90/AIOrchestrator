# AIOrchestrator — agent-protocol audit (read-only)

**Date:** 2026-09-08 · **Scope:** the AGENT protocol (channels, role skills, hooks, runners, ledger), not the UI, not the Telegram bridge internals · **Posture:** critical, nothing here is certified by its author.

## Copies read, and how the numbers were taken

| what | copy | how I know |
|---|---|---|
| code + kit | **branch source, `ours/integration` HEAD `926cc6b`**, checkout `/Users/nvene/Visual Studio/AIOrchestrator` (this Mac) | `git log -1 --format='%H %s'` → `926cc6b… Merge stage/4a` |
| specs | same checkout, including the two **untracked** files `docs/superpowers/specs/2026-09-08-token-efficiency-design.md` and `…-revisione-memoria-fra-turni.md` | `git status` shows them untracked |
| **not read** | the installed plugin cache (`~/.claude/plugins/cache/aiorch-local/…`), the running daemon binary at `/opt/aiorchestrator`, the live `config.json` on the VPS | decision 18/23 — every claim below about the *live regime* is marked `[documented]` and sourced to the 2026-09-08 review, which did read them |

Sizes: `wc -c` on that checkout (`find kit/skills -type f | xargs wc -c`, `wc -c kit/hooks/* kit/bin/*`). Token figures are `[estimate]` at **3 bytes/token**, using the calibration the independent review measured from the supervisor−implementer boot delta (2.8–3.2 B/tok, `2026-09-08-revisione-memoria-fra-turni.md` §"Calibrazione"). Duplication and anecdote shares are measured by script (paragraph normalisation + `difflib`, ≥0.75 ratio for near-duplicates); the script is reproducible from the commands quoted inline.

Live consumption figures (249 k median first-call context, 690 k tokens/turn median, 34–47 k boot, 97.8 % cache read, 1.56 pending entries/turn, 13.1 % sidechains) are `[documented]` from the two 2026-09-08 documents. **I did not re-measure them and this audit does not redo that analysis** — it builds on it.

---

# 1. The protocol as it really is

## 1.0 Two regimes, and which one is live

| regime | how a session runs | how it is woken | who writes the channel |
|---|---|---|---|
| **A — interactive terminal + watcher** (the original) | `wt new-tab … claude "/supervisor <id>"`, one long-lived TUI process | the session arms a **persistent `Monitor`** that polls every channel's `size + md5` every 5 s (`kit/skills/supervisor/reference/watcher.md`) | the session itself, via `channel-append.sh` |
| **B — bridge-driven headless** (print / stream) | `claude -p --output-format json …` per turn (print), or one long-lived `claude` fed on stdin (stream) | the **bridge dispatcher** decides there is pending traffic and starts a turn (`Running/PrintTurnDispatcher/PrintTurnDispatcherModel.cs`) | the **app**, from the session's final message |

**The live regime is B.** Evidence: `RunnerConfigs_Json.cs:10-12` documents the shipped shape (`implementer: print/transcript`, `supervisor: stream/transcript`, `general: print/fresh`); `README-daemon.md` (last paragraph) says the *terminal* runner is Windows-Terminal-only and "on macOS/Linux the watchdog logs a spawn failure … until the print runner lands"; the VPS `config.json` read on 2026-09-08 had supervisor `stream/transcript`, implementer·reviewer·solo·communicator `print/transcript`, general `print/fresh` `[documented]`, and since 22:34 that evening implementer + reviewer run `resume: fresh` `[documented]`. Regime A is still live only on the Windows WPF host.

Consequence worth stating up front: **regime A's protocol is still shipped at full weight in every role skill** — 38 KB of `reference/watcher.md` across six roles, plus the append-helper contract (locks, exit codes 3/127, degraded mode) that a print session is explicitly forbidden to use. On the daemon host that is dead text in the hot path.

## 1.1 One full cycle, hop by hop (regime B)

**Hop 1 — owner → supervisor.** The owner types into the orchestration's Telegram topic. The bridge long-polls `getUpdates`, aggregates messages sent in a row (~15 s after the last, `supervisor/SKILL.md` §"Echo the owner in your terminal"), downloads photos into `media/` and appends **one** `## [n] FROM owner — … — <subject>` entry to `<orch>/owner-channel.md`, taking the same `.lock` directory the shell helper takes (`kit/bin/channel-append.sh` header). Nothing is queued in memory: *the queue is the channels* (`PrintTurnDispatcherModel.cs`, class docstring).

**Hop 2 — what wakes the supervisor.** Per tick the dispatcher resolves the session's sources (owner channel + every open member's spoke, `TurnSources_Resolver`), parses each file, and selects pending entries by **content identity, not index**: `PrintTurn_Trigger.Select_Pending` keeps an entry whose author is inbound for the role (`Is_Inbound`: for a supervisor, `owner` or any member) and whose `ChannelEntry_Digest.Compute` is not in the cursor. Entries landing within `CoalesceWindow` (default 3 s, `RunnerConfigs_Factory.DEFAULT_COALESCE_WINDOW`) ride one turn, across channels. One turn per session at a time; ≤ 10 globally, ≤ 3 per orchestration (`DEFAULT_MAX_CONCURRENT_TURNS*`).

**Hop 3 — what text the model actually receives.** Three shapes, all from `Running/PrintTurnPrompt_Builder.cs`:

- *Resumed turn* (`Build_FollowUp`): `[bridge turn <requestId>]`, optionally `Turns already executed by the bridge for this session: …` (restart de-duplication, decision 8), then either `New traffic in your channel — N entries:` (single source) or `New traffic on N channels, M entries, oldest first:` with `--- from imp-1 (channel.md) ---` labels, **the entries verbatim** (`item.Entry.RawText.Trim()`), then the contract paragraph: *"Your final message IS your channel entry … first line the subject, then a blank line, then the body. A question ends the turn exactly as an answer does."* Multi-source adds the `TO: <channel>` addressing rules and the list of channels addressable this turn.
- *Boot turn* (`BOOT_TURN`): "Nothing has been said to you yet — this is your boot turn." Dispatched exactly once per session that owns an owner channel (`Needs_BootTurn`: supervisor or solo, `ExecutedTurns.Count == 0`), because the greeting is what creates the Telegram topic.
- *Fresh turn* (`Build_FreshSession`, shipped in `3537d57`, merged as `926cc6b`): `FRESH_SESSION_PREAMBLE` — "You are a FRESH session … The entries below are your pending traffic — the bridge tracked them, so do not re-derive them from the channel" — plus `FRESH_SESSION_ROLE_FALLBACK` (invoke the role command with the Skill tool if it did not run) and, for non-general roles, `FRESH_SESSION_NO_GREETING`.

On top of that the model receives its **role skill** as the expansion of the positional slash command (`PrintTurnCommand_Builder.Build_Arguments` appends `Build_RoleCommand(...)` whenever the turn is not a transcript resume — so on turn 1 in transcript mode, and on *every* turn in fresh mode), plus Claude Code's own system prompt, tool definitions and whatever MCP the repo registers. Measured boot for a member: 34–47 k tokens, of which the skill ≈ 8 k `[documented]`.

**Hop 4 — what the supervisor must write.** Its final message, split by the app:

1. `TurnReply_Splitter.Split` breaks the text on `TO: <one-word-channel>` lines (fence-aware; a marker inside ``` is quoted, not addressed; a single-source session never comes through here).
2. `PrintTurnEntry_Splitter.Split` turns each block into (subject, body) — first line/blank/body, else first line capped at 120 chars; `Strip_LeadingNarration` removes "Now writing my final message…"-shaped preambles (a real 2026-09-07 incident); `Neutralise_HeaderLines` prefixes echoed `## [n] FROM …` lines with `> ` so a quoted header cannot become a second entry.
3. The app appends each part under the author word for the role (`SessionRole_Names.Get_Author`), with header, index and timestamp allocated inside the lock, retrying 3× at 300 ms (`ENTRY_APPEND_ATTEMPTS`).
4. It then appends a `turn_ended` app entry (attempt, exit code, cost, entry indices) into the same channel and persists the cursors.

Inside the body the protocol is a **marker vocabulary**, all of it mined out of prose: `QUESTION:` + ≥2 `OPTION:` + `RECOMMEND:` + `RISK:` + `ROW:` (all five required or the question is not sent, `supervisor/SKILL.md` §"A question to the owner is FIVE lines"), optional `DEADLINE:`/`DEFAULT:`, `BLOCKED ON OWNER`, `IMAGE:`/`ATTACH:`, `WORKTREE:`, `WRITING/MUTATION WINDOW OPEN|CLOSED`, `STANDING BY`, `WAITING ON`, `GO AHEAD — resume`. The app's side of six of them is `Status/MemberState_Resolver.ALL_MARKERS`.

**Hop 5 — brief → implementer.** In stream mode the brief is a `TO: imp-1` block; in print mode the supervisor writes the spoke with `channel-append.sh`; in either case the brief must carry: task, numbered completion contract ending "append your boundary report … and re-arm your watcher", the repo's mandatory reading list, staging discipline, `WORKTREE: <full path>`, optionally `MODEL: sonnet|opus`, optionally a `PARALLEL UNITS` proposal (`supervisor/SKILL.md` §"Briefing a new implementer", §"Brief for parallelism"). The append wakes the implementer the same way — dispatcher tick, `Is_Inbound(Implementer, Supervisor)`.

**Hop 6 — implementer turn.** Boot is deliberately lean (no repo study, no agents); with a task it reads the repo's mandated docs (Fincanva: 38.9 KB ≈ 10–12 k tokens `[documented]`), announces `WRITING WINDOW OPEN` before a multi-file batch, may fan out (read-only by default; writers only on disjoint file sets; no sub-agent runs git; a sub-agent's report is not evidence), commits by explicit path, and reports "after EVERY milestone, task, and step" with commit SHAs and exact test counts. It ends by `WRITING WINDOW CLOSED` + report, or `STANDING BY — <what it waits for>` if accepted and idle. It has **no hooks at all** (`kit/skills/implementer/SKILL.md` frontmatter has no `hooks:` block — verified with `sed -n '1,20p'`).

**Hop 7 — review.** `rev-1` exists from minute one. It is read-only *by construction*: `PrintTurnCommand_Builder.REVIEWER_DISALLOWED_TOOLS = [Write, Edit, NotebookEdit]` passed as `--disallowedTools … --`, plus a `PreToolUse: Bash` hook (`kit/hooks/reviewer-readonly-check.sh`, 40,881 B / 870 lines). Depth is a budget named by the supervisor (`quick` 0–1 agents … `max` 12–16) and the reviewer must state planned agent count before spending and actual count after. Report schema: `F1 · SEVERITY · CONFIRMED|REFUTED|UNPROVEN` blocks with `where/claim/defect/failure/evidence/refuted?`, a `coverage:` line, and a separate `OUT OF SCOPE (pre-existing…)` block — separated **by provenance, not severity** (decision 22).

**Hop 8 — ledger update.** The supervisor writes `<orch>/PLAN.md`. `Planning/PlanLedger_Parser.Parse_OrNull` regexes `^(indent)-\s*\[(x|X| |>|!|\?|-)\]\s*(text)$`, skipping the sections named by `PlanLedger_Sections` (PARKED, OWNER REQUESTS) so a parked discovery cannot inflate the denominator. Two Stop hooks make the write non-optional: `supervisor-ledger-check.sh` blocks the turn end while `<orch>/.ledger-behind` exists (raised by the app when a verdict was posted or an owner message arrived without PLAN.md being touched), and `run-to-the-end-check.sh` blocks it while any `- [ ]`/`- [>]` line is open unless the session declared `- [?]`, `- [!]`, `QUESTION:` or `WAITING ON`. Both defer to `.awaiting-answer` and `.meeting` to avoid the deadlock measured on 2026-08-11.

**Hop 9 — owner update.** The bridge mirrors channel entries to the topic (`〔orch〕 [imp-2 → sup] …`); per protocol only a question, an answer to one, and `BLOCKED ON OWNER` are *pushed*; progress narration stays in the channel while the app sends a 3-line status every ~30 min built from PLAN.md + live member states, and answers `/progress`, `/left`, `/tokens`, `/cost` from the same data.

## 1.2 Who wakes whom — the whole set

| woken | by | mechanism |
|---|---|---|
| supervisor | owner entry, any member entry | dispatcher tick (B) / Monitor fingerprint (A) |
| implementer, reviewer | supervisor entry, owner entry typed into the spoke | same |
| solo, general | owner entry only (`PrintTurn_Trigger.Is_Inbound`) | same |
| requester of a request file | the app's `FROM app` confirmation entry | same |
| everyone | `/resume` from the owner → `GO AHEAD — resume` entries | same |
| nobody | app bookkeeping (`turn_ended`, `STATUS`, nudges) — read on the *next* turn, never a trigger | by design (`Is_Inbound` excludes `App`) |

**What never happens:** nothing wakes a session inside its own turn. That single fact generates roughly 12 KB of compensating prose across three skills (§"AND ANSWER THEM AGAIN IF THEY ASK AGAIN", §"RE-READ THE CHANNEL AT WINDOW CLOSE", §"ANSWER THE OWNER BEFORE YOU WORK", the app's mid-turn narration, AWAY MODE).

---

# 2. Critique of the coordination design

Rating key: **W** works · **F** fragile · **O** over-engineered · **M** missing.

| # | mechanism | rating | one-line verdict |
|---|---|---|---|
| 2.1 | markdown channel as message bus | W/F | right medium, no schema |
| 2.2 | agent-written `[n]` + timestamp | F (mitigated) | the mitigations are excellent; the untrusted field is still the citation key |
| 2.3 | compaction | F | one-way hole, detected not closed |
| 2.4 | one channel per member (hub-and-spoke) | W/F | correct isolation, N-fold wake cost, zero shared memory between members |
| 2.5 | "the final message IS the entry" | W/O | elegant transport, wrong place for state |
| 2.6 | PLAN.md as task state | F | LLM-written, regex-parsed, Goodhart-able, unlinked to anything verifiable |
| 2.7 | hooks as advice | W (posture) / F (delivery) | the doctrine is right; the guards were measurably not running |
| 2.8 | request-file protocol | W/M | works in 2 s; no id, no idempotency, no dead letter, no authenticated requester |
| 2.9 | supervisor as single phone line | F | a topology problem paid for in prose |
| 2.10 | fan-out rules (decision 16) | W | the best-reasoned part of the kit |
| 2.11 | schema / state machine / acks / idempotency / dead letter | M | all absent as *mechanisms*; all present as *prose* |

**2.1 The channel as bus.** Append-only markdown over a filesystem is the right transport for this system: auditable, human-readable, survives the app, and cheap to tail. What it lacks is a schema. Every field the app needs is mined from prose by a matcher: subject/body split, `TO:`, the five question lines, six state markers, `IMAGE:`/`ATTACH:`, `WORKTREE:`. Each matcher has its own documented incident (mid-sentence markers pinning a state for four hours; `MUTATION WINDOW CLOSED` containing `WINDOW CLOSED`; a narration line becoming a subject on the owner's phone). These are not sloppiness — they are the predictable cost of parsing natural language as a wire format, and the kit pays it in three currencies: matcher complexity in C#/bash, rule prose in every skill, and incidents.

**2.2 Numbering and timestamps.** The best design decision in the repo is `Channels/ChannelEntry_Digest`: entry identity is a hash of the raw text, "derived from what it SAYS rather than from where it sits or what number it claims" — so delivery cursors are immune to both decision 12 (duplicated `[n]`) and decision 13 (non-monotonic counts under compaction). Paired with `channel-append.sh` allocating index and timestamp *inside* the lock, the failure class is genuinely closed for cooperating writers. Two residues remain: (a) `[n]` is still the **human citation key** ("act on entry [83]") and is still agent-visible, so a duplicate makes a citation ambiguous even when the machine no longer cares; (b) the helper's own header states the honest limit — "writers using the protocol cannot collide with each other", never "channel appends are atomic" — and the skills then instruct a **degraded unlocked append** on exit 127, i.e. the protocol documents its own bypass in four places (`implementer/SKILL.md` §Channel protocol; same in reviewer, general-supervisor, supervisor).

**2.3 Compaction.** `Channel_Compactor.COMPACT_ABOVE_ENTRIES = 90`, `KEEP_RECENT_ENTRIES = 45`. `PrintTurnDispatcherModel.Warn_IfEntriesWereArchivedUndelivered` detects the case where entries were archived before the bridge ever delivered them and logs it once per channel per app life — a good honest instrument, but it *reports* a data-loss window it does not close. And the archive is effectively write-only: opened by a session **twice in the whole 6→8 Sep window** `[documented]`, while 3 of 25 member channels are already compacted — which is exactly why a long-lived member's own brief can be unreachable to it (the C2 gap the review identified).

**2.4 Hub-and-spoke.** Isolation is correct: implementers never see each other, so a bad implementer cannot poison a peer's context, and each spoke is an exact copy of the proven 1:1 protocol. Two costs are structural, not incidental. First, the supervisor is woken by every spoke: ~400 wake-ups/day, 247 of them from member traffic, ~1 M tokens per wake-up `[documented]` — the topology converts every member's chatter into the most expensive role's context. Second, there is **no shared memory between members at all**: a fact imp-1 discovered (a build flag, a repo quirk, a dead end) reaches imp-2 only if the supervisor relays it, and the supervisor "briefs lean and has not read the code" (`supervisor/SKILL.md` §"Brief for parallelism"). A read-only, append-only `facts.md` per orchestration — written by members, never by the app, included in every state pack — would be nearly free and is missing.

**2.5 "The final message IS the entry".** As a transport contract it is genuinely good: no tool call, no lock, no double-write, and one incident class ("a member wrote its report TWICE") permanently closed. As a *state* contract it is the root of several problems at once. The same string must serve (i) the machine-parsed protocol, (ii) the append-only audit record, and (iii) the text on the owner's phone, translated. That is why `STATE:` must be *stripped* before the entry (spec C1.2), why `IMAGE:`/`ATTACH:` lines are removed from the mirrored text, why `Strip_LeadingNarration` exists, and why a supervisor's internal reasoning cannot be verbose without becoming phone noise. One artifact, three audiences, and the model has to satisfy all three in one message.

**2.6 PLAN.md.** It is the only durable task state in the system, it is written by an LLM in prose, parsed by one regex, and it drives the owner's progress bar, the 30-minute status, `/progress`, `/left` and two Stop hooks. Strengths: `PlanLedger_Markers` is a single definition with a test tying the bash copy to it; `PlanLedger_Sections` enforces the PARKED boundary in code (decision 22's half that can be enforced). Weaknesses:

- **`[x]` semantics are unverifiable.** "Done means built, reviewed by someone who did not write it, and no open blocking finding" — nothing in the app can check any of the three clauses. The parser sees a character.
- **Granularity is the metric's denominator and the supervisor owns it.** fincanva-2 closed 22 lines in 329 turns, fincanva-1 five in 204 `[documented]` — a 3× spread. Any "tokens per delivered item" measurement inherits that, which the token spec acknowledges and pins by freezing the line set.
- **No linkage.** A line is not tied to a brief, a member, a worktree, a commit, a reviewer verdict or an OWNER REQUESTS row by anything but prose ("a ledger line must trace to an OWNER REQUEST row" is a rule the parser cannot check). So the system cannot answer "which commit closed this line" or "which line is this member on" except by reading `WORKTREE:`/subject text.
- **The OWNER REQUESTS table** is the right idea (requests die between arrival and briefing) implemented as a markdown table an LLM must never renumber, in a file the same LLM is blocked from ending its turn without touching.

**2.7 Hooks as advice.** Decision 21's doctrine — hooks advise, the app enforces at the point of effect, a hook that cannot evaluate its predicate says so and allows — is correct, unusually well-argued, and implemented (`kit/hooks/hook-log.sh` drops `.guard-not-in-force`; the app records it). The delivery is another matter: measured in the 6→8 Sep window, `run-to-the-end` blocked **3 times out of 435** and **52 hook executions exited 127** (command not found) `[documented]`. Two of the four guards depend on `python3` for payload extraction, on a machine where CLAUDE.md decision 19 says `python3` is a native-Windows binary that cannot see msys paths. So the honest reading is: the *advisory* layer is partly not running, and the "app enforces" half exists for the ledger sections and for member kill/memory caps but **not** for the things the hooks currently carry (turn-end discipline, awaiting-answer, reviewer read-only shell).

**2.8 Request files.** Latency and ack are good: drop JSON, executed within ~2 s, confirmed by a first-class `FROM app` entry that wakes the requester. What is missing is the boring transactional furniture:

- **No request id.** Identity is the *filename*, and agents are told by prose to make it unique ("Put your orchestration id and a timestamp in the FILENAME — every supervisor writes into the same folder"). A field the app generates would be free.
- **No idempotency key.** The consequence is written down as a rule instead: the general supervisor is *forbidden* to retry a failed `start-orchestration` because doing so once created duplicate orchestrations (`general-supervisor/SKILL.md` §Boot sequence). That is an idempotency hole patched in prose, and it costs the owner a lost start when the turn dies in the announce→write window (the reason the skill now insists on writing the file *before* telling the owner).
- **No dead letter.** Malformed files are logged with a reason and deleted (`OrchestrationRequests_Reader` docstring). Deleting is right for liveness, but the agent's intent is then gone.
- **No authenticated requester.** `requester` is documented as "what they are shown, not a gate"; role restrictions (general supervisor may not `add-implementer`) live only in skill prose.

**2.9 The single phone line.** "Every minute you spend working is a minute the owner is talking to a wall" (`supervisor/SKILL.md` §"STAY REACHABLE") is a correct diagnosis of a topology defect, and the system has now tried five compensations: a dedicated COMMUNICATOR role (built, then **retired**), app-side mid-turn narration read off the transcript, the receipt rule, "answer again if they ask again", AWAY MODE with parking, and the 30-minute app status. All of it is prose and app machinery around one fact: the role that talks to the owner is the role that blocks. C4 (digest member wake-ups) attacks the volume; the structural fix — a cheap, always-idle owner-facing turn that can *read* state and *route*, with the supervisor woken only to decide — is the thing that was built as COMMUNICATOR and thrown away for being a second voice. It was thrown away for the right reason (two voices answering) and the wrong scope (it was allowed to narrate, not to answer from state).

**2.10 Fan-out (decision 16).** The strongest piece of protocol design here. The asymmetry is argued from the mechanism (a sub-agent blocks its caller's turn; the supervisor's turn is the owner's phone line; an implementer's turn is *meant* to block), the writer rule is a real invariant (disjoint named file sets; ambient files and git stay with the coordinator), the anti-laundering rule ("a sub-agent's report is NOT evidence") is stated rather than implied, and the session-vs-fan-out test ("is this a second deliverable, or the same one going faster?") is exactly the right boundary. Two residual risks: sub-agent tokens are **invisible to the app's accounting** (sidechains are 13.1 % of consumption and `result.usage` excludes them `[documented]`), and a wide fan-out runs inside the session's own 3 G cgroup (`RunnerConfigs_Factory.DEFAULT_SESSION_MEMORY_MAX`), so "as many as the work has distinct angles" is bounded by a limit nobody told the model about.

**2.11 What is missing as mechanism.** Structured message schema; explicit task state machine; acknowledgement semantics *for work* (delivery is acked by the cursor — a brief being *accepted*, a verdict being *acted on*, is not); idempotency keys; dead letter; causal threading (`in_reply_to`); priority/preemption (owner traffic is ordered first inside a coalesced turn but cannot interrupt a running one). Every one of these exists in the system as a **rule an agent must remember**, which is precisely the class of thing the kit's own hook headers say does not work: *"the difference is enforcement, not diligence"*.

---

# 3. Prompt / skill engineering critique

## 3.1 Measured sizes (`find kit/skills -type f | xargs wc -c`)

| file | bytes | ~tokens @3 B | anecdote-marked¹ | exact-dup² | imperatives³ |
|---|---|---|---|---|---|
| `supervisor/SKILL.md` | 87,271 | ~29.1 k | **49 %** | 14 % | 112 |
| `solo/SKILL.md` | 42,160 | ~14.1 k | 31 % | 29 % (35 % near-dup of supervisor) | 56 |
| `general-supervisor/SKILL.md` | 29,818 | ~9.9 k | 20 % | 15 % (20 % near-dup) | 41 |
| `implementer/SKILL.md` | 24,951 | ~8.3 k | 35 % | 15 % | 30 |
| `reviewer/SKILL.md` | 21,696 | ~7.2 k | 28 % | 12 % (16 % near-dup of implementer) | 18 |
| `communicator/SKILL.md` | 7,082 | ~2.4 k | 17 % | 29 % | 13 |
| `subagents/SKILL.md` | 4,108 | ~1.4 k | 0 % | 0 % | 8 |
| `supervisor/reference/watcher.md` | 11,879 | ~4.0 k | — | 5 % | 13 |
| `supervisor/reference/stream-runner.md` | 6,609 | ~2.2 k | — | 0 % | 5 |
| 5× `*/reference/print-runner.md` | 945 each | ~0.3 k | — | **90 %** (byte-identical across 5 roles) | 0 |
| **all skills + references** | **269,017** | **~90 k** | | | |

¹ share of paragraph bytes containing a date, "measured", "really happened", "went wrong", "verbatim", "Their words", "used to". ² exact paragraph repetition across files, ≥120 chars, whitespace-normalised. ³ occurrences of NEVER/MUST/HARD RULE/never/always/ALWAYS.

**What a session actually loads at boot** (skill + the reference its own text orders it to read):

| role · regime | bytes | ~tokens |
|---|---|---|
| supervisor · stream | 93,880 | **~29–34 k** |
| supervisor · terminal | 99,150 | ~31–35 k |
| solo · print | 43,105 | ~14 k |
| general · print/**fresh** | 30,763 | ~10 k **per turn** |
| implementer · print | 25,896 | ~8.6 k |
| reviewer · print | 22,641 | ~7.5 k |

## 3.2 The C5 parking is falsified by arithmetic on the review's own numbers

The token spec PARKED the skill diet, with a reopen criterion: *"Reopen only if a measured empty `-p` turn (with and without the skill) shows the skill above 40 % of the supervisor's boot"* (`2026-09-08-token-efficiency-design.md` §C5). The supervisor's measured first-call boot is **55.9–56.9 k** `[documented]`. Its skill + stream-runner is 93,880 B ≈ **29.3–33.5 k** at the review's own 2.8–3.2 B/tok calibration — **52–60 % of that boot**. The criterion is already met without running the empty `-p`; only the *precision* of the split is unmeasured, not the order of magnitude. For members the parking is right (≈8 k of 34–47 k, dominated by Claude Code's own prompt); **for the supervisor it is not**, and the supervisor is the role that wakes ~400 times a day.

## 3.3 Rule density versus actionable procedure

The supervisor skill is 154 paragraphs, 112 imperative markers, and **49 % incident narrative**. Structurally it is a *case-law compendium*, not a procedure. That has real value — the anecdotes are why each rule survives contact with a session that thinks it knows better, and the kit is explicit about it ("read the relevant one BEFORE you propose changing, relaxing or 'improving' any of them"). But it is billed on every boot, and in `resume: fresh` on every *turn*, to a session that in 15 % of its turns produces < 600 output tokens `[documented]`.

The split is mechanical and cheap: the *rule* (imperative, ≤ 2 lines) stays in `SKILL.md`; the *account* moves to `reference/why-<topic>.md`, loaded only when the session is about to change, relax or argue with a rule — which is precisely when the story is needed. On the measured shares, a supervisor `SKILL.md` of 12–15 KB (≈ 4–5 k tokens) is reachable without deleting a single rule.

## 3.4 Duplication

- Six `print-runner.md` files, five of them **byte-identical** (`diff -q` → identical for reviewer, solo, general-supervisor, communicator against implementer's). The supervisor's differs and *deliberately* cross-references `stream-runner.md` §5–6 rather than copying — the right instinct, applied once.
- `solo/SKILL.md` is **35 % near-duplicate** of `supervisor/SKILL.md` (≥0.75 similarity). It re-states TELEGRAM STYLE, `/pc`, the receipt rule, "answer again", the platform-code table, the ledger legend, RUN TO THE END.
- The watcher scripts are copied per role with per-role source lists (`implementer` vs `reviewer` share 22–23 % of paragraphs exactly).
- The platform-code table (`SL`, `AS`, `OL`, …) lives in supervisor, solo and general-supervisor — machine-local product vocabulary hard-coded three times in a "portable kit".
- `channel-append.sh` exit-code lore (~1.5–2.5 KB) appears in four skills, and is **forbidden to use** in the live regime.

## 3.5 Contradictions between shipped instructions (live, not hypothetical)

1. **Fresh mode vs boot step 1.** `implementer/SKILL.md` §"Boot sequence": *"Read your channel top to bottom."* `PrintTurnPrompt_Builder.FRESH_SESSION_PREAMBLE`: *"the bridge tracked them, so do not re-derive them from the channel."* Both arrive in the same turn, and the skill arrives *second* (it is the positional prompt's expansion). The spec's own morning plan anticipates the loser: *"If fresh turns still spend 9–10 calls, the preamble lost to the skill's 'read your channel top to bottom'"*.
2. **Greeting.** Skill step 2 mandates an entry with subject *exactly* `imp-1 online`; `FRESH_SESSION_NO_GREETING` forbids it. Again resolved by prompt text against skill text.
3. **Watcher.** Every skill ends with: *"READ `reference/watcher.md` NOW, at boot … Nothing but that Monitor ever wakes you: a turn that ends without it armed ends this session's participation."* In print/stream that is false and `print-runner.md`/`stream-runner.md` countermand it — but the countermand is conditional on the session having *first* run the env-resolving Bash call, and the unconditional sentence sits at the end of the file where recency favours it.
4. **The COMMUNICATOR is declared retired and still shipped.** `supervisor/SKILL.md` §"TELEGRAM STYLE": *"The COMMUNICATOR role is retired … nothing writes them any more."* The kit still ships `communicator/SKILL.md` (7,082 B) with instructions to tail the supervisor's transcript, `SessionRoles.Communicator`, `Build_RoleCommand(... ) → "/communicator <orch>"`, a `communicatorModel` config key, `.communicator.usage.json` in `UsageTotals_Reader`, and a `🟢 Com:` mirror tag.
5. **`subagents/SKILL.md` is machine-local policy inside a portable kit.** It names the owner ("Nathan"), forbids Fable by name, and quotes `OrchestratorConfig_Loader.cs` key names. It is also the only skill with `disable-model-invocation` *absent*, i.e. the only one a model may pick up on its own.
6. **JIT at the wrong granularity.** `solo/SKILL.md` §"How you work" tells the session to read the *implementer's whole 24,951-byte SKILL.md* to get one section ("Fan out"), via a shell-composed path. That is a ~8 k-token read to retrieve ~1 k tokens of rules.

## 3.6 Against the published good practice

| practice (Anthropic, *Building effective agents* / *Effective context engineering*) | here |
|---|---|
| minimal, high-signal system prompt | ✗ for supervisor (~29–34 k tokens, 49 % narrative); ✓-ish for members (~8 k) |
| just-in-time context retrieval | partial — `reference/` exists for 3 of ~25 sections; the *state* a turn needs (brief, own last report, git status, ledger lines) is rediscovered by the model instead of handed to it (7.8 of 9.2 calls on the one fresh role `[documented]`) — C2 addresses exactly this |
| structured note-taking across context resets | ✗ today. PLAN.md is the only durable note and it is owner-facing, prose, and enforced by a Stop hook. `STATE:` is designed (C1.2) and not built |
| sub-agent compaction / delegation for context isolation | ✓ for implementer/solo/reviewer (decision 16); ✗ for the supervisor, where it is banned (correctly) with nothing put in its place |
| tool-result clearing / bounded context | ✗ — `resume: transcript` keeps everything; median first-call context 249 k, 76 % of tokens in calls above 200 k `[documented]`. `--autocompact` is available and not passed (C10, owner decision pending) |
| tools over prose contracts | ✗ — the protocol is markers in prose (see §4.7) |

---

# 4. Reimplementation of native primitives

| # | the kit's version | native alternative | justified? |
|---|---|---|---|
| 4.1 | persistent `Monitor` fingerprint watcher, 38 KB across roles (`*/reference/watcher.md`) | bridge dispatcher + `claude -p` per turn (already built and live) | **was** justified (2026-08-07: 29 background Bash watchers reaped across 4 sessions; Monitors survived 41 min) — **not now**. Two wake mechanisms for one job, one of them dead on the daemon host, both shipped at full weight |
| 4.2 | channel files as transport & memory | Agent SDK sessions, `--resume`, `--fork-session` | **justified as transport** (audit trail, human-readable, app-down operation, cross-process). `--resume` *is* used. `--fork-session` correctly rejected by the review (same transcript, no saving). **Not justified** as the way a turn *rediscovers* its own state |
| 4.3 | `PLAN.md` | `TodoWrite` / Tasks | **justified** as the owner-facing artifact (TodoWrite is invisible to the app and dies with the session; the awaiting-answer hook even whitelists `TodoWrite` as harmless *because* it changes nothing). **Not justified** as the only task state: it should be a *projection* of app-owned state, not the state |
| 4.4 | statusline-as-telemetry probe (`kit/statusline/statusline.sh` §"TELEMETRY PROBE" → `.usage.json`) | `--output-format json` result usage (already captured per turn by `TurnLog_Store`), transcript `usage` records, OpenTelemetry export (`CLAUDE_CODE_ENABLE_TELEMETRY`) | **not justified any more, and the code says so**: `IPrintTurnDispatcher.cs:20`, `Status/MemberWorking_Decider.cs:24`, `Limits/RateLimitEvent_Translator.cs:11` all note that a headless `-p` session renders no status line, so the probe files do not exist on a bridge-driven host — yet `Usage/UsageTotals_Reader` is still the single reader behind the cards, `/tokens` and `/cost`. Grep found **zero** mentions of telemetry/OTEL anywhere in the repo: the native metrics path has never been evaluated `[my measurement: grep over the checkout]` |
| 4.5 | `reviewer-readonly-check.sh` — 40,881 B, 870 lines, a quote-aware shell lexer in bash+python3 | `--disallowedTools` (already used), `--permission-mode`, settings `permissions.deny` rules, or the sandbox the repo already owns (`Running/SessionSandbox/MemoryLimitedInvocation_Builder` — a cgroup wrapper) | **partly justified, largely over-engineered.** The tool denial is the real boundary; the lexer exists to police `Bash`, and its own header records four successive failed matcher designs. A reviewer that gets read-only tools plus a read-only mount (or no `Bash` at all, plus `Grep`/`Glob`/`Read`) needs none of it. This is the single most expensive artifact in the kit per unit of guarantee |
| 4.6 | separate OS processes per member, with pid files, watchdog, respawn, tree-kill, cgroup caps | `Agent` tool / sub-agents inside one turn | **justified** for the *reviewable-deliverable* boundary: independent context, own model, own worktree, survives app restart, cost attributable, owner-visible. Decision 16's session-vs-fan-out test draws the line exactly where it belongs. The *within-deliverable* parallelism correctly uses sub-agents |
| 4.7 | protocol as markers in prose + `channel-append.sh` on `PATH` | an MCP server exposing `channel.append`, `plan.set_line`, `request.submit`, `state.put`, `question.ask` | **not justified.** Typed tool calls would delete: the exit-code lore (~2–4 KB × 4 skills), the five-line question shape and its refusal path, `TO:` splitting, the header-shape incident class, `Strip_LeadingNarration`, `Neutralise_HeaderLines`, marker mis-spelling, and the "final message IS the entry" ambiguity — while *keeping* the markdown channel as the projection everyone reads. The repo already knows how to register MCP at user scope (`README-daemon.md`, last paragraph), so the capability is understood; it is simply not used for the protocol itself |
| 4.8 | `.awaiting-answer` flag + `PreToolUse` deny to stop a supervisor after a question | in print/stream this is *free*: a question ends the turn because the process ends | **obsolete in the live regime.** `stream-runner.md` §4 says it outright: *"There is no tool-denial hook holding you: the turn simply finishes."* The 11,625-byte hook, its deadlock guards and its interaction with two Stop hooks exist for regime A |
| 4.9 | `run-to-the-end` + `ledger-behind` Stop hooks | app-side gating (it already raises the flags), or a state machine that simply *knows* what is open | posture is right (decision 21) but the measured hit rate (3/435, 52 × exit 127 `[documented]`) says the lever is mostly not pulling. The escapes are also a taught vocabulary (`WAITING ON`, `- [?]`, `- [!]`, `QUESTION:`) — four more prose markers |

---

# 5. What a greenfield protocol redesign looks like

The shape below keeps everything the system has proven (append-only files, one supervisor gate, hub-and-spoke isolation, owner on a phone, works with the app down) and moves the *contract* from prose to types.

## 5.1 Event log as truth, markdown as projection

`<orch>/events.jsonl`, append-only, one JSON object per line, written by the app and by a tiny `aiorch` CLI (so a human or an app-down session can still append a valid event):

```json
{"id":"01J…","ts":"2026-09-08T22:34:11Z","orch":"fincanva-3","from":{"role":"implementer","member":"imp-2"},
 "to":"supervisor","kind":"report","task":"T-14","in_reply_to":"01J…","idem":"imp-2/turn-7",
 "body":{"summary":"parser ported","commits":["abc1234"],"tests":{"passed":2574,"failed":0,"skipped":9},
         "windows_closed":["writing"],"noticed":[{"file":"X.cs:88","what":"…"}],"agents":{"planned":3,"actual":2}},
 "state":{"branch":"stage/4b","commit":"abc1234","verify":"dotnet test …","next":"Y.cs:212","dead_ends":["…"]}}
```

`channel.md` becomes a **rendered projection** of that log — the same file the owner, the mirror and any human read, regenerated on append. Nothing about auditability or human readability is lost; what is lost is the ability to write a *malformed* entry, which is a feature (the "invisible entry" class disappears).

## 5.2 An explicit task state machine, owned by the app

`proposed → approved(owner) → briefed → in_progress → reported → in_review → verdict(accept|rework) → merged → closed`, with `parked` and `dropped` as terminal side-states. Transitions happen **only** through typed calls, each of which the app can validate:

- `plan.propose(line, owner_request_row)` — refuses a line with no owner-request row (decision 22, enforced instead of instructed).
- `brief.send(task, member, contract[], worktree, model, parallel_units[])` — the transition to `briefed`; the app creates the worktree, so the `WORKTREE:` marker disappears.
- `report.file(task, evidence)` → `reported`. `review.file(task, findings[], coverage)` → `in_review`. `verdict.set(task, accept|rework, by)` → the *only* path to `[x]`, and it records **who** cleared it, so "reviewed by someone who did not write it" becomes checkable rather than promised.
- `merge.record(task, commit)` → `merged`.

PLAN.md is then generated from this, and the two Stop hooks become unnecessary: the app knows what is open, who owes what, and whether the ledger is stale — it does not need to ask a model to keep a file honest.

## 5.3 Briefs, reports, verdicts as typed objects

Each has required fields (a brief without a completion contract is refused at the call, not complained about at turn end), each renders to the prose an agent and the owner read, and each carries `task` — which is what gives the system causality for free: "which brief is this member on", "which verdict closed this line", "how many briefs were re-issued for this line" (the rework metric the token spec needs) all become queries instead of greps.

## 5.4 Bridge-composed state pack per turn

Exactly the review's C2, with its four additions, and cache-ordered: **stable first** (system prompt → role prompt → PLAN projection) **volatile last** (brief, own last report/`state`, `git status --short` + `git log -3` of the worktree, pending entries, roster, contract). Budget ≤ 6 k tokens. The role prompt stops saying "read your channel"; the channel becomes what it should be — the record you consult for a fact.

## 5.5 Small role prompts + on-demand reference

`SKILL.md` ≤ 12–15 KB per role: identity, the transitions it may make, the markers it must produce (ideally none, once calls are typed), the hard gates. Everything else in `reference/`: `why-<topic>.md` (the incident law), `runner-<mode>.md` (one file per regime, *selected* by the app rather than read-then-countermanded), `fanout.md` (one copy, referenced by implementer and solo, not duplicated). Target on today's content: supervisor from ~29–34 k to ~5 k tokens resident.

## 5.6 Deterministic acknowledgement

Three acks, all app-generated, none costing a model turn: **delivered** (exists today as the cursor digest), **received** (the turn that consumed an event records the event ids it acted on — today `executed_turns` records indices but not the stage session id, which C7 needs), **acted** (a transition happened). "Has the supervisor answered the owner yet" then stops being a heuristic over channel text (the bug decision 13 records) and becomes a lookup.

## 5.7 Per-task worktree lifecycle owned by the app

`briefed → worktree created`, `merged → worktree removed`, one worktree per task rather than per member. This removes the class of incident the supervisor skill spends ~1 KB on (retiring a live member off a stale pid and committing into another's worktree) and the 20-open-worktrees end state decision 22 describes.

## 5.8 What is lost, and how to keep it

| lost | mitigation |
|---|---|
| agent flexibility — an agent can no longer say something the schema did not anticipate | every event carries a free `notes` string, and a `kind:"note"` event with an unconstrained body is always legal. The schema constrains *transitions*, never speech |
| human readability | the markdown projection **is** the human artifact, and it is generated, so it can be *better* than what a model writes by hand (stable headers, real indices, no malformed entries) |
| works-with-the-app-down | keep the file transport and ship the `aiorch` CLI that appends valid events and re-renders the projection. Degraded mode becomes "no state machine transitions, but the conversation continues" — stated in the log, not silently |
| the audit trail you can read top to bottom | unchanged: the projection is that file; the JSONL is the machine's copy of the same facts |
| incremental delivery | the split is naturally four packages, in the repo's existing stage discipline: (1) state pack + fresh-mode prompt reconciliation; (2) typed calls via MCP for append/plan/request, projection generated; (3) task state machine + verdict authorship; (4) prompt diet + reference split |

---

# 6. Top 10 findings

Impact scored **R** reliability · **T** token cost · **O** owner experience (H/M/L).

| # | finding | R | T | O |
|---|---|---|---|---|
| 1 | **Two contradictory instruction sets are live in fresh mode.** `FRESH_SESSION_PREAMBLE` ("do not re-derive from the channel", "file no greeting") versus `*/SKILL.md` §Boot ("read your channel top to bottom", "append `<member> online`"). The skill arrives as the positional prompt's expansion, i.e. *after* the stdin preamble in intent and *around* it in fact, and nobody has measured which wins. Round 1's entire saving hangs on it. **Fix: select the boot text by regime (`boot-fresh.md` / `boot-transcript.md`) instead of shipping both and countermanding one.** | **H** | **H** | M |
| 2 | **The supervisor prompt is 29–34 k tokens ≈ 52–60 % of its measured 55.9–56.9 k boot**, on the role that wakes ~400×/day at ~1 M tokens per wake-up. C5's reopen criterion ("skill above 40 % of the supervisor's boot") is already satisfied by arithmetic on the review's own calibration. 49 % of that file is incident narrative that is only needed when a rule is challenged. **Fix: rule/why split; supervisor `SKILL.md` to 12–15 KB with zero rules deleted.** | M | **H** | L |
| 3 | **No task state machine, and `[x]` is unverifiable.** PLAN.md is LLM prose parsed by one regex, unlinked to brief, member, commit, verdict or owner-request row; line granularity varies 3× between orchestrations `[documented]`, so it is both the owner's only progress signal and the denominator of every efficiency metric. **Fix: app-owned transitions; `[x]` only via a recorded verdict by a non-author.** | **H** | M | **H** |
| 4 | **Cost and limit telemetry is structurally blind on the live host.** The single reader (`Usage/UsageTotals_Reader`) consumes `.usage.json` files written by the *status line*, which a headless `-p` session never renders — the code says so in three places — while `result.usage` misses sidechains (13.1 %) and resets at background-task notifications (14 % of turns) `[documented]`. So `/cost`, `/tokens`, the cards and the 90–100 % limit alerts are wrong or idle on the daemon. C7 fixes the transcript half; **the native `--output-format json` per-turn result is already being written to `turns.jsonl` and OTEL export has never been evaluated (zero mentions in the repo).** | **H** | M | **H** |
| 5 | **The owner's only line is a blocking turn.** Five compensations exist (retired COMMUNICATOR, app narration, receipt rule, answer-again rule, AWAY MODE) for ~12 KB of prose across three skills. C4 reduces the volume; nothing restores an always-answerable voice. **Fix: a cheap read-only owner-facing router with access to the state pack — the COMMUNICATOR idea, allowed to answer *from state* rather than narrate a transcript.** | M | M | **H** |
| 6 | **The advisory layer is measurably not running.** 52 hook executions exited 127; `run-to-the-end` blocked 3 times in 435 `[documented]`; two of four guards depend on the `python3` that decision 19 says is unreliable on this machine. Decision 21 promises the app enforces at the point of effect — for turn-end discipline, awaiting-answer and reviewer shell, that app-side half does not exist. **Fix: move each of those three to the point of effect, or accept and document them as advice only.** | **H** | L | M |
| 7 | **`reviewer-readonly-check.sh` is 40,881 B / 870 lines of quote-aware shell lexing** whose own header records four failed matcher generations, to police a `Bash` tool the reviewer could simply not have — while the actual guarantee comes from `--disallowedTools Write Edit NotebookEdit`. The repo already owns a sandbox wrapper. **Fix: read-only tools + read-only mount (or no Bash); delete the lexer.** | M | L | L |
| 8 | **The request protocol has no id, no idempotency key and no dead letter.** The missing idempotency is currently patched by *forbidding retries* in prose, after duplicate orchestrations were created; the cost of that patch is a start silently lost when a turn dies in the announce→write window (documented, 2026-08-25, recovered only by `/resume` twelve minutes later). `requester` is unauthenticated by design. **Fix: app-generated request id + `idem` key + a `.requests/failed/` folder.** | **H** | L | M |
| 9 | **The protocol is a marker vocabulary parsed out of prose.** `STANDING BY` (strict, must lead the subject alone), the four window phrases (mis-spelled by 2 of 10 members in one day), the five mandatory question lines, `TO:`, `WAITING ON` (with a trailing-boundary regex because "WAITING ONLY" contains it), `IMAGE:`/`ATTACH:`, `WORKTREE:`. Each costs matcher complexity, skill prose and incidents in both directions. **Fix: typed MCP calls; keep markdown as the projection.** | **H** | M | M |
| 10 | **Dead and half-live surfaces are shipped at full weight.** 38 KB of watcher protocol + the 11.6 KB awaiting-answer hook belong to a regime the daemon cannot run; the COMMUNICATOR is declared retired in one skill and shipped in six places; five `print-runner.md` files are byte-identical; `solo` is 35 % near-duplicate of `supervisor`; 6–13 % of channel bytes are app bookkeeping a fresh session re-reads `[documented]`; the compaction archive was opened twice in three days while 3 of 25 channels are already compacted. **Fix: one wake mechanism, one copy of each shared rule, bookkeeping out of the channel (C3).** | M | **H** | L |

## What is genuinely good, and should survive any redesign

`Channels/ChannelEntry_Digest` (identity from content, immune to decisions 12 and 13) · `kit/bin/channel-append.sh` (lock as a directory, index and stamp inside the lock, distinguishable exit codes, honest statement of what it cannot bind) · `kit/hooks/hook-log.sh` (a guard that cannot decide says so, allows, and leaves a marker the app records) · `Planning/PlanLedger_Sections` + `PlanLedger_Markers` (one definition, tests tying the bash copy to it) · `PlanLedger_Parser`'s PARKED enforcement (decision 22's enforceable half) · decision 16's session-vs-fan-out test and the "a sub-agent's report is not evidence" rule · `RunnerFallback_Ladder` naming the rung it cannot run instead of hiding it · drain-before-stop (`7e13ff8`) · `Warn_IfEntriesWereArchivedUndelivered` (a data-loss window made audible) · and the discipline, visible in every file header, of recording *why* a rule exists with the date it was paid for.
