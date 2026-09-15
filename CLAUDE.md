# AI Orchestrator — AI Context

This file is loaded automatically by Claude Code at the start of every session in this repo. Read it fully before doing any work.

## What This Project Is

A portable orchestration kit that generalizes a proven two-agent supervision pattern to N agents, with Telegram as the owner's window into everything.

**The pattern being generalized** (born in the Da-Vinci-Fintech-Suite repo, file `SUPERVISION-CHANNEL.md` on the `portfolio-live-following` worktree — read it for the reference protocol): one SUPERVISOR Claude Code session (expensive model, fresh context, reviews and gates) and one IMPLEMENTER session (cheaper model, writes code) coordinate through an append-only markdown channel file. Each session arms a background file-watcher before ending every turn, so an append by one side wakes the other (duplex, self-waking, no human relay). The owner interfaces only with the supervisor, at high level. It works extremely well; this project productizes it.

**What this repo builds:**
0. **General supervisor** (added 2026-08-06) — an always-on concierge session with its own channel
   (`~/.claude/supervision/general/channel.md`) mirrored to the Telegram supergroup's General topic
   (pinned by the owner). The owner texts it "work on skeleton client" from their phone; it resolves
   the repo and starts/closes whole orchestrations via the request-file protocol below.
1. **Orchestrator app** (this solution) — a desktop control app that owns:
   - The repo list (seeded from the owner's project registry; editable).
   - "Start supervisor" per repo: spawns a Claude Code terminal session (`wt new-tab -d <repo> -- claude "/supervisor <orch-id>"`) and creates the orchestration session.
   - "Add implementer" per orchestration: spawns N implementer terminals, each with its own id (`claude "/implementer <orch-id>/imp-<n>"`).
   - The **Telegram bridge loop** (background, in-process): tails all channel files → mirrors appends to Telegram; long-polls `getUpdates` → routes owner messages into supervisor inbox files.
   - Status: which sessions/orchestrations are alive, last activity per channel.
2. **Claude Code role commands** — `/supervisor <id>` and `/implementer <id>` installed user-level (`~/.claude/commands/`), available in every project. Each loads the full role protocol (channel paths, watcher command, report format, staging discipline).
3. **Installer** — sets up a new machine: prompts for bot token / supergroup chat id / owner user id, writes local config, installs the role commands, seeds the repo list.

## Architecture Decisions (locked so far — brainstorm in progress, spec pending)

1. **Telegram is a MIRROR + owner console, never the agent↔agent transport.** Hard Bot API constraints force this and it is also the better design: bots cannot create group chats (only forum topics), bots never see other bots' messages, and one bot token allows a single concurrent `getUpdates` poller. Agent↔agent traffic stays on local channel files (append-only, auditable, watcher-wakeable — the proven mechanism). The bridge mirrors outbound and injects inbound.
2. **One Telegram forum supergroup, one topic per orchestration session.** The owner creates the supergroup once (Topics enabled, bot as admin); the app creates a topic per orchestration id via `createForumTopic`. Owner messages typed into a topic are inherently supervisor-only, because implementers never read Telegram at all.
3. **Central channel home, outside every git tree:** `~/.claude/supervision/<orch-id>/` containing `session.json` (repo path, member roster + PIDs, telegram topic id), `owner-channel.md` (duplex owner ⇄ supervisor: bridge appends inbound Telegram messages, supervisor appends replies/questions, bridge mirrors the supervisor's entries back to Telegram), `orchestrator.log.jsonl` (structured per-orchestration log), and `imp-<n>/channel.md` (one duplex channel per implementer). This kills the accidental-`git add -A` hazard the in-repo channel file had, and makes bridge discovery trivial (watch one folder; new orch-id folder ⇒ create topic).
4. **Hub-and-spoke topology, one supervisor : N implementers.** Each implementer gets its OWN channel file with the supervisor — an exact copy of the proven 1:1 duplex protocol. Implementers never see each other's traffic. The unified "group conversation" view exists only in the Telegram mirror, where the bridge merges all spokes chronologically tagged `[sup → imp-2]`, `[imp-1 → sup]`, etc.
5. **The app spawns sessions in real terminals** (Windows Terminal `wt new-tab`, fallback `Start-Process`), passing the role slash command as the initial prompt so the session knows its role and id from message one. Mac later via `osascript`; same design, different launcher line.
6. **Portable kit:** everything a new machine needs (app, role commands, config template) installs from this repo (`kit/install.ps1`). Owner uses this across multiple machines.
7. **Request-file protocol (`~/.claude/supervision/.requests/*.json`)** — agents ask, the app executes (~2 s), and confirms with a first-class `FROM app` channel entry that wakes the requester's watcher. Actions: `start-orchestration` (repo only — ids are auto-allocated `repo-slug-n`) + `close-orchestration` (general supervisor), `add-implementer` + `close-implementer` (orchestration supervisors), `set-telegram-muted` (any supervisor — DND). Closing marks `ClosedUtc` in session.json (audit trail kept, tailing stops, UI dims, terminals killed); the orchestration close also closes its Telegram topic. See the spec's AMENDMENT sections.
8. **Lifecycle (spec AMENDMENT 2, revised):** sessions are always-on while the app runs — pid files (written by the spawned shells) + `SessionWatchdog` respawn anything dead (general supervisor auto-starts); app exit tree-kills every session (and closes their terminal windows); app restart brings everything back. **Resume (revised 2026-09-10, owner request): a SUPERVISOR or SOLO respawn continues its OWN conversation with `claude --resume <session-id>`** — the id comes from the slot's `.usage.json` (the statusline probe already dumps `session_id` + `transcript_path`), and `ResumableSession_Resolver` names it ONLY when that transcript still exists and is non-empty, because `--resume` of an unknown id prints "No conversation found" and exits, which under the watchdog is a respawn loop. The first spawn has no probe file, so it is fresh without anyone telling a first spawn from a respawn. Every respawn path (watchdog, `/model`, `/effort`, app restart) goes through the same two launcher methods, so there is one apply path. **Never `--continue`**: it guesses the most recent conversation in a repo directory several sessions share. **Implementers and reviewers still re-enter fresh** through their role command (the channels are their durable state), and **the general supervisor stays stateless** across launches by owner directive (memory = its own CLAUDE.md + the channel read as a LOG — closed/failed requests are never auto-retried on boot; `--continue` once re-ran a failed start and duplicated orchestrations). A resumed session must still trust the CHANNEL over its memory of what it was about to do — the role commands say so. **After the 2026-09-11 fork merge, this covers only the terminal runner** — gated on `runners.<role>.runner == terminal` AND `runners.<role>.resume == transcript` in the merged config (`IRoleRunnerConfig.Resume`, `AIOrchestratorCoreLib/Running/RoleRunnerConfig/`). **Bridge-driven sessions** (`runners.<role>.runner = print|stream`) are driven by the dispatcher instead of a terminal: `ResumeModes.Transcript` (`AIOrchestratorCoreLib/Running/ResumeModes.cs`) resumes the print session so every turn after the first keeps context, and `ResumeModes.Fresh` starts empty with a state pack rebuilt from disk. Same config key (`Resume`), two runners reading it. **Never `--continue`, in either runner.**
9. **DND with catch-up:** mute pauses outbound Telegram by FREEZING tailer offsets — unmute (UI, request, or the owner texting anything) delivers all pending traffic in one burst. The general supervisor's "check-in ritual" (summary digest of all orchestrations + pending questions per topic) is protocol.
10. **Telemetry:** `statusline.ps1` doubles as probe — dumps raw statusline JSON to per-session `.usage.json`; UI shows per-member cost/task/worktree(`WORKTREE:` marker)/time-on-task; engine texts usage-limit alerts at 90/95/97/98/99/100% (schema-tolerant parser — NEEDS LIVE VERIFICATION that this Claude Code version exposes limit data). "Show session" foregrounds a terminal by title (sessions spawn one WT window each, `wt -w new`).
    **Usage figures have ONE reader** (`UsageTotals_Reader`): `Build_PerSourceTotals` is the
    per-session lifetime breakdown and `Build_OrchestrationTotals` is that list summed, so the
    cards, the detail window, `/tokens` and `/cost` can never disagree — and every source passes
    exactly once through the respawn accumulator. `/cost` is the money reading (per-session share
    + burn rate, suppressed under 15 min as meaningless); `/tokens` is the token reading.
11. **The translation layer is gone from this tree as of the 2026-09-11 fork merge** — no
    `Translation/` tree, no `telegramItalianLayer` key, no `Create_WithItalianLayer`, no `/italian`
    command; the code that used to back this paragraph (`/italian` toggled the layer from the phone,
    with the app's status-bar checkbox mirroring it, PERSISTED to config.json so the provider
    reloaded on the file's write stamp rather than keeping an in-memory copy in step) does not exist
    anymore. **Whether that stays permanent is still open**: the 2026-09-11 spec's §11.1
    (`docs/superpowers/specs/2026-09-11-fork-merge-and-per-user-profiles-design.md`, on branch
    `feat/fork-merge-and-profiles-spec`, not in this worktree) asks the owner to confirm dropping it
    in favour of the fork's rule ("with the owner, write in the owner's language"); the owner has not
    answered. Until they do, treat the deletion as the tree's *current* state, not as a *settled*
    decision — if the answer is "keep it," a future task re-ports the layer described above from
    master's history; it is not merely un-deleting a flag.
12. **Channel headers WERE agent-written — the allocation is now the TOOL's, and the duplicate-index
    danger has moved rather than gone (corrected 2026-09-15).** As originally written: `[n]` and the
    timestamp were guesses unless the agent re-read the file — on 2026-08-10 `option-lab-2` carried
    two `[80]` and two `[81]` entries, and a supervisor stamped `2026-08-11 01:34` on an entry
    written at `15:20` the day before. The date field drives "time on task", and a future stamp used
    to render as "on task under a minute" indefinitely — through a SECOND copy of the duration
    wording in `SessionRows_Builder` that lacked the negative guard `SessionDuration_Formatter`
    always had. One implementation now (`Describe_SinceStamp_OrNull`, which returns null for a future
    stamp rather than a confident wrong number). **Never add a second copy of a formatter** — that
    half is permanent and is why this entry stays.
    **What changed:** `channel-append.sh:657-665` now computes `NEXT_INDEX` (max of the live file)
    and `STAMP` **inside the lock**, and the grammar states it (`kit/grammar/channel-grammar.json:16`).
    Agent-authored fields are now only **author** (validated against `AIORCH_ROLE`), **subject** and
    **body**. So for cooperating writers going through the tool, the failure class is closed.
    **What was the live cause until 2026-09-15, and is now CLOSED:** entry parsing was
    **fence-blind** (`audit-architecture.md:459-472`) — an entry quoted inside a fenced block was
    split into phantom entries with duplicate indices, corrupting the mirror, the index screen,
    member state and the next-index computation. That, not the agent's typing, is the likelier true
    cause of the `option-lab-2` duplicate `[80]`. It was also reproduced from the owner's Telegram
    export as something worse than a duplicate index: the supervisor's message arrived CUT, its tail
    delivered separately and signed by whoever was quoted — and deleted outright when the quoted
    author word was `owner` or `app`. `ChannelFence_Screen` now answers "is this line quoted code"
    for the parser, the tailer and `ChannelShape_Validator` alike, and **only a CLOSED fence
    suppresses anything**, so a stray ``` cannot swallow a channel. The two readers that used to
    disagree are one: the tailer asks the parser where an append is cut.
    **Also closed the same day:** an unusable `[n]` — oversized, or `[0]` — threw out of `int.Parse`
    or the entry factory after the offset had advanced and before `Pending` cleared, so it recurred
    every 2 s for ever and nothing could even append the error report. Such a header now opens no
    entry and is REPORTED through `ChannelShape_Validator.Find_MalformedHeaders`, owner-channel alert
    included. **Still distrust `[n]`** — the tool allocates it under the lock, but nothing stops a
    session appending by hand, and a duplicate index is a fault the index screen reports rather than
    a thing that cannot happen.
13. **Never compare a stored entry COUNT against a later live-file count.** `Channel_Compactor`
    moves older entries into a sibling `.archive.md`, so a live-file count is not monotonic. This
    silently broke "has the supervisor answered the owner yet": `option-lab-2` compacted 2 minutes
    after a delivery, 18 supervisor entries left the live file, and the pending could never clear —
    the owner was told their message was still waiting long after it was answered, and the
    supervisor was nudged for a failure that never happened. Count through
    `ChannelHistory_Counter.Count_Entries_ByAuthor`, which spans live + archive.
14. **Owner-facing repeats EDIT, they never stack.** The busy-supervisor narration used to send a
    new Telegram message every 3 minutes for as long as a turn ran; it now edits one line that
    counts up, like the turn-ended receipt always has. A repeat that is a notification is a
    waterfall, which is the thing this system exists to prevent.
15. **Alerts the owner cannot act on do not go to Telegram** (owner directive 2026-08-10). The
    PLAN.md ledger-shape complaint ("lines that lump several tasks together") now goes only to the
    supervisor's channel and the log — splitting a task line is the supervisor's job, so texting
    the owner about it was pure noise. Apply the same test to any new alert.
16. **Parallel agents: implementers (and solo) fan out, supervisors do not, writers need disjoint
    files.** An implementer's turn is MEANT to block — it is the one doing the work — so the
    supervisor's "never use a sub-agent" rule covers the SUPERVISOR's turn only (that turn is the
    owner's phone line) and is never relayed to a member. Solo gets the same fan-out contract, by
    reference, since it does the work itself with nobody else to hand parallelism to. Read-only
    fan-out is an implementer's default; parallel WRITERS are allowed only on disjoint file sets, with
    git and ambient files (`.csproj`, DI registrations, shared constants) kept to the implementer
    itself — PLAN.md is the supervisor's, never an implementer's to touch. The supervisor PROPOSES
    the split in the brief (`PARALLEL UNITS`) and the implementer verifies it — a supervisor briefs
    lean and has not read the code. A sub-agent's report is NOT evidence: the implementer reads the
    diff and runs the suite before reporting. A new implementer SESSION is for a separately reviewable
    deliverable (own review cycle, worktree, or ledger line); one deliverable going faster is fan-out,
    and ledger lines never shatter into units. Spec:
    `docs/superpowers/specs/2026-08-11-implementer-parallel-fanout-design.md`.
17. **The APP VERIFIES the kit plugin; it never copies commands.** `KitAssets_Bootstrapper`
    (`AIOrchestratorCoreLib/Kit/KitAssets_Bootstrapper.cs`) runs in BOTH hosts at startup — the WPF
    app and the daemon (`AIOrchestrator/App.xaml.cs`, `AIOrchestrator.Daemon/BridgeHost_Service.cs`):
    it unwires legacy hooks, moves aside legacy `~/.claude/commands` files, installs only the
    statusline, and records a verdict (`PluginVerdicts`) on the `IPluginGate`
    (`AIOrchestratorCoreLib/Kit/PluginGate/`) — SPAWNING is refused on a mismatch, the bridge keeps
    running regardless. Delivery is `kit/install.ps1` / `kit/install.sh`: they register this checkout
    as the `aiorch-local` marketplace and install `aiorch@aiorch-local` at user scope, reinstalling on
    a content mismatch (`kit-and-scripts` rule: "it never copies commands into `~/.claude/commands` —
    a local command beats a plugin skill for the slash word"). A running app built from a stale
    checkout still only verifies against ITS OWN build's commit — decision 23 still applies: the
    binary that is actually running is what matters, not what `git log` says is merged. **The
    equivalent caution for this model: a kit edit is not verified by editing `kit/` and re-running
    the installer** — verify it against a RESTARTED session (terminal or bridge), because
    `KitAssets_Bootstrapper` runs the verdict at STARTUP, and neither the marketplace registration nor
    a `git diff` on `kit/` tells you what verdict the currently-running process already recorded.
18. **SAY WHICH COPY YOU READ — installed, built, or branch source.** Every file in this system
    exists three times: the branch source (`kit/…`), the app's build output, and the installed copy
    (`~/.claude/commands`, `~/.claude/hooks`). They drift, and a finding about one is not a finding
    about another. On 2026-08-11 this happened FIVE times in one evening, twice to the supervisor:
    "the hooks contain no python3" (true of the installed copy, false of the branch), "the hooks were
    not edited tonight" (installed mtime, branch had six commits), a `diff` between build output and
    main tree that read identical and meant nothing. **State the copy in every report** — it is the
    single cheapest habit here and the one that would have saved that evening.
19. **`python3` on this machine is native WINDOWS Python, not the msys one.** It cannot see msys
    paths: `open('/tmp/x.sh')` throws `FileNotFoundError` on a file `cp` has just created and `bash`
    can read. Anything handing a path from bash to python3 must hand it a Windows path. The failure
    is silent in a heredoc — a script that "ran" wrote nothing, and the two green runs that followed
    were the unmodified original.
20. **A harness that cannot find what it tests must REFUSE TO RUN.** `hook-behaviour-check.sh`
    resolves the hooks relative to its own location, so a copy run from elsewhere finds none of them,
    every invocation returns nothing, and nothing-is-ALLOW: it reported 16 confident failures about
    code it never executed. Same class as the fixture guard — a harness must fail loudly rather than
    certify the absence of the thing it is testing. **And never assert on a state with two routes to
    it:** a case that allows for either of two reasons pins neither, which is how a guard stayed
    green with its check deleted.
21. **Hooks ADVISE, the app ENFORCES at the point of effect.** Every session runs as the same OS user
    as the app, so no filesystem location is beyond its reach — a session can delete a flag, edit the
    hook script, or unwire it from `settings.json`. A guard there restrains an honest session and
    stops nobody else, so the enforcement that must actually hold belongs in the app, where a session
    can only ask. **Corollary: a hook that cannot evaluate its predicate SAYS SO and ALLOWS.** It must
    never invent a denial it cannot justify, and never grant silent consent either — the line goes to
    `orchestrator.log.jsonl` (the app tails it; not Telegram, see 15; not stderr, which nothing
    reads), one appended line naming WHICH predicate failed and why. "Could not extract a tool name
    from the payload" is actionable; "hook error" is the silence again.
22. **THE ENDEAVOUR IS WHAT THE OWNER ASKED FOR — everything else is PARKED** (owner directive
    2026-08-14, after `ai-orchestrator-4`). Every session that reads code finds problems in it, and
    those discoveries were becoming work: the horizon exploded, orchestrations *"took an eternity to
    reach objectives and also forgot to carry out tasks that were explicitly requested"*. That
    session's PLAN.md ended with 40+ heading sections of findings and rulings, and three branches
    were left unmerged with twenty worktrees open. **A ledger line must trace to an OWNER REQUEST
    row**; anything else goes as one line into PLAN.md's `## PARKED` section — written down so it is
    not lost, outside the denominator so it cannot move the owner's bar. Two admissions only: it
    BLOCKS a requested line (then it is part of that line, not a new one), or it is live damage,
    which goes to the OWNER as a question. **Cheapness is never the argument** — the cost of a
    discovery is not its fix, it is the horizon it opens. Per decision 21 the app enforces the half
    it can: `PlanLedger_Parser` skips the sections named by `PlanLedger_Sections`, so a parked item
    cannot inflate the bar even written with a `- [ ]` marker. Reviewers separate findings from
    `OUT OF SCOPE` by PROVENANCE, not severity; implementers report `NOTICED (not fixed)`.

23. **THE RUNNING APP IS A FOURTH COPY — check `Get-Process AIOrchestrator | Select Path` BEFORE
    believing anything you built is live.** Decision 18 names three copies (branch source, build
    output, installed). There is a fourth and it is the only one whose behaviour the owner is
    describing when they report a bug: on 2026-08-21 the app was running from
    `AIOrchestrator\bin\Debug - Copia\net10.0-windows\AIOrchestrator.exe` — a manual copy, so the
    owner can rebuild while it runs — while every `dotnet build` that session wrote to `bin\Debug\`.
    Three fixes were merged, pushed, and reported as delivered; none of them were running, and the
    live DLL still contained `Rename_TelegramTopic_FireAndForget`, which the merge had deleted. A
    green build and a clean `git log` say NOTHING about what the owner is using.
    **This also feeds the kit:** `KitAssets_Installer` installs `~/.claude/commands` from the
    RUNNING app's output folder, so a stale copy keeps reinstalling stale role commands at every
    startup — which is why the installed `supervisor.md` can stay old through any number of correct
    builds. **To read a running binary** (metadata names are UTF-8, string literals are UTF-16LE —
    an ASCII `grep` returns confident false negatives): search the DLL for
    `"Some_Method_Name".encode('utf-8')` and `"a literal".encode('utf-16-le')`.
    **`KitAssets_Bootstrapper` replaces `KitAssets_Installer` in that paragraph** (decision 17,
    rewritten for the 2026-09-11 fork merge) — the fourth-copy trap is unchanged: it is still the
    RUNNING binary's bootstrapper that decides what gets installed and verified, not the source tree.

24. **`/model` and `/effort` from the phone (owner request 2026-09-09) — never a guess, one apply path,
    stateless buttons.** Bare `/model` or `/effort` answers with buttons; a typed value resolves through
    `ModelChoices` / `EffortLevels` or gets the buttons instead (the owner typed "fabel" in the message
    that asked for the command). A button carries a STATELESS payload — `model:<orch>:<sup|imp>:<value>`
    (`ModelEffortButton_Data`) — and is handled by the app BEFORE the generic `opt-` path, because an
    `opt-` tap becomes a synthetic owner message and lands in an agent's channel. `Apply_Dial` is the ONE
    apply path (store the override → kill → respawn → owner-facing app entry) and the agents' `set-model`
    request goes through it too. The effort override lives per role in session.json
    (`supervisorEffortOverride` / `implementerEffortOverride`; a solo sits on the implementer slot, as it
    does for the model) and reaches `claude --effort` ONLY when set — null means no flag, the CLI's own
    default. Merged with `feat/fable-51-default-xhigh` the same evening: its xhigh for supervisor and solo is
    the ROLE DEFAULT, applied only when no override is set.
    The pulse and the prompt's "now:" line show what each session ACTUALLY reports (`model.display_name`
    + `effort.level` from its `.usage.json`, one reader: `SessionModelReading_Factory`), never the
    override — a session respawned before an override landed still runs the old one. The reply keyboard
    is no longer `is_persistent`: that flag re-shows the bar whenever the phone keyboard hides (which is
    what the back button does) and disables the icon that collapses it.

25. **THE OWNER'S ANSWER CREDIT: raised at DELIVERY, one-shot, and never spent on a status line**
    (owner reports 2026-09-10 — `da-vinci-fintech-suite-31` entries 137, 183, 202 and the `/merge`
    silence). **What this tree actually contains:** `_ownerAwaitingAnswer` is the credit, raised by
    `Raise_OwnerWait` at DELIVERY (`Flush_OwnerDeliveries_Async`, and `/merge`, which opens the same
    `Track_OwnerReply` tracker an owner message does — its completion report is the credited answer
    and its turn end is announced), and `Is_TurnEndDeclaration` reads the SUBJECT only (bodies end
    with `WAITING ON` lines by habit, so the body says nothing) so a turn-end declaration cannot
    CONSUME it. Three drops came from the credit: a `WAITING ON …` SUBJECT (the run-to-the-end hook's
    own marker) spent it seconds before the real answer; it was raised at buffering, so a line
    written before the owner's message even landed spent it; and the suppressed memo was one slot, so
    the status line written after the answer overwrote it and the turn-ended receipt delivered the
    wrong text. The busy notice is gated on `!pending.Answered`.
    **What is NOT here, and the diagnostic that goes with it.** The narration FILTER is gone — the
    fork abolished it on 2026-09-09 on the owner's ruling (*"if the supervisor writes to me, I must
    know it — that rings"*), and this merge kept that. So on this build `OwnerPush_Policy.Should_Push`
    pushes EVERY supervisor entry on the owner channel except an empty body and the owner's own words
    quoted back (`Is_OwnerRestatement`); its `ownerIsWaitingForAReply` parameter is **unread**, which
    means the credit is live in the engine and INERT at the push. There is no `_suppressedEntries`
    list anywhere in `AIOrchestratorCoreLib/` and no turn-end digest: `Build_TurnEndedText` says so
    itself, its "last words" half deleted with the filter. `OwnerAnswerSurvivesFailedSendTests` still
    pins that the answer survives a failed send, but its "narration after the answer is not pushed"
    oracle was RETIRED with the filter — the file's own comment records it — and it now pins the
    opposite: the channel is not wedged afterwards. **So the old recipe "entry #N present with no
    `mirror send failed` line means the push policy suppressed it" WOULD MISDIAGNOSE a live incident
    on this tree.** Read a missing entry this way instead: absent from the log entirely ⇒ never
    tailed; `[owner] entry #N FROM …` present with no `mirror send failed` ⇒ look at the QUESTION
    HOLD, not at the filter — `QuestionHold_Policy` parks an owner channel whose orchestration is
    awaiting an answer (a `held entry #N` line says so), the topic may be Silenced or Deferred, or
    the entry was an owner restatement. The filter, the suppression list and the turn-end digest are
    **plan 03's (`phone.push = filtered`) to restore as a per-user setting** — the protective half of
    the credit landed here precisely so it is correct the day they return.
    **And the compaction guard is asked INSIDE the channel gate**
    (`Channel_Compactor.Compact_IfNeeded(path, mayRewrite)`): the compactor queues behind a session's
    append, so a guard answered before that wait describes a file that has since grown — entry 137
    was kept by the rewrite and parked behind the re-anchored cursor, in the file and never on the
    phone. The step's old docstring called that window "microseconds"; it was the length of an append.
    **The held-append prefix memo dies with its append** (2026-09-12): `_deliveredEntriesOfHeldAppend`
    counts entries POSITIONALLY, so it is only valid while the cursor has not advanced — it is
    cleared on every non-`Held` exit of `Mirror_Append_Async`, in one place. Two early `Delivered`
    returns (nothing mirrorable, a silenced topic) used to leave it behind, and a topic silenced
    while a question was held then ate the front of the NEXT append, silently and with no log line.

26. **The fork merge of 2026-09-11 (plan 01).** A fork developed headless on a Linux VPS
    (`nathanthegrey/AIOrchestrator`, see `.claude/rules/git-and-boundaries.md` and
    `docs/MODIFICHE-DEL-FORK.md` for its own account) was merged into the owner's master on
    `integration/fork-merge`: **the fork's structure won every conflicted file**, and master's
    features and intent (decisions 8's resume rule, 17's kit delivery rule, the Telegram/pause/
    question-hold decisions above, etc.) were **re-ported by hand across a numbered task series**,
    ledgered in `docs/superpowers/plans/2026-09-11-fork-merge-01-report.md` — that ledger is the
    audit trail for which side's code is actually running where. Spec:
    `docs/superpowers/specs/2026-09-11-fork-merge-and-per-user-profiles-design.md` (this spec file
    lives on branch `feat/fork-merge-and-profiles-spec`, not in the merge worktree or on
    `integration/fork-merge` — read it from that branch, not here). Two names for the same repo
    persist for now: `.claude/rules/` still describes fork/upstream boundaries that a merged repo has
    already outgrown (see the git-and-boundaries rule) — that is a known staleness, not yet resolved.
    **One concrete contradiction it will trip over:** `git-and-boundaries.md` says "Do not modify
    `docs/investigations/`, `HANDOFF.md` or `CLAUDE.md` — they are Manu's; a fact for him goes in the
    report," yet this very decision (and 8, 11, 17, 23 above) were written directly into `CLAUDE.md`
    by task on the owner's own instruction. Do not read that rule as still binding on `CLAUDE.md`
    without asking — it needs the owner's explicit call on how session boundaries work post-merge.

## Resolved Decisions (2026-08-06, owner)

- **UI framework: WPF** ("keep it simple") — `net10.0-windows`. The suite's `LoggingLib` ships a WPF `ListBoxLoggerSimple` control, which the app uses as its live log panel.
- **Coding patterns: follow the suite's `CODING_PATTERNS.md`** (read `CODING_PATTERNS_QUICKREF.md` in the suite repo before writing C# here). `AIOrchestratorCoreLib` = strict (triples, factories, immutability); the WPF app project = UI-relaxed (mutable observable properties allowed), same as the suite's App-project rule. NOTE: this repo has no pre-write hook — compliance is on the author.
- **Suite code reuse — RETIRED for now (2026-08-06 live-fix):** v1 referenced the suite's `LoggingLib` for its `ListBoxLoggerSimple` log panel, but that control is hard-designed light (white root background, pastel per-tag rows baked into its template) and cannot be dark-themed from outside; the app now ships its own dark log view (`Views/LogRowView` + `ActivityLogListBox`). **The repo currently builds standalone — no suite checkout required.** Coding patterns still follow the suite's `CODING_PATTERNS.md`; if suite libs are reused later, reference the MAIN checkout at `..\..\manuelvene90\Da-Vinci-Fintech-Suite` (never a worktree).
- **Per-orchestration logging + live state view** (owner directive mid-design): every orchestration writes `orchestrator.log.jsonl`; the app shows a live log panel and per-member state chips (implementer working / awaiting review / writing window open / blocked on owner) derived from the channel files.
- **Dual interaction:** Telegram AND direct terminal typing are both first-class; the file protocol works with the app closed.
- **PAUSE — asleep, not closed and not finished (owner, 2026-09-09).** `session.Paused` is a flag
  beside the delivery mode, like `AwaitingTest`/`Done`, and it means the owner walked away from an
  orchestration without ending it. Two halves, and BOTH are needed for "dormant" to be true:
  **outbound** rides the existing funnel — `EffectiveMode_Resolver.Resolve` answers `Deferred` when
  paused, ahead of presence, so all fifteen send gates and the offset freeze inherit it and the
  backlog replays on unpause; **pushing** does not, because nothing a delivery mode says has ever
  governed what the app WRITES INTO A CHANNEL. Each waker is gated separately —
  `Append_SupervisorAttention_UnlessMeeting` (the choke point for supervisor traffic), plus the
  member-side sweeps it deliberately excludes (`Nudge_IdleImplementers_Async`, `Flag_IdleMembers`),
  `Check_LedgerHealth_Async`, `Push_PeriodicStatus_Async`, `Resume_AllSessions_Async`, the
  `SessionWatchdog` respawn, and `Break_SilentDeadlock_Async` (which must not CONSUME the suppressed
  entry even though its send is already suppressed). Miss one and dormancy is a word.
  **`Break_SilentDeadlock_Async` DOES NOT EXIST IN THE CODE (verified 2026-09-15).** A repo-wide grep
  finds only two prose references plus `AwaySuppressesAppAlertsScanTests.cs:54`, a test that asserts
  its ABSENCE; `docs/superpowers/plans/2026-09-12-behavioural-seams-03.md:838` already logged this.
  It is named here, and in the audit dossier, as if it were a live waker — it is not, so there is no
  deadlock breaker in this system. Treat the clause above as a requirement for the day it is built,
  not as a description of what runs. Do not cite it again as existing machinery.
  `Status/PausedFlag_Marker` writes `.paused`, DERIVED-never-authored like `.meeting` and reconciled
  every tick, because the turn-end hook is bash and cannot read session.json — without it a paused
  session with open ledger lines is refused its turn end and keeps working. It SURVIVES a respawn
  (the inverse of `.awaiting-answer`): that flag describes what a process was doing, this one what
  the owner decided. Writing in the topic lifts the pause (`Wake_PausedTopic_IfNeeded`), and a
  second `/pause` inside 60 s re-asserts rather than toggling — the `/done` evidence, where every
  toggle in this machine's history was undone by a repeat press 17-23 s later.
- **ONE QUESTION AT A TIME IS ENFORCED BY THE APP NOW, not by prose (owner, 2026-09-09).** The rule
  was written down twice and held by neither: `supervisor.md` called it a HARD RULE and claimed *"the
  app enforces this by STOPPING YOU"*, while the thing stopping anyone was a PreToolUse hook that
  covered the **supervisor role only** — so the SOLO session that put nine unanswered questions on
  the owner's phone in five minutes was never covered at all — and which says of itself that it is
  advisory. Meanwhile the app computed "a question is outstanding here" every 2 s and spent the
  answer on a topic-name glyph: `AwaitingAnswerFlag_Marker.Is_Raised` had **no production callers**.
  Now `QuestionHold_Policy.Should_Hold` reads it in the mirror loop: an owner channel whose
  orchestration is awaiting an answer is HELD — skipped without `Settle_MirrorAttempt`, so the cursor
  does not advance and the entry is re-emitted next poll. **Never the failure-retry path**, which
  gives up after `MIRROR_RETRY_WINDOW_MINUTES` and DROPS with an error. Once a channel is held in a
  tick the rest of its appends are held too, because the cursor is per FILE — confirming a later
  entry would confirm the held question with it, losing the very thing being protected. It releases
  when the owner writes (any inbound message clears the flag) or at `QUESTION_HOLD_CAP_MINUTES`, and
  it never applies in terminal mode, where no flag is raised. Member channels are never held: that
  would stop the WORK, which is the fair objection the engine's own comment raised against gating on
  a pending question.
- **The app has NEVER pinned a Telegram message.** Do not go looking for a pin to remove: the only
  pin-family call in the repo is `unpinAllForumTopicMessages`. What the owner sees is Telegram's own
  auto-pin of the `forum_topic_created` service message, and the unpin used to run ONCE, inside topic
  creation, fire-and-forget — so any topic created before that code existed, or whose call lost a
  race, stayed pinned for ever. `Sweep_TopicCreationPins_FireAndForget` now re-runs it over every
  open topic at startup. **General is deliberately never swept**: the owner pinned their own channel
  message there, and `unpinAllChatMessages` / `unpinAllGeneralForumTopicMessages` would wipe it.
- **Model ladder + effort (owner, 2026-09-09; shipped default corrected 2026-09-12):** supervisor,
  implementer, reviewer and solo (reviewer and solo resolve off `ImplementerModel` when they carry
  no value of their own) default to Opus. **This superseded the original ruling that pinned them to
  the full id `claude-fable-5-1`**: that pin lived on as `kit/presets/classic.json`'s restatement of
  all four roles even after the settings catalogue registered Opus as the shipped default, which
  made the registered default unreachable in practice on any machine naming no preset — the common
  case, since `preset` absent means classic. Ruled out 2026-09-12 (task-6 fix round 1): the four
  `models.*` rows were removed from `classic`, so a machine that states nothing now actually spawns
  the catalogue's Opus. `claude-fable-5-1` remains reachable — name it explicitly in config.json (or
  a hand-edited preset file) — it is simply no longer what an untouched machine gets. **The shipped
  default now lives in the settings catalogue**
  (`AIOrchestratorCoreLib/Configuration/SettingsCatalog/SettingsCatalog.cs`'s `Build_Models`), not as
  a literal in `OrchestratorConfig_Factory`: that file's six `DEFAULT_*_MODEL` fields are `static
  readonly`, read FROM the catalogue at class-load time, kept only because six call sites and four
  tests still read them by name and because the reviewer/solo compat ladder (absent falls to the
  implementer's value before any default) lives there. General supervisor and communicator stay on
  `sonnet` (routing/narration = cheap), and neither preset ever states a value for them. **Supervisor and solo sessions spawn with
  `--effort xhigh` UNLESS the orchestration carries an effort override** (`/effort`, decision 24),
  which wins; no other role carries a default. Every flag is emitted by the single chokepoint
  `Build_ClaudeInvocation`. **EFFORT IS DATA TOO AS OF 2026-09-12 (plan 02 task 7, `015b6dd`), and the
  sentence that used to end this bullet — "the model is DATA … the effort is CODE (in the app binary
  — it needs a rebuilt app running)" — is no longer true.** The role default lived in
  `SpawnCommand_Builder.SUPERVISION_EFFORT_LEVEL`, a compiled constant, so the owner could change the
  model from their phone and could not change the effort at all without a rebuild — the same dial in
  two different places. That constant and `Resolve_Effort_OrDefault` are DELETED; the default is now a
  catalogue row (`effort.supervisor`, `effort.solo`) resolved catalogue → preset → config.json by
  `EffortSettings_Json.Parse` on the same preset rung the models use, and read through
  `IOrchestratorConfig.Get_EffortForRole_OrNull`. The RESOLUTION happens at the launcher, which is the
  one place holding both the per-orchestration override and the config provider. `EffortSettings_Json`
  has deliberately NO `Write`: a block written back becomes a stated value, which is exactly how the
  owner's live config.json came to pin a stale model and defeat the Opus default they had asked for
  (found 2026-09-12, entry 216). Both dials are now data, read live, and neither needs a rebuilt app.

## Design Spec

**`docs/superpowers/specs/2026-08-06-ai-orchestrator-design.md` is the approved design** — read it before changing architecture. This file stays the quick context; the spec is the authority.

**`docs/superpowers/specs/2026-09-11-fork-merge-and-per-user-profiles-design.md`** covers the
2026-09-11 fork merge (decision 26) and per-user profiles. It lives on branch
`feat/fork-merge-and-profiles-spec`, not on `integration/fork-merge` or in this worktree — check out
that branch (or `git show` the path from it) to read it.

## Repository Structure

```
AIOrchestrator.slnx           ← solution (repo root = solution level)
AIOrchestrator/                ← the WPF desktop app host
AIOrchestrator.Daemon/          ← the headless bridge host (systemd/launchd/Windows service)
AIOrchestratorCoreLib/          ← the shared engine: Bridge, Channels, Running, Kit, Launching, Composition, …
AIOrchestratorCoreLib.Tests/    ← xUnit test suite (see .claude/rules/code-conventions.md)
tools/
  claude-contract/              ← contract tests against the real/fake `claude` CLI (FakeClaude harness)
  token-gate/
  ledger-sampler/
kit/                            ← the `aiorch` Claude Code plugin + local marketplace (decision 17)
  .claude-plugin/                ← marketplace.json, plugin.json
  skills/                        ← one skill per role (supervisor, implementer, reviewer, solo, communicator, general-supervisor, subagents)
  bin/                           ← channel-append.sh and other plugin executables
  hooks/
  grammar/
  statusline/
deploy/                        ← systemd unit, launchd plist, Windows service script, ledger-sampler deploy
docs/
  MODIFICHE-DEL-FORK.md          ← the fork author's own narrative (Italian)
  investigations/
  superpowers/                    ← specs, plans, sdd task briefs/reports (this task's brief lives here)
CLAUDE.md                      ← this file
HANDOFF.md                     ← in-progress work carried across sessions, when present
README-daemon.md               ← daemon-specific run/deploy notes
```

## Conventions

- The repo root is `C:\Users\Gianpiero\source\repos\AIOrchestrator` (solution level), not the inner project folder.
- Machine-local secrets (bot token, chat ids) live in gitignored `*.local.json` / `secrets.json` — never committed, never hard-coded.
- Multi-line git commits via `git commit -F <tempfile>` (Windows PowerShell mangles `-m` with here-strings).
