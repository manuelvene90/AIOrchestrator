# Agentopolis — product design (v1 direction)

**Status:** DIRECTION AGREED with the owner on 2026-09-29 (decisions D1-D12 below), pending the owner's review of
this written text. Two decisions rest on assumptions still to confirm (D4's repository, D9's setup fee), and D3/D4
need Nathan's agreement, since they make his repository and his engine the base. Each milestone in §10 gets its own
spec → plan → build cycle; this document is the umbrella they refer to.
**Written:** 2026-09-29 by the solo session of `ai-orchestrator-30`, at the owner's request (*"study
the source material, and think of ways to make this a great product … transform a team of a few
developers into super productive agent managers"*).
**Sources read:** `Coding-Wand/agentopolis` at `f803c71` (Nathan's rewrite), this repo
(`AIOrchestrator`) at `6ce279a`, the DVFT suite (`manuelvene90/Da-Vinci-Fintech-Suite`, master), the
market as of September 2026, and Anthropic's published terms. Every claim about a codebase below was
read in that codebase; claims about the market cite sources in §12.

---

## Decisions log (brainstorm with the owner, 2026-09-29)

- **D1** (13:55) — two credential modes: subscription/terminal for our own organization and users,
  API keys the only mode customers see (§3.1).
- **D2** (14:27) — for customers, agents always run on the customer's own server or PCs; our cloud
  handles only accounts, licences and teams (§3 rule, points 1 and 3). A hosted option is not v1.
- **D3** (14:43) — the engine stays TypeScript (Agentopolis's kernel); the desktop app, web and
  cloud services are C# (from the DVFT suite); they meet at one versioned API (§5, decisions 1-2).
- **D4** (14:52) — one repository for both sides. *Assumed, to confirm:* it is `Coding-Wand/agentopolis`
  (keeping Nathan's history), with the TypeScript engine and the C# app side by side in their own
  top-level folders and the API contract shared between them.
- **D5** (14:56) — the desktop app is in Guido's pilot. This overturns §10's "never on the pilot's
  critical path": M1 now includes a minimal desktop cockpit (inbox, task board, threads, approvals)
  on the Runner API, and the pilot starts when it is ready. Slack stays available as a second surface.
- **D6** (15:08) — by default any team member may approve a merge to production; the approval
  policy (§6) lets an admin tighten it per project (maintainers only, or two people).
- **D7** (15:11) — every person gets their own assistant as their front door (§4.1 "each human's
  desk"); project leads are shared by the team.
- **D8** (15:33) — the product is **Agentopolis**, the company **Coding Wand**. Trademark and domain
  checks before any public launch.
- **D9** (15:39) — after the pilot, customers pay **per seat** (a person who manages agents).
  *Assumed, from the recommendation:* plus a setup fee; AI usage is paid by the customer to the
  provider directly, never resold by us.
- **D10** (15:43) — the default agents' instructions are drafted by AI from both systems' tested
  prose, reviewed by the owner and Nathan, and promoted only when their example cases pass.
- **D11** (16:28) — the owner reviews §7's behaviour rules and §8's disagreements one by one:
  - **D11.1 KEEP** — receipt in the agent's own words before work, and a "done" line when finished.
  - **D11.2 KEEP** — a question asked while an agent is busy is answered first, in its next message.
  - **D11.3-D11.18 KEEP ALL AS PROPOSED** (16:46: *"let's start keeping them all, I'll tweak later using the product"*) — the rest of §7 and every §8 row as the Proposal column says. Each stays a setting a team can change in the product.
- **D12** (16:52) — customer API keys in v1 are **Anthropic only** (direct, AWS Bedrock or Google Vertex, all
  supported by Claude Code as it is). Other vendors wait until after the pilot.

---

## 0. The proposal in one page

**What it is.** Agentopolis turns a small software team into a team of *agent managers*. Each
developer runs a crew of AI colleagues — a lead per product, developers, reviewers, testers, and any
role the team invents — that take a job described in plain words and bring back **small, verified,
reviewed changes**, asking the humans only for decisions that are really theirs.

**Why anyone would pay for it** when Claude Code is already on their desk:

1. **Verified delivery, not more output.** The industry's measured problem is no longer writing code;
   it is *reviewing* it — teams with high AI adoption merge 98% more PRs while review time rises 91%
   and PR size 154% (Faros, §12). Agentopolis's answer is structural: every change is re-checked by
   the house against held-out tests the agent could not edit (the *oracle*), then read cold by an
   independent reviewer, then merged through a queue. Humans review *outcomes*, not diffs.
2. **Persistent colleagues, not sessions.** A lead that knows the product, remembers what the team
   told it, and owns the backlog — instead of an empty terminal every morning.
3. **Governance a company can sign off.** "Autonomy over the means, control over the consequences":
   anything that leaves the house (a merge to production, a mail, a deploy, money) needs a named
   human's button; everything else runs freely inside a sandbox. No model ever takes part in an
   authorization decision.
4. **Custom agents in minutes.** A Role Studio where a team designs the colleague it actually needs
   (QA, ops, customer-support triage, data engineer), tested against example cases before it is
   allowed to work.
5. **Value you can see.** The product measures itself — cycle time, review time, cost per merged
   change, hours of agent work — and writes the monthly impact report the consultancy sells on.

**What we build it from.** Nathan's Agentopolis kernel (TypeScript) is the engine; this repo's
two months of hard-won human-factors rules and role prose are the behaviour; the DVFT suite's
accounts, licences, updater, Avalonia shell and Blazor web are the product chassis.

**The one decision that must come first** is legal, not technical (§3): a commercial product may not
run customers' work through consumer subscriptions or hold their Claude credentials. The design below
is built around that: **the runtime lives with the customer, on the customer's own Claude seats or API
key; our cloud never touches a Claude credential.**

**The order.** Pilot with Guido's team first, with the kernel we already have and a minimal desktop
app built for it (§10, M1; D5). Accounts, licences, updater and web follow once the pilot has started.

---

## 1. Who it is for, and what success looks like

**First customer:** Guido's startup — four developers, still at "chatbot level" with AI, a data
routine that takes five hours and must drop for the service to be sellable. Free for a month,
result-based afterwards; a status update due in about two weeks.

**The target segment:** small product teams (2–20 developers) with no platform team, where every
developer is also designer, researcher and reviewer — the shape the owner and Nathan identified
(*"a reality more like ours than lastminute's"*). Large enterprises with fragmented roles are served
well enough by Anthropic's own tools; small teams need the *organization* that the tools lack.

**Success, stated so it can be measured:**
- For the customer: the five-hour routine measurably faster (baseline and method agreed in writing
  before we start); PR cycle time and review time down; no rise in change-failure rate.
- For each developer: they delegate whole jobs from a sentence and spend their time deciding,
  not typing — without having to live in a terminal.
- For us: a repeatable pilot we can sell to the next client, with its numbers on the product page.

---

## 2. Positioning — where the gap is

Running several agents in parallel worktrees is now a **free commodity and a graveyard**: Claude
Code's desktop app does it natively, Conductor, Sculptor and Nimbalyst do it for free, and Terragon,
Vibe Kanban's company and Crystal have all shut down or been deprecated in 2026. Competing there is
competing with Anthropic's free features.

Anthropic is also moving up: agent teams (experimental), **Claude Tag** in Slack (Team/Enterprise,
one shared Claude per channel), Claude Code Projects (cloud threads with shared memory). Cloud agent
vendors (Devin, Factory, Cursor, Copilot Agent HQ, Codex) sell autonomy by the credit.

**Nobody we found offers the combination:**

| | Anthropic (Tag, teams, desktop) | Devin / Factory / Cursor | Conductor & co. | **Agentopolis** |
|---|---|---|---|---|
| Persistent role agents per product, with memory | partial (Tag remembers a channel) | no | no | **yes** |
| Independent verification (oracle + cold reviewer + merge queue) | no | review bots | no | **yes** |
| Approvals as a policy the company sets | no | limited | no | **yes** |
| Custom agent types, tested before use | subagent files | no | no | **yes** |
| Code and credentials stay on the customer's machines | desktop only | no (their cloud) | yes | **yes** |
| Windows + macOS desktop | yes | web | macOS only | **yes** |
| Measures its own impact | no | usage dashboards | no | **yes** |
| Comes with people who set it up on *your* case | no | no | no | **yes (the consultancy)** |

The last row matters most. Nathan's framing is right: the value of consultancy is *applying a model
that works for us to a company where it has never been applied*. The product is the method,
packaged; the consultancy is what makes it fit.

**What we must never compete on:** raw model quality, parallelism, or "more PRs". Those are
Anthropic's, and more PRs is precisely the problem customers already have.

---

## 3. The legal foundation (decide first)

Everything else depends on this, and both current codebases violate it as a *product*:

- Anthropic's Claude Code legal page: developers building products **"should use API key
  authentication … Anthropic does not permit third-party developers to offer Claude.ai login into
  their own applications, or to route requests through Free, Pro, or Max plan credentials on behalf
  of their users … developers may not collect, store, or intermediate Claude.ai credentials or
  session tokens."**
- The same page allows **"an end user … signing in to the unmodified Claude Code binary with their
  own Claude subscription, including where a platform hosts Claude Code"** — provided the binary is
  unmodified, nobody resells usage, and "Claude Code" is not in the product's name.
- Consumer terms (Pro/Max) forbid commercial use (§11) and automated access; in April 2026 Anthropic
  blocked subscription OAuth tokens in third-party tools server-side.
- Agentopolis today runs every turn on one Max token minted by hand and stored in its env file —
  fine as Nathan's personal risk (his own decision record says so), impossible to sell. AIOrchestrator
  spawns the CLI under the owner's own login — same position.

**The rule for the product (D1, D2, D12):**
1. The runtime (the *Runner*, §5) runs **on hardware the customer controls** — a developer's PC or
   the team's server.
2. It launches the **unmodified** `claude` binary. For a customer, credentials are an **Anthropic API
   key of the customer's organization** (direct, Bedrock or Vertex — D12), the only mode a customer
   sees (D1). Agentopolis's cloud never sees, stores, forwards or swaps it. The subscription/terminal
   mode exists only for our own organization and users (§3.1).
3. Our cloud holds identity, licences, organization structure and — only if the customer opts in —
   metadata (task titles, states, cards). Never code, never prompts, never Claude credentials.
4. **Optional, worth one email:** ask Anthropic (partner/sales channel) whether customers could later
   sign in their own Team/Enterprise seats on the unmodified binary, which the legal page appears to
   allow. It is not needed for v1 — customers use API keys — but it would widen the offer, and the
   terms are moving (the support site and the legal page partly contradict each other in September
   2026).

This also resolves the question I asked earlier (*where do a client's agents run?*): with the
customer, always; our cloud is the control plane, never the execution plane.

### 3.1 D1 — two credential modes (owner, 2026-09-29 13:55)

The owner will also use Agentopolis for his own work, the way he uses AIOrchestrator today. So the
product has **two credential modes**, chosen in the **organization settings** and in each **user's
settings**:

- **Subscription mode** — agents run in terminal sessions under the user's own Claude login, exactly
  as AIOrchestrator works now. It exists for our own organization and users; **customers never see
  this setting**.
- **API-key mode** — the only mode a customer sees: they enter an API key for their AI provider, per
  organization (and optionally per user).

What this implies for the design (to confirm in the brainstorm):
- The mode is a per-organization **capability granted by the licence**, not only a hidden UI switch:
  the setting is absent from customer organizations' pages *and* refused by the Runner, so it cannot
  be switched on by editing a file.
- The kernel keeps both runners behind one seam: headless `claude -p` for API-key mode, and the
  terminal/interactive runner (AIOrchestrator's) for subscription mode. §8's "Runner" row becomes
  "both, chosen by the credential mode".
- "An API key of **any** AI provider" widens the kernel beyond Claude Code — see open question 11.

---

## 4. The product, from the user's chair

### 4.1 The cast

- **The organization** — the company. Members have a role: **Owner, Admin, Maintainer, Member,
  Viewer**. Admins set policy; maintainers approve for their projects; members delegate work.
- **Projects** — one per product/repo. Each has a **standing lead** (a named persona, e.g. "Ada ·
  lead Fincanva") who owns its backlog, briefs the crew, and merges to the work branch.
- **Job colleagues** — developer, reviewer, tester, researcher, designer, and any custom role —
  born when a task opens, retired when it closes. Human names from a pool, faces per role.
- **Each human's desk** — a personal concierge (Agentopolis's Jarvis, generalized: *one per person*,
  not one per company). It is the door for everything waiting for *that* person: questions,
  approvals, results, the morning line. It routes a sentence to the right project lead.

### 4.2 A day, for one developer at Guido's

1. **08:00** — the morning line in their desk: *"While you were away: task #41 merged to dev (routine
   export −38% on the benchmark); #43 is stuck, needs your call; the reviewer found a correctness
   issue in #44, back with the developer."* Silent if nothing changed.
2. They write to the lead: *"The nightly reconciliation takes 5 hours. Find where the time goes
   and get it under one hour without changing results."* A 👀 lands in two seconds; the lead replies
   with what it understood and what it assumes, and proposes the **playbook** (§4.4): profile first,
   then build a benchmark and an equivalence test, then optimize.
3. The lead opens the tasks. Each spawns a developer in its own worktree and branch; the thread holds
   everything the task spawns. The **task card** (edited in place, never reposted) shows state, who
   is on it, the last fact, what waits for a human, and cost.
4. A developer needs to read production data statistics: a **permission card** arrives — *yes once /
   for this task / always / no*. The developer taps once and goes back to their own work.
5. The work comes back: the house re-ran the benchmark and the held-out equivalence tests the agent
   could not edit, a cold reviewer read it, the merge queue put it on `dev`. The card says *"−41%,
   results identical on 3 datasets, reviewed, merged to dev"*. The developer's job was the decision,
   not the diff.
6. Publishing to production is an **effect**: a red card to the project's maintainers, generated from
   the actual plan (never from the model's prose), approved with a button (and a PIN if the company
   wants one).

### 4.3 Surfaces

The same state everywhere, drawn from one register (never a separate status that can lie):

- **Desktop cockpit (Avalonia, Windows + macOS)** — the full product: organization and projects,
  the **Waiting for you** inbox, a board of tasks and who is on them, live threads with streaming
  replies, a diff and result viewer, the Role Studio, permissions and approval policy, accounts and
  usage windows, the impact dashboard. A **"take over"** button opens a real terminal on any agent's
  session for the power user — Nathan's point that the terminal scares non-technical people is
  honoured by making it optional, not by removing it.
- **Web** — the same cockpit, trimmed to what you do away from your desk: inbox, approvals, chat
  with your desk and leads, task board, impact. Installable on the phone (PWA).
- **Slack (connector)** — for teams that live there: project channels, thread = session, cards =
  Block Kit, Home tab = the inbox. Agentopolis's Slack work is the reference implementation.
- **Telegram (connector, optional)** — kept for people like the owner; off by default.
- **Git host** — branches and PRs appear on GitHub/GitLab as usual; nothing proprietary about the
  output.

Every surface is a **connector** over one conversation model: *space* (project or topic) → *thread*
(one conversation/session) → *messages* and *cards* (a question, an approval, a stuck-task choice,
a result). A connector renders them and returns button presses; it never decides anything.

### 4.4 Playbooks — how a team of agents actually works

Both codebases hard-wire one topology (hub-and-spoke here; lead → developer → reviewer there). A
product needs this to be data. A **playbook** is a named pipeline of stages, each with a role, a
gate and a stop rule:

- **Feature** — lead brief → developer → house checks → cold review → merge queue → (effect) release.
- **Performance** (the pilot's) — profile → hypothesis list → **benchmark + equivalence oracle
  built first and frozen** → optimize → house re-measures on the frozen oracle → review → merge.
  This is the killer feature for Guido: the agent cannot "win" by changing the test, and the number
  on the card is measured by the house, not claimed by the agent.
- **Bug** — reproduce with a failing test first → fix → oracle → review.
- **Research** — a web-enabled researcher in its own sandbox returns a fixed-shape report.
- **Custom** — teams compose their own from stages.

### 4.5 Custom agents — the Role Studio

A role is data (Agentopolis's `role.yaml` + prose, generalized):
- persona (name, face, voice), kind (**standing** or **job**), the prose it runs on;
- model **preset** (small / normal / hard — never a model name in the UI) and the menu a lead may
  choose from; escalation only after measured failure;
- built-in tools allow-list, MCP connectors, which requests it may make, which project tools it opts
  into, its time ceiling;
- **example cases** (golden tests: an input, the behaviour required, a good and a bad answer).

The **wizard** ("describe the colleague you need") drafts all of that from a conversation — the
agent Nathan left as a placeholder. A role is **promoted only when its example cases pass**, the
same gate Agentopolis already uses for prose and model changes. Roles are versioned per
organization, shareable later as a library (a marketplace is a v3 idea, not a v1 one).

Built-in library at launch: lead, developer, reviewer, tester, researcher, designer, QA, ops,
docs writer, support triage.

---

## 5. Architecture

```
            ┌──────────────────── Agentopolis Cloud (ours) ─────────────────────┐
            │ Identity (orgs, members, roles) · Licences & seats · Update feed   │
            │ Relay (outbound WebSocket from Runners; metadata only, opt-in)     │
            │ Web cockpit (Blazor) · Impact reports                              │
            └───────────────▲───────────────────────────────▲────────────────────┘
                            │ HTTPS, org token               │ HTTPS
┌───────────────────────────┴───────── customer premises ───┴───────────────────────┐
│  RUNNER (Linux: team server, VM, Docker, or WSL2 on a dev PC)                      │
│   Kernel (TypeScript, from Agentopolis)                                            │
│    ├ Register (SQLite WAL; Postgres later) — the only truth                        │
│    ├ Scheduler: one loop per conversation, lanes, admission by memory              │
│    ├ Turn engine: unmodified `claude -p`, stream-json, envelope, prefix/cache      │
│    ├ Authorizer: permission switches + approval policy (no model, ever)            │
│    ├ Effects ledger (7 states) · Work: tasks, worktrees, oracle, review, merge queue│
│    ├ Playbooks & Role registry (hot-reloaded, validated, gated by example cases)   │
│    ├ Connectors: Slack · Telegram · Git host · cockpit API                         │
│    └ Sandboxes: bubblewrap + nftables per turn; Claude creds from the customer     │
└───────────────▲──────────────────────────────────────────────────────────────────┘
                │ local API (HTTP + WebSocket, versioned schema) — LAN, SSH tunnel, or via Relay
        Desktop cockpit (Avalonia, C#)
```

**Decisions this proposes, with the reason:**

1. **The kernel is Agentopolis's, in TypeScript.** It is the more mature kernel for this job: the
   register as the only transactional truth, pending work as a query (restart is free), the effects
   ledger, the permission broker on the CLI's own `can_use_tool` channel, the oracle and merge queue,
   measured token economics, relay-per-turn surviving restarts, a fake-claude harness and golden
   replays. AIOrchestrator's engine is a 19,100-line class with Telegram and role names wired through
   it (§7) — its value is in its protocols and its behaviour rules, not its engine. The Claude Agent
   SDK is also TypeScript/Python only, which keeps that door open.
2. **The chassis is C#.** Accounts, licences, the updater, the Avalonia shell, design tokens and the
   Blazor web already exist in the DVFT suite. The two languages meet at **one versioned API**, which
   also happens to divide the work along the brothers' own lines: Nathan the kernel, Manu the product.
3. **The Runner is Linux.** One isolation model (bubblewrap + nftables per turn, separate OS users)
   instead of three. Windows developers run it in WSL2 or on the team server; the desktop app runs
   natively on Windows and macOS and connects to it. A developer who wants "everything on my PC" gets
   WSL2 installed by the installer.
4. **Multi-tenant happens in the cloud, not in the Runner.** One Runner serves one organization (or a
   team within it). SQLite with a single writer is enough there; Agentopolis already recorded the
   thresholds at which to move to Postgres.
5. **The cockpit never embeds the kernel.** Desktop, web, Slack and Telegram are all clients. That is
   what makes "run it on the team server and use it from anywhere" free.

---

## 6. Teams, identity and governance (what neither codebase has)

Both systems are single-owner to the bone (`owner_user_id` is one string with ~61 uses in
Agentopolis; `TelegramOwnerUserId` here). The team model is new work:

- **Members and roles** (Owner/Admin/Maintainer/Member/Viewer), synced from the cloud identity into
  the Runner. Every card records **who** pressed it; the audit trail names people.
- **Approval policy per effect kind**, set by admins: who may approve a production merge (maintainers
  of that project; optional two-person rule), who may answer a permission card (the requester, else
  the project's maintainers), what happens to an unanswered card (wait / no / yes).
- **Routing:** a card goes to the person who started the thread, falls back to the project's
  maintainers, then to the channel. When two people answer, the first valid press wins and the card
  says who.
- **Permission switches** (Agentopolis's ~20 rows of Free / Ask me / Forbidden) per organization
  with **per-project overrides**; only admins loosen, anyone may tighten; loosening can require a PIN.
- **Memory with an author.** "What I know about you" becomes per person; team knowledge lives with
  the project lead and records who said it. Forget reaches the register.
- **Shared vs personal agents:** leads belong to projects and serve the team; each person's desk is
  theirs alone.
- **Cost and limits per person and per project**, shown, never blocking (Agentopolis's rule); usage
  windows per account visible to the team.

---

## 7. What we carry from each codebase

**From Agentopolis (the engine):** everything in §5.1, plus the envelope-as-message, the four-tool
MCP surface with per-turn capability tokens, integrity/contamination labels, grants that expire,
the closing turn after a stop, routines and sentinels, the morning line, earned permission,
the effects adapters (git merge built deterministically, mail as drafts only), the deploy discipline
(release as a card with automatic rollback, CLI promoted through a contract suite and canary), the
threat model mapped to OWASP Agentic. And its **measured token rules**: nothing dynamic in the
system prompt, memory in the first user message, model and effort fixed at spawn, per-role tool
allow-lists, 1-hour cache for standing agents and 5-minute for job agents.

**From AIOrchestrator (the behaviour):** the rules that took two months of live use to learn, most of
which are about *humans* and apply unchanged:
- the receipt in the agent's own words before work starts, and the "done" line when it ends;
- answer what they asked, first line first, even mid-task;
- questions with options, a recommendation, a risk level, and an optional deadline + default;
- repeats edit, they never stack; alerts the human cannot act on never reach them;
- run to the end — a report is not a stopping point;
- **scope discipline**: the job is what the human asked for; discoveries are parked, not done;
- honest progress: *done* means ready to merge, never "waiting for the merge";
- the role prose for supervisor, implementer and reviewer (≈3,600 lines), mined for the new roles;
- the settings catalogue rendered identically on every surface; presets per person;
- the pause lever, sibling sessions (two jobs steered separately), the request-file idea as the
  model for agent → house requests.

**From the DVFT suite (the chassis):** Cognito-based identity with Google login and a DPAPI-protected
desktop session; server-checked entitlements with device limits and trials; the Squirrel/S3 update
pipeline and `/api/versions`; the Avalonia shell (`ProductShellLib`, `UniversalControlsDesktopLib`,
theming from design tokens, brand-asset generator); the Blazor web with a BFF keeping refresh tokens
in HttpOnly cookies; the coding patterns and their enforcing hooks.

**Do NOT carry:**
- `SecretsVaultLib/Secrets_Config.cs` — it is tracked in git with plaintext AWS, Anthropic, GitHub
  and Freshdesk keys, and the DVFT API runs on plain HTTP. The new repo starts with secrets in a
  vault/environment from commit one.
- The fintech product enums and the ~45 switch sites that register a product in the suite — the new
  repo needs one product, registered once.
- Consumer-subscription token handling, for customer organizations (§3). Our own terminal/subscription
  mode (D1) is carried, gated by the licence.
- Telegram-shaped domain fields (`TelegramTopicId` in the session model and similar).

**Gaps nobody has built yet:** organizations/teams (§6), macOS packaging (bundle, signing,
notarization — the suite has none; Velopack, Squirrel's successor, covers Windows and macOS and is
worth evaluating), Windows code signing, CI, Stripe wiring, GDPR processor flows.

---

## 8. Where the two systems disagree — decisions for the brainstorm

| Topic | AIOrchestrator | Agentopolis | Proposal |
|---|---|---|---|
| Questions | one open question at a time, enforced by a hold | never capped; a second one on the same task edits its card | **one card per task, edited; no global cap** — both goals met |
| Progress truth | PLAN.md ledger the session writes | the register only; "markdown ledger" explicitly rejected | **register**, rendered as a ledger on every surface |
| Parallel sub-agents | implementers encouraged to fan out | swarms rejected (measured 2.6–5.9× tokens) | **serial by default; fan-out as a per-role switch**, cost shown |
| Status | periodic STATUS digests, PULSE line | no status command, cards edited in place, morning line | **Agentopolis's**: the pull-model inbox and the edited card |
| Runner | terminal sessions (visible), print, stream | `claude -p` stream-json only, headless | **headless** for API-key mode, with "take over in a terminal" in the cockpit; **terminal** runner for our own subscription mode (D1) |
| Resume | resume supervisor/solo; fresh + state pack for workers | resume while the prefix fingerprint holds; rotate with a pack | **Agentopolis's**, it is measured |
| Owner channel | Telegram topic per orchestration | Slack thread per session, channel per topic | **connector-neutral** space → thread → cards |
| Authority | owner only | owner only, PIN | **policy per effect kind, per role** (§6) |

---

## 9. The business around it

- **Pilot** (Guido, now): four weeks, free, result-based. We install the Runner on their server or a
  VM, connect their repos and their Claude Team seats or API key, set up one project with the
  performance playbook, and run it with them in the desktop app (D5), with Slack as an optional second
  surface. **Agree the baseline and the measurement in writing on day one** ("routine X, dataset Y,
  wall-clock on machine Z").
- **After the pilot (D9):** a subscription per seat (a human who manages agents) plus an implementation
  fee (setup, custom roles, playbooks for their stack) and an optional monthly retainer (we tune
  roles, review impact, train the team). Market references: retainers typically $5–15k/month;
  tooling $200–600 per engineer per month including model usage; median measured throughput gains
  are modest (+5–15%), so promise the measured thing, not a multiplier.
- **Impact reports built in:** cycle time, PR throughput, review time, change-failure rate, cost per
  merged change, agent-hours — the DX/DORA vocabulary buyers already use. The monthly report to the
  client is generated, not written — and it becomes the metrics on the product page.
- **Our own usage costs nothing to resell**: the customer pays Anthropic directly (§3), which also
  keeps us outside "reselling usage".

---

## 10. Build order

Each milestone is shippable and demoable; each gets its own spec → plan → build cycle.

- **M0 — Foundations (week 1).** Agree D3/D4 with Nathan; lay out the one repository (D4) with the
  TypeScript engine and the C# app side by side; write the Runner API contract (versioned, with a
  contract test on both sides). Write to Anthropic about §3. Secrets policy from the first commit.
- **M1 — Pilot kit (weeks 1–5).** Two tracks, one deliverable: Guido's team using it on their routine,
  with a before/after number.
  - *Engine (Nathan):* the Agentopolis kernel changed only where the pilot needs it — customer-owned
    Anthropic API keys (D12) and, for our own organization, the terminal/subscription mode (D1);
    a Runner installer for a Linux server/VM; **several humans** with a desk each (D7), card routing
    and the approval default of D6; the **performance playbook** with a frozen benchmark oracle.
  - *Pilot cockpit (Manu):* the minimal Avalonia app of D5 — inbox, task board, threads with
    streaming replies, approvals — talking to the Runner directly over its API (LAN or SSH tunnel).
    No cloud account, licence or updater is needed for the pilot: the Runner issues the pilot's
    member tokens itself.
- **M2 — Product chassis (after the pilot starts).** New Cognito pool (separate from DVFT and
  Fincanva), organizations and members synced to the Runner, entitlements per org (including the
  licence-granted subscription mode of D1), updater for Windows and macOS, the relay, web v1 (inbox
  and approvals).
- **M3 — Custom agents.** Role Studio, the wizard, example-case gate, playbooks as data, the built-in
  role library.
- **M4 — Impact and business.** Impact dashboard and generated monthly report, Stripe, trials,
  onboarding for the second customer.
- **M5 — Breadth.** Telegram connector, GitLab, Postgres register, hosted relay hardening, role
  library sharing.

**The critical-path rule (revised by D5):** the desktop app IS on the pilot's path, so the pilot
cockpit is kept minimal — inbox, task board, threads with streaming replies, approvals — and
everything else in M2 (licences, updater polish, web) can follow the pilot's start.

---

## 11. Risks

1. **Anthropic ships it.** Tag, agent teams and Projects cover part of this. Our moat is the
   combination in §2 — verification, governance, custom roles, self-hosted, Windows, and people who
   fit it to the customer — plus speed of adaptation. Keep the connectors thin so Claude Tag or a
   future Anthropic surface can become *another connector*, not a competitor.
2. **Terms change again.** Mitigated by §3: customers are on API keys, which always work; our own
   subscription mode (D1) is our own risk, as it is today.
3. **Two codebases full of rules.** Both carry dozens of decision records written for one owner's
   incidents. Agentopolis already adopted "rigour by risk" — keep it: full rigour for security, money,
   personal data and data loss; light elsewhere. Carry *rules that serve a team*, not every rule.
4. **Two languages.** Contained by one versioned API with a contract test on both sides.
5. **Isolation on customer machines.** A Runner on a developer's PC under WSL2 is weaker than a
   dedicated server. Say so in the setup and recommend a team VM.
6. **The pilot fails on the number.** Result-based and free makes that survivable ("friends as
   before"); the playbook's oracle at least makes the result honest either way.
7. **GDPR and the AI Act as a vendor.** Customer data stays on customer premises by design, which
   keeps us mostly out of the processor role; still needs a DPA template, retention rules and the
   Article 50 disclosure text before the second customer.

---

## 12. The questions the brainstorm answered

All eleven were answered on 2026-09-29; the answers are the decisions log at the top (D2-D12). Kept
here as the record of what was asked.

1. Is the §3 rule acceptable — Runner always on customer hardware, their credentials, our cloud as
   control plane only?
2. Kernel in TypeScript (Agentopolis) + chassis in C# with one API between them — or one language?
3. One monorepo or two repos (kernel / product)?
4. Pilot on Slack only, desktop after — or does Guido need to see the desktop app in the pilot?
5. Which member roles and approval defaults does a four-person startup actually want?
6. The per-person desk (a Jarvis each) — yes, or one shared front door per team?
7. Name: Agentopolis for the product, Coding Wand for the company?
8. Pricing shape after the pilot — per seat, per project, or flat per team?
9. Who writes the default role prose — the owner, Nathan, or generated and reviewed?
10. Which of AIOrchestrator's human-factor rules (§7) are non-negotiable for the owner?
11. "Any AI provider" (D1): does v1 mean Anthropic API keys plus Anthropic via Bedrock/Vertex (which
    Claude Code already supports), or other vendors too (OpenAI, Google)? Other vendors need a second
    agent harness (for example the Codex CLI) behind the same turn engine — a real cost, better placed
    after the pilot.

---

## Sources

- Agentopolis: `CLAUDE.md`, `docs/superpowers/specs/2026-09-21-target-architecture.md`,
  `2026-09-18-agentopolis-v1-design.md` (§18 measurements), `plans/2026-09-21-roadmap-milestones.md`,
  `docs/decisions/*` (0003, 0017, 0027, 0029, 0033, permission switches, rejected token techniques),
  `src/` (engine, turn, scheduler, governance, work, effects, slack), `tests/golden/*`.
- AIOrchestrator: `CLAUDE.md` (decisions 1–27), `AIOrchestratorCoreLib/` layout and sizes,
  `kit/skills/*`, `docs/MODIFICHE-DEL-FORK.md`.
- DVFT suite: `LICENSING_ARCHITECTURE.md`, `AUTH_ARCHITECTURE.md`, `DEPLOYMENT_PLAN.md`,
  `NEW_PRODUCT_APP_GUIDE.md`, `SUITE_DECISIONS.md`, `docs/superpowers/notes/2026-09-21-product-onboarding-improvements.md`.
- Anthropic: code.claude.com/docs/en/legal-and-compliance; code.claude.com/docs/en/agent-sdk/overview;
  anthropic.com/legal/consumer-terms; anthropic.com/news/introducing-claude-tag;
  support.claude.com/en/articles/15036540; The Register, 2026-04-06, on subscription OAuth blocking.
- Market: Faros AI (AI software engineering), DORA 2025, Stack Overflow Developer Survey 2025 (AI),
  METR 2025, DX (AI measurement framework, assistant pricing), vendor pricing pages for Devin,
  Factory, Cursor, GitHub Copilot, OpenAI Codex, Jules, Warp, Tembo, Conductor, Sculptor (September
  2026; aggregator figures to be re-checked before quoting to a customer).
