# Agentopolis pilot — plan 02: the engine (M1, Nathan's track)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended)
> or superpowers:executing-plans to implement this plan task-by-task. This plan follows the agentopolis
> repository's own plan style — goal, files, "done when" per point, no code written in advance —
> because its `CLAUDE.md` requires it ("Piani snelli"). Full rigour only on points marked **risk**.

**Goal:** the Agentopolis kernel runs at a customer on their Anthropic API key, for several people with
a desk each, without Slack, serves the Runner API the desktop app talks to, runs the performance
playbook with a number measured by the house, and installs on a fresh Debian 13 VM with one script.

**Architecture:** every change is made where the mapping found the seam: credentials at the turn's
hand-off (`relay.ts` / `runner.ts`), people at the owner checks and the register, Slack behind the
surface-neutral rows it already writes (`renders`, `ask_cards`, `messages`, `HomeData`,
`DaemonActions`), the API as a new `node:http` server beside `/diag`, the playbook inside the existing
oracle and house checks. No new library.

**Tech Stack:** Node 24.20.0, pnpm 11.10.0, TypeScript, vitest, Drizzle + SQLite (WAL), bubblewrap,
nftables, systemd (all existing).

**Spec:** `docs/superpowers/specs/2026-09-29-agentopolis-product-design.md` (D1, D2, D5-D7, D12, §4.4,
§6); plan 01 (the contract `api/v1/openapi.yaml` and decision records 0035-0037); overview P1-P5.

**Branch:** `m1/pilot-engine` from `master` after plan 01 is merged. Four rounds: (1), (2), (3-4),
(5-6). One review at the end, on the points marked **risk**. The owner's test once, at the end, on a
Debian 13 VM that is not Nathan's VPS.

## Global Constraints

- Every rule in the agentopolis `CLAUDE.md` holds except where records 0035-0037 amend it: nothing
  dynamic in the system prompt; model and effort fixed at spawn; the envelope is the message; only the
  addressee is woken; pending work is a query; every uniqueness rule is a database constraint; a turn
  proposes, never commits an effect; no model takes part in an authorization decision; the mandate is
  an intersection.
- Migrations are expand-only (decision 0032), numbered after the latest in `drizzle/` (0072 at the
  time of mapping — check before writing).
- Secrets reach a turn by file descriptor or a 0400 per-turn file, never through argv, the settings
  JSON (`turns/*.json` is readable by others) or an inherited environment variable (decision 0034).
- Strings the people read go in `src/slack/strings.ts` (or its surface-neutral successor), in English,
  marked `DRAFT — the owner rewrites this` (D10: drafted by AI, reviewed by the owner and Nathan).
- The suite runs once per round; a red is read, not re-run until green.
- No real Slack, VPS, Anthropic account or network in the tests: fake-claude, FakeChat, `--fake`.

## Review Focus

1. **An API key leaking into a turn's environment or a log line** — `/proc/<pid>/environ` inside the
   cage, the house masker, the turn JSON. Point 1 carries a test that reads the child's environment
   under fake-claude and asserts the key is absent, and one that masks it in an outbound message.
2. **A card pressed by someone who may not press it** — a Viewer approving `merge_production`, a member
   of another organization's token, a token revoked mid-session. Point 2 tests each and asserts no
   effect row moves.
3. **A room with no Slack channel losing its question or reply** — today an ask card is not created
   (`asks.ts:192`) and a reply waits for a channel (`outbox-pump.ts:295`). Point 3 tests a question
   and a reply in a Slack-less room end to end through the API.
4. **A benchmark that times noise, not code** — the check retries a red once (best of two), turns run
   beside it, the dataset is copied into RAM. Point 5 measures baseline and candidate in the same
   `oneAtATime` slot, never retries a timing, and records the spread.
5. **Two members writing to the same thread at once** — "a message during a turn is queued" (0023)
   assumed one voice. Point 2 pins the order: both queued, in arrival order, each attributed.

---

## 1 · Credential modes: an Anthropic API key for customers — **risk**

**Goal:** a customer Runner runs every turn on its organization's Anthropic API key; our own Runner
keeps the subscription mode, and only with the licence capability `subscription`.

**Files:**
- `src/config/schemas.ts` (new top-level `credentials: { mode: subscription|api_key }`, default
  `subscription` for backward compatibility of Nathan's home; `ConfigFile` near :639).
- `src/daemon.ts` (a pure `credentialModeRefusal(mode, licence)` beside `tokenInCageRefusal` :3208,
  called at boot after `AccountCredentials.take` :392; the licence is a file
  `/etc/agentopolis/licence.json` signed with our Ed25519 key, public key compiled in; `subscription`
  refused without the capability).
- `src/governance/accounts.ts` (`AccountCredentials.take` :92 also takes `AGENTOPOLIS_API_KEY_<HEX>`;
  new account kind `api_key` — migration adding the value to `ACCOUNT_KINDS` usage, `schema.ts:1808`).
- `src/engine/relay.ts` (`tokenHandOff` :485 generalised to a `CredentialHandOff` keyed by mode: the
  key is written to a 0400 file readable only by the turn user and handed to the CLI through an
  `apiKeyHelper` setting that `cat`s it — check first whether the pinned CLI accepts the key on a file
  descriptor like the OAuth token, and prefer that if it does).
- `src/engine/runner.ts` (:439-460 env step by mode; :744-757 `onPaidKey` stops the turn only in
  subscription mode), `src/engine/cli-contract.ts` (:12 `SUBSCRIPTION_KEY_SOURCES` becomes the
  expected source per mode: `none` for subscription, `apiKeyHelper` or `ANTHROPIC_API_KEY` for
  api_key; `initBreaks` :30-45 by mode).
- `src/governance/guards.ts` (plan windows, `isPaying`, `onPaying` :394-463 only in subscription mode;
  `onApiRetry` :746 stays for 429s in both).
- `src/engine/launcher.ts` (:95 `TURN_ENV_NAMES`), `deploy/sudoers.d/agentopolis` (env_keep),
  `deploy/agentopolis-account` (an `add-key` verb that writes the key and its gateway digest).
- `src/turn/spec.ts` (:680 the research turn must go through the gateway in api_key mode, or is
  refused — it bypasses the digest check today).
- Cost: `src/scheduler/scheduler.ts` (:353-375 `ownCost`) reads the register's per-request rows as the
  truth in api_key mode (the result's `total_cost_usd` is null after a forced stop, `runner.ts:955-960`);
  `costLabel` = `billed` in api_key mode, `estimated` otherwise (`daemon-actions.ts:1058`, `blocks.ts:419`,
  `mcp/read.ts:369`).
- Tests: update `tests/governance/accounts.test.ts`, `tests/engine/runner.test.ts`,
  `tests/engine/relay.test.ts`, `tests/security/ladder.test.ts`, `tests/engine/launcher.test.ts`,
  `tests/governance/limits-per-account.test.ts`, `tests/routines/cli-gate.test.ts`,
  `tests/turn/network-env.test.ts` to be mode-aware; add `tests/engine/api-key-mode.test.ts`.

**Done when:** under fake-claude with `apiKeySource: "apiKeyHelper"`, an api_key Runner runs a turn to
the end and a subscription Runner stops the same turn as paid (both asserted); the child's environment
and argv contain no key and the key file is 0400 owned by the turn user (asserted); a Runner configured
`subscription` without a licence refuses to boot with a sentence saying why, and with a licence whose
signature is altered by one byte refuses too (mutation proof); an outbound message containing the key
is masked; the gateway passes a request carrying the key whose digest is in `gateway.env` and refuses
one whose digest is not; a forced stop in api_key mode records the register's cost, not null; the task
card says "billed".

## 2 · Several members, a desk each, approvals by policy — **risk**

**Goal:** four people use one Runner; each has a desk colleague of their own with their own memory;
anyone the policy allows may press a card, and every decision names the person who pressed it; a
card goes to whoever started its thread, else to the project's maintainers.

**Shape (decided here, smallest change that holds):**
- Members live **in the register**, not in `config.yaml`, because `member add` (point 6) creates them
  and their API tokens: new table `members { id, name, role: owner|admin|maintainer|member|viewer,
  slack_user_id?, token_sha256, ssh_key_fingerprint?, created_at, revoked_at? }`, unique on `name`
  and on `slack_user_id` where not null. `slack.owner_user_id` stays as a legacy alias that seeds one
  `owner` member at boot, so Nathan's home keeps working.
- The addressee value `"owner"` (`store/messages.ts:13`, 54 references, ~60 literals) is **kept**
  and means "a human"; a new nullable `person` (member id) goes next to it. Renaming would touch more
  than a hundred places for nothing.
- Integrity `"owner"` (`messages.ts:72`) and goals (`intents.ts:57`) are given only to Member and
  above: a Viewer's words must not count as an owner's.
- The expand-only lint (`tools/ci/expand-only.sh`) refuses any `DROP`, including `DROP INDEX`, so the
  three house-wide "one only" rules — `return_cards_one_open` (`schema.ts:1718`), the morning-line
  unique index (`schema.ts:~883`) and the `settings_key` CHECK (`schema.ts:2322`) — are not relaxed:
  per-person morning lines, return cards and home pages get **new tables**, and the per-project
  approval policy goes in a new `project_policies` table.

**Files:**
- Migrations 0073+ (all nullable adds or new tables, snapshots in `drizzle/meta/`): `members`;
  `messages.person`; `memories.person`; `containers.started_by`; `requests.policy`;
  `person_morning_lines`; `person_return_cards`; `person_home_pages`; `project_policies`.
- **Who may speak:** `src/slack/inbox.ts` (:173,187,197 `classifyEvent` accepts any member mapped by
  `slack_user_id`, drops a Viewer's message, carries `person`), `src/slack/catch-up.ts:112`,
  `src/slack/app.ts:35`; the API's messages (point 4) carry the token's member.
- **Who may press:** a pure `mayPress(member, action)` (Admin and above for switches, PIN, accounts,
  brake release; Member and above for everything else) replacing every `=== owner_user_id` press check
  — `commands.ts:160,176,379,402,437`, `owner-buttons.ts:157`, `daemon.ts:1628,1635,1640,1647,1694,1736,
  1812,2075,2105`, `stuck.ts:55`, `sessions.ts:143`, `return-card.ts:289`, `account-doors.ts:96,132,138`,
  `grants.ts:66,87`, `egress-grants.ts:176`, `pin.ts:326,356,386,440,486,558`; and a pure
  `mayDecide(member, subject, project)` reading `project_policies` (default: any Member and above for
  `merge_production`, D6) at the top of `#decide` (`daemon-actions.ts:1571`, after the stale check),
  in `#ownerYes` (`tasks.ts:3193`, today `decidedBy === owner`) and in `pin.spend` /
  `pinRefusesPress`.
- **Who pressed, recorded:** the member id wherever `"owner"` is written as the decider today —
  `daemon-actions.ts:700,774,1146,1158,1175`, `asks.ts:343,350`, `return-card.ts:300,302`; already
  a user in `requests.decidedBy` (`approvals.ts:741`) and `effects.authorizedBy` (`effects.ts:440`,
  `mail.ts:262`, `release.ts:333`, `secret-run.ts:346`, `delivery.ts:561`).
- **A desk each (D7):** `AgentFile` (`schemas.ts:196`) gains optional `serves: <member name>`;
  `ceoFor(person)` replaces `ceoName` (`daemon.ts:3174`, 25 callers) and its copies `ceoAgent`
  (`bootstrap.ts:48`), `alerts.ts:385`, `daemon-actions.ts:1896`, `home.ts:703` (`jarvisMemory`),
  `daemon.ts:3381`; `ensureContainers` (`bootstrap.ts:159-166`) opens one desk room per member (a
  Slack DM only when the member has a Slack id); `ownerRoomIn` (`handover.ts:47`) and `containerFor`
  become member-aware; `scope.ts:64` gives every desk its person's scope; the morning line
  (`morning.ts:320,353`, `daemon.ts:2486`) is written per person into `person_morning_lines`;
  `member add` (point 6) creates the member's desk agent from the template role `ceo`.
- **Memory by person:** `memories.person` filled in `rememberInto` (`envelope.ts:1564`, `ownerSpoke`
  :1623) and `rowFor` (`memory/rows.ts:230`); a desk remembers only what its own person said.
- **Routing:** `threadRoom` (`router.ts:72`) records `started_by`; `routeOwnerMessage`
  (`router.ts:195`) records `person`; a `recipientsFor(card)` helper — session → container starter,
  else the project's `maintainers` (new optional `project.yaml` field next to `lead`, `schemas.ts:~383`)
  — used by the mention points (`outbox-pump.ts:298-330`, `stream.ts:281`) and by the API's inbox;
  `placeOf` (`router.ts:120`) prefers the asks in the presser's thread. Two members writing in one
  thread during a turn are both queued in arrival order (0023), each with its `person`.
- **Home and inbox per person:** `chosenPage(db, member)` (`home.ts:576`) from `person_home_pages`;
  `homePage(member)` (`daemon.ts:2437`), `daemon.home(member)` (`daemon-actions.ts:1733`, called at
  `commands.ts:293,320,344,413,447`); a `personScope` beside `ownerScope` (`scope.ts:40`); `waiting`
  (`view.ts:540`, the `"to"='owner'` filters at :203,238) narrowed to the person's cards.
- **The PIN** stays one per house in the pilot (`pin_secrets.setBy` single): noted as a known limit —
  a shared secret among approvers — and in the PR.
- Tests to update: `tests/slack/inbox.test.ts`, `app.test.ts`, `tests/config/schemas.test.ts`,
  `tests/daemon/catch-up.fake.test.ts`, `tests/slack/commands.test.ts`, `tests/redteam/redteam.test.ts`,
  `tests/actions/permission-switches.test.ts`, `pin-loosen.test.ts`, `undo.test.ts`,
  `tests/effects/pin-approve.test.ts`, `tests/governance/asks.test.ts`, `account-switch.test.ts`,
  `pin.test.ts`, `flags.test.ts`, `tests/gateway/egress-ask.test.ts`, `tests/slack/bootstrap.test.ts`,
  `tests/daemon/home.fake.test.ts`, `routines.fake.test.ts`, `undelivered.fake.test.ts`,
  `tests/alerts/alerts.test.ts`, `tests/memory/rows.test.ts`, `tests/daemon/memory.fake.test.ts`,
  `tests/daemon/daemon-routing.fake.test.ts`, `topics.fake.test.ts`, `task-thread-reply.fake.test.ts`,
  `tests/slack/outbox-pump.test.ts`, `sessions.test.ts`, `tests/slack/home.test.ts`,
  `tests/home-layout.test.ts`, `tests/view/view.test.ts`, `tests/daemon/return-card.fake.test.ts`,
  `tests/store/db.test.ts`, `query-plans.test.ts`; fixture members in
  `tests/fixtures/home-valid/config.yaml`. New: `tests/governance/members.test.ts`.

**Done when:** with four members (owner, admin, member, viewer) under `--fake`: each has a desk room
and a desk colleague whose memory holds only what that person said; a question opened in the member's
thread appears in the member's inbox and not the viewer's; the member approves a `merge_production`
card and the effect records the member's id; the viewer's press on the same card is refused with a
reason and no row moves (mutation proof: remove the `mayDecide` call and the test goes red); a revoked
member's press is refused; Nathan's existing home, with only `owner_user_id`, boots and behaves as
before (the existing suite green).

## 3 · Slack optional: every room works without a channel

**Goal:** a Runner with no `slack:` block boots, and questions, cards, replies and streaming reach the
people through the Runner API alone; with Slack configured, nothing changes.

**Files:** `src/config/schemas.ts` (:648-656 `slack` optional); `src/daemon.ts` (Slack app started only
when configured; `/fake/inbound` unchanged); `src/work/asks.ts` (:192 an ask card is created without a
channel); `src/slack/outbox-pump.ts` (:295 a reply to a room without a channel is delivered — to the
API's event stream — instead of waiting; Slack kinds are skipped, not queued forever, when Slack is
off); `src/slack/router.ts` (`placeOf` :120 accepts a room id as well as a Slack channel + ts);
`src/slack/receipt.ts` (the 👀 receipt is Slack-only: absent without Slack, and the API sends
`turn.started` instead); `src/scheduler/stream.ts` (`streamTargetFor` :252 no longer requires a Slack
thread); tests `tests/slack/*` untouched and green; new `tests/daemon/no-slack.fake.test.ts`.

**Done when:** `agentopolis --fake --no-slack` boots; through the API a member writes to their desk,
the colleague answers, a question card appears in `/v1/inbox`, pressing it wakes the colleague, and the
reply streams as `reply.delta` then `reply.end`; the existing Slack suite is green unchanged.

## 4 · The Runner API server

**Goal:** the contract of plan 01 is served on `127.0.0.1:4894`, every response validates against it,
and every action goes through the same function Slack's buttons call.

**Files:** create `src/api/server.ts` (node:http, bearer auth with `timingSafeEqual` against the
member tokens of point 2, JSON bodies capped at 64 KB), `src/api/routes.ts` (one handler per
endpoint), `src/api/events.ts` (SSE: polls the register's event cursor `changesSince` `view.ts:647` and
`messages.id`; tees the turn's `onPartial` events `scheduler.ts:1505` through a second `EnvelopeReader`
with the same `streamPiece` masking `exit-gate.ts:132`; `Last-Event-ID` resumes); `src/api/messages.ts`
(a member's message: `recordInbound("api:<clientId>")` + `deliverOnce` + a container-keyed variant of
`routeOwnerMessage` `router.ts:167` with `slackTs` null — never `ownerSays` `daemon.ts:2768`, which
invents a Slack ts); `src/config/schemas.ts` (:632-636 port 4894); `src/daemon.ts` (start/stop beside
`startHealth` :3441); `deploy/agentopolis.service` (no change to the wall: turns cannot reach 4894,
`network-wall.md:15-20`); tests `tests/api/*.test.ts` (one per endpoint, one for SSE resume, one for
auth), plus `tests/api/contract.test.ts` from plan 01 run against live responses.

Reads map to existing functions: inbox → `waiting` (`view.ts:540`, per realm, with the render id
resolved through `latestRenderFor` `cards.ts:585` / `ask_cards.render_id`); tasks → `taskLines`
`view.ts:483`, `taskDetail` `archive.ts:228`; rooms → a new `roomsFor(member)` query; messages →
`roomMessages` `archive.ts:158`; card → `renderPayload` `cards.ts:600` with the buttons taken from the
render's options, never from Slack blocks. Presses map to `DaemonActions` (`commands.ts:51`):
`approve` :1215 (with `pinWindow` :1231), `approveWithPin` :1309, `deny` :1554, `unblockTask` :1070,
`closeTaskFromCard` :1094, `stuckMove` :1110, and `asksDoor` (`return-card.ts:292`) for questions.

**Done when:** every endpoint answers per the contract under `--fake`; a request without a token or
with a revoked one gets 401 and changes nothing; a press with an old epoch returns `stale` and moves no
row; an SSE client that disconnects and reconnects with `Last-Event-ID` receives exactly the events it
missed; a message posted twice with the same `clientId` is recorded once.

## 5 · The performance playbook — measured by the house

**Goal:** a performance job goes profile → benchmark and equivalence tests written first and merged →
optimize against them frozen → the house measures baseline and candidate → the card shows
"before → after (×speedup)"; "green but not faster" counts as no progress on the ladder.

**Files:**
- `src/mcp/request-payloads.ts` (:58-207 `open_task` gains optional `measure: { command, target }` on
  `kind: develop`; no new kind — a new kind forces a new role, :32, `tasks.ts:259-267`).
- `src/config/schemas.ts` (:309-319 `project.yaml` gains `bench: { command, timeout_minutes }`, run
  only by the house, never in the developer's stop gate or command allowlist
  `governance/permissions.ts:166`, `stop-gate.ts:73`; `setup` beyond pnpm if Guido's stack needs it,
  :373 — decided after the conversation with Guido, overview item 1).
- `src/work/oracle.ts` (:123 `heldOutOf` never hides a benchmark file; :482 per-command timeout from
  `bench.timeout_minutes`), `src/daemon.ts` (:2979 pass `timeoutMs`).
- `src/work/check-runner.ts` (:605-719 `proveCommit` runs the bench command for baseline and candidate
  in the same `oneAtATime` slot, three runs each, parses a fixed line `AGENTOPOLIS-MEASURE
  metric=<name> value=<number> unit=<s|ms>` from the full output at :693 before it is cut to 15 lines,
  records median and spread; a timing is never retried as "flaky" :696-702).
- `src/store/schema.ts` (:118-132 `HouseCheck` JSON gains `measure`, no migration; a new
  `tasks.baseline` JSON column — migration).
- `src/work/tasks.ts` (:4177-4224 `cardFor` passes `measured`; :2909-2921 `#landed` and the
  `task.merged` event :2941 carry the numbers), `src/slack/blocks.ts` (:392-426 `taskCard`),
  the API's `TaskDetail.measure` (plan 01 contract).
- `src/governance/progress.ts` (:156-181 `reportFacts`: below target = no progress, an improvement =
  progress; "same results" red stays a red), `src/work/stuck.ts` (:63-113 "best" = best measurement).
- Role prose (drafted, marked DRAFT, reviewed by the owner and Nathan — D10):
  `examples/home/roles/lead/AGENT.md` (:22-33, a "performance job" paragraph: profile first, a test
  task writes the benchmark slice + equivalence tests, merge it, then a develop task names them in
  `oracle` and carries `measure`), `developer/AGENT.md` (:25-33, the number is the house's; dispute the
  benchmark, never edit it), `tester/AGENT.md` (writes both, prints the measure line).
- Golden: `tests/golden/lead/lead-21-perf-opens-test-first.json`, `lead-22-perf-develop-names-oracle.json`,
  `tests/golden/developer/dev-21-disputes-benchmark.json`.
- Tests: `tests/mcp/server.test.ts` (schema), `tests/work/oracle.test.ts` (benchmark not held out,
  fenced), `tests/work/check-runner.test.ts` (bench runs caged, three runs, no retry, measure parsed
  from beyond line 15), `tests/work/tasks.test.ts` (baseline at open), `tests/work/proven-merge.test.ts`
  (merge carries the numbers), `tests/governance/loops.test.ts` (two below-target reports → one step
  up), `tests/slack/blocks.test.ts` (card bytes).

**Done when:** in a fake project whose bench command prints the measure line, a develop task with
`measure.target: 2.0` shows the baseline at open, a report that is green at ×1.1 counts as no progress,
two such reports step the rung up once, a report at ×2.3 is reviewed and merged, and the card and the
merge line both read "before → after (×2.3)"; editing the benchmark file on the task branch turns the
check red.

## 6 · The customer installer — **risk**

**Goal:** on a fresh Debian 13 VM, one command installs a Runner for a named organization, with its
members, its API key and its repository, and one command removes it.

**Files:** `deploy/machine.sh` (parameterize the values that are Nathan's: the aiorchestrator refusal
:23-25,130-138 becomes a check for *any* second tenant on the same login, off by default for a
customer; memory ceilings from the VM's RAM; the source URL and commit as arguments),
`deploy/preflight.sh` (:133-135 same), create `deploy/install-customer.sh` (runs `machine.sh`, then the
steps `INSTALL.md` leaves to a person: env file, `agentopolis-account add-key`, gateway digest,
`ptrace_scope=1`, resolver check, CLI install/promote at the pinned version, deploy key printed for the
customer to add on GitHub, `enable --now`), create `deploy/uninstall.sh` (stops and disables units,
deletes the nft table, removes units, sudoers, helpers, `/opt/agentopolis*`, users; keeps
`/var/lib/agentopolis` and `/etc/agentopolis` unless `--purge`), `src/cli/init.ts` (:10-23 generates a
home from a clean template — one desk role, one lead per project, no Fincanva/Ianus/Nathan values —
instead of copying `examples/home`), create `deploy/templates/home/` (the clean template),
`src/cli/main.ts` (new verbs `member add <name> --role <role> --ssh-key <file>` that creates the
member, prints the API token once and adds the key to a restricted `agentopolis-tunnel` user allowed
only `-L 4894:127.0.0.1:4894` (`permitopen`), and `member remove`), `deploy/INSTALL-CUSTOMER.md`,
`.github/workflows/machine.yml` (a second job: `install-customer.sh` then `uninstall.sh` in the Debian
container, then install again).

**Done when:** in the CI Debian container, `install-customer.sh --org guido --api-key-file k --repo
git@github.com:…` ends with the daemon active and `/v1/me` answering for a member created by `member
add`; a second run changes nothing; `uninstall.sh` leaves no unit, nft table, sudoers file or user, and
a third install succeeds; the tunnel user cannot open a shell or forward any port but 4894 (asserted);
no value from Nathan's VPS appears in a generated home (grep in the test).
