# Agentopolis pilot — plan 01: foundations (M0)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended)
> or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`)
> syntax for tracking.

**Goal:** a new repository (D4, corrected 17:48) holds the whole product: `engine/` is seeded by
copying the Agentopolis kernel and passes its own suite there, `app/` has a C# home and a CI job, the
Runner API has a versioned contract both sides test against, and the owner's decisions are recorded
where the engine's rules live. `Coding-Wand/agentopolis` is read, never written.

**Architecture:** three top-level folders (overview P6): `engine/` (TypeScript, copied from
Agentopolis at a named commit, then ours), `api/` (the contract, language-neutral), `app/` (the C#
solution).
The contract is an OpenAPI 3.1 document plus JSON Schemas for the Server-Sent Events; the engine
validates its responses against it in a test, and the cockpit deserializes the recorded fixtures in a
test.

**Tech Stack:** TypeScript/Node 24 + vitest (engine, existing); .NET 10 + xUnit (app); OpenAPI 3.1 /
JSON Schema 2020-12 (contract).

**Spec:** `docs/superpowers/specs/2026-09-29-agentopolis-product-design.md` §5 (decisions 1-5), D3,
D4; overview `…-00-overview.md` P1, P2, P6.

## Global Constraints

- Nothing is pushed, opened or commented on `Coding-Wand/agentopolis`; it is cloned read-only to copy
  from.
- No new library on the engine side without the owner's word (a rule carried over with the copy). Contract
  validation in the engine test uses a library only if one is already a dependency; otherwise the test
  checks shapes by hand against the schema file.
- Stage explicit paths; never `git add -A` / `.` / `commit -a`. One point = one commit with its test.
- The merge to `main` is the owner's. The first commit (point 0) goes straight to `main` of the empty
  repository; everything after it on branch `m0/foundations`.
- Secrets never in any file of the repository; they come from the environment.
- Files, code, commits in English.

## Review Focus

1. **A contract change that only one side notices** — the engine and the app must both fail their
   test when `api/v1/openapi.yaml` changes shape. Point 2's two tests pin that.
2. **CI that silently skips the C# job on a PR that touches only `app/`** — path filters must include
   `app/**` and `api/**`. Point 3 pins it.
3. **The copied rules contradicting the owner's decisions** — "free or nothing" and "one owner" arrive
   with the copied `CLAUDE.md` as rules that do not break; point 2 rewrites them in the same commit that
   brings them, or the next session will obey the old rule.
4. **An event type with no schema** — every SSE event name the engine can emit must have a schema;
   point 2's engine test enumerates the emitter's names against the file.
5. **A copy that silently lost tests** — the seeded `engine/` must run the same test count as the
   source at the copied commit, minus a written list of what was removed and why (point 1).
   (Line endings: `.gitattributes` fixes `*.cs`, `*.axaml`, `*.yaml`, `*.ts` to LF — point 0.)

---

## 0 · The new repository

**Goal:** an empty private repository exists under the company's GitHub organization, with the three
folders, a README, `.gitignore`, `.gitattributes` and a CODEOWNERS file.

**Files:** `README.md` (what the product is, one paragraph from the spec §0; the layout), `.gitignore`
(Node, .NET, OS files), `.gitattributes` (LF for `*.ts`, `*.js`, `*.json`, `*.yaml`, `*.md`, `*.cs`,
`*.axaml`, `*.sh`), `.github/CODEOWNERS`, `engine/.gitkeep`, `app/.gitkeep`, `api/.gitkeep`.

**Done when:** the repository exists with the name the owner chose (creating a GitHub repository is the
owner's call — he creates it or tells the session to), private, default branch `main`; the first
commit holds only the files above.

## 1 · `engine/` seeded from Agentopolis — **risk**

**Goal:** the Agentopolis kernel runs in `engine/` of the new repository with its own suite green, and
nothing of Nathan's machine or projects comes with it.

**Files:** copy from a read-only clone of `Coding-Wand/agentopolis` at `f803c71` (or the commit the
owner names): `src/`, `tests/`, `tools/`, `drizzle/`, `drizzle.config.ts`, `hooks/`, `deploy/`,
`scripts/`, `package.json`, `pnpm-lock.yaml`, `pnpm-workspace.yaml`, `tsconfig*.json`, `biome.json`,
`vitest.config.ts`, `.node-version`, `docs/decisions/` (the records the code cites), `docs/threat-model.md`,
`examples/home/roles/` (the role prose, marked DRAFT for the owner's review, D10). **Not copied:**
`examples/home/agents/` and `examples/home/projects/` (Nathan's colleagues, Fincanva, Ianus),
`deploy/VPS.md`, `deploy/SWITCH.md`, `deploy/MIGRATE.md`, `deploy/migrate-home.sh`, the milestone plans
and reports, `plans/PARKED.md`. Create `engine/NOTICE.md` (source repository, commit, date, what was
left out and why), `.github/workflows/engine.yml` (the source's `checks.yml` steps with
`working-directory: engine`, path filter `engine/**`, `api/**`), and fix the paths the move breaks
(the expand-only migration check, `machine.yml`'s container paths, any absolute path to the repository
root in `tools/`).

**Done when:** `pnpm -C engine install --frozen-lockfile && pnpm -C engine typecheck && pnpm -C engine
lint && pnpm -C engine test` are green in CI and locally; the test count equals the source's at the
copied commit minus the tests of the files not copied, each listed in `NOTICE.md`; `grep -rn
"159.195.254.120\|nathanthegrey\|fincanva\|ianus\|aiorchestrator" engine/` returns only lines
listed in `NOTICE.md` as deliberately kept (for example a test fixture name), each with its reason.

## 2 · The engine's rules, rewritten with the owner's decisions — **risk**

**Goal:** the new repository's `CLAUDE.md` carries the engine's working rules (the copied Italian
`CLAUDE.md`, translated to English) amended by D1 (credential modes), D2 (runtime at the customer),
D5 (desktop app), D6 (any member approves production by default), D7 (a desk per person) and P1-P6,
so no session enforces "free or nothing" or "one owner" against the pilot.

**Files:** `CLAUDE.md` (root: the product, the layout, the rules for each folder, how to deliver);
`engine/docs/decisions/0035-credential-modes.md`, `0036-several-members.md`,
`0037-slack-optional-and-runner-api.md`, `engine/docs/decisions/README.md` (index).

**Content, exactly:**
- 0035: subscription mode stays for our own organization and is granted by the licence
  capability `subscription`; a customer organization runs on its Anthropic API key; "free or
  nothing" applies to subscription mode only; in API-key mode cost is real money, shown and never
  blocked, with the per-turn `--max-budget-usd` ceiling unchanged. Supersedes the parts of 0003 that
  refuse a key. 0027 (gateway pass-through) stands: a direct API key passes through with its digest
  checked; Bedrock/Vertex wait for their own record (P3).
- 0036: members with roles Owner/Admin/Maintainer/Member/Viewer; approval policy per effect kind,
  default "any member" for `merge_production` (D6); a desk (front-door colleague) per member (D7);
  every decision records the person who pressed. "No model in an authorization decision" unchanged.
- 0037: Slack optional (P5); Runner API on `127.0.0.1:4894`, bearer token per member, SSE (P1, P2).

**Done when:** the three records exist and the index lists them; `grep -n "free or nothing" CLAUDE.md`
shows the rule scoped to subscription mode; no line of `CLAUDE.md` says only one person may press a
button; the owner has read it (his word in the PR).

## 3 · The Runner API contract, tested from both sides

**Goal:** one file says what the Runner serves; the engine and the app each have a test that breaks
when the file changes shape.

**Files:** create `api/v1/openapi.yaml`, `api/v1/events.schema.json`, `api/v1/fixtures/*.json`
(one recorded response per endpoint, one per event type), `api/README.md`; engine test
`engine/tests/api/contract.test.ts`; app test `app/tests/Agentopolis.Api.Tests/ContractFixturesTests.cs`
(created in point 4's solution).

**Interfaces (the contract v1 — produced here, consumed by plans 02 and 03):**

All endpoints under `/v1`, `Authorization: Bearer <member token>`, JSON, UTF-8.

| Method | Path | Returns |
|---|---|---|
| GET | `/v1/me` | `Member { id, name, role, deskRoomId }` |
| GET | `/v1/inbox` | `InboxItem[] { kind: question\|approval\|permission\|stuck\|alert\|parked\|expired\|flag, title, since, roomId, taskId?, cardId? }` |
| GET | `/v1/tasks?state=open\|closed` | `TaskLine[] { id, title, project, state, since, worker?, roomId }` |
| GET | `/v1/tasks/{id}` | `TaskDetail { line: TaskLine, acceptance: string[], rounds, costMicroUsd, costLabel: estimated\|billed, measure?: { metric, unit: s\|ms, baseline, latest, speedup, spread } }` |
| GET | `/v1/rooms` | `Room[] { id, name, kind: desk\|project\|topic\|task, lastAt }` |
| GET | `/v1/rooms/{id}/messages?after={messageId}` | `Message[] { id, roomId, author: { kind: member\|colleague\|house, name }, text, at, cardId? }` |
| POST | `/v1/rooms/{id}/messages` | body `{ clientId, text }` → `{ messageId }` (idempotent on `clientId`) |
| GET | `/v1/cards/{id}` | `Card { id, subject: { kind, id }, epoch, title, body, risk: low\|high, options: CardOption[] { action, label, style: primary\|danger\|default } , details? }` |
| POST | `/v1/cards/{id}/press` | body `{ action, epoch, text? }` → `{ result: ok\|stale\|pin_required\|refused, reason?, pinWindowId? }` |
| POST | `/v1/pin/{windowId}` | body `{ pin }` → `{ result: ok\|wrong\|locked }` |
| POST | `/v1/brake` | → `{ result: ok }` |
| GET | `/v1/events` | `text/event-stream`; events below; `Last-Event-ID` resumes |

SSE events (`events.schema.json`): `inbox.changed {}`, `task.changed { taskId }`,
`message.new { roomId, messageId }`, `card.changed { cardId, epoch }`,
`reply.delta { roomId, turnId, text }`, `reply.end { roomId, turnId, messageId }`,
`turn.started { roomId, turnId, colleague }`, `turn.ended { roomId, turnId }`. Every event carries
`id` (monotonic, from the register's event cursor) and `at` (ISO 8601 UTC).

C# DTOs produced in `app/src/Agentopolis.Api/` (point 4), one record per schema: `Member`, `InboxItem`,
`TaskLine`, `TaskDetail`, `Measure`, `Room`, `Message`, `MessageAuthor`, `Card`, `CardOption`,
`PressResult { Outcome: PressOutcome (Ok|Stale|PinRequired|Refused), Reason?, PinWindowId? }`,
`PinResult { Outcome: PinOutcome (Ok|Wrong|Locked) }`, `RunnerEvent { Id, Name, Json }`; enums parsed
with `JsonStringEnumConverter` from the contract's snake_case values.

Errors: `401` no/unknown token, `403` role may not do this (body `{ reason }`), `404`, `409` stale
epoch on press (also returned as `result: stale` with 200 — the app treats both the same), `422`
invalid body.

**Done when:** `engine/tests/api/contract.test.ts` loads the YAML and asserts every fixture in
`api/v1/fixtures/` validates against its schema, and fails when a required field is removed from a
fixture (checked once by hand, noted in the PR); `ContractFixturesTests` deserializes every fixture
into the app's DTOs with `JsonSerializerOptions` `PropertyNamingPolicy = CamelCase`, `UnmappedMemberHandling
= Disallow`, and fails when the YAML gains a required field the DTO lacks; `api/README.md` states the
versioning rule: additive changes stay `v1`, anything else is `v2` served side by side.

## 4 · The C# home and its CI

**Goal:** `app/` holds a .NET 10 solution that builds and tests on Windows and macOS in CI, with the
suite's coding rules and hooks copied in.

**Files:** create `app/Agentopolis.App.slnx`, `app/src/Agentopolis.Api/` (DTOs of point 2 +
`RunnerClient` interface only), `app/tests/Agentopolis.Api.Tests/`, `app/Directory.Build.props`
(net10.0, nullable enable, warnings as errors), `app/CODING_PATTERNS_QUICKREF.md` (copied from the
DVFT suite, header line naming its source commit), `.claude/hooks/` C# pre-write check copied from the
suite and wired in `.claude/settings.json` for `app/**/*.cs` only, `.github/workflows/app.yml`
(matrix `windows-latest`, `macos-latest`; `dotnet build`, `dotnet test`; paths `app/**`, `api/**`),
`.gitattributes` (LF for `*.cs`, `*.axaml`, `*.yaml`, `*.json`, `*.md`).

**Done when:** a PR touching only `app/` runs `app.yml` and not `engine.yml` (and the reverse), both
green; `dotnet test app/Agentopolis.App.slnx` passes locally on the owner's Windows
machine; the pre-write hook refuses a `.cs` file with a public setter under `app/` and ignores the
`engine/` tree.

## 5 · The one email to Anthropic (optional, owner sends it)

**Goal:** ask whether a customer could later sign in their own Team/Enterprise seats on the
unmodified CLI inside a third-party orchestration product (spec §3 point 4). Not needed for the pilot.

**Files:** `docs/superpowers/notes/2026-09-29-anthropic-question.md` (the draft, three paragraphs,
quoting the legal-and-compliance page lines the spec quotes).

**Done when:** the draft exists and the owner has decided whether to send it.
