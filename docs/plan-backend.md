# Plan backend — the seam between PLAN.md and a planning system elsewhere

**PLAN.md stays the execution ledger.** Sections, markers, the turn-end hook, the progress bar and the
role commands are unchanged. A backend synchronises **one section — `## OWNER REQUESTS`** — and nothing
else. `## PARKED` is never synchronised: a discovery nobody asked for is local (CLAUDE.md decision 22).

## The contract — `Planning/PlanBackend/IPlanBackend.cs`

| Call | Direction | Meaning |
|---|---|---|
| `List_ApprovedRequests(orchId)` | in | What the owner approved upstream and this plan does not have yet. |
| `Acknowledge_Request(orchId, requestId, ledgerRowRef)` | out | It is now a ledger line, at that text. |
| `Report_RowClosed(orchId, requestId, ledgerRowRef, evidence)` | out | That line reached `[x]`. A **report**, not a closure. |
| `Report_OrchestrationClosed(orchId, summary)` | out | The orchestration is over; one line of what its ledger said. |

`PlanMdBackend` is the default and does nothing. With it, `PlanBackendSync_Decider.Should_Sync` returns
false before any file is touched, so an installation that configures no backend behaves as it did
before this existed.

## What the app does with it

Once a minute, **off the bridge tick** (`BridgeEngineModel.Start_PlanBackendPass` starts the pass and
returns; one pass at a time), for every orchestration, `PlanBackend_Step.Sync`:

1. **Ingests.** Each approved request gets a row in the `## OWNER REQUESTS` table (their own words,
   appended, never renumbered, tagged `(upstream:<id>)`) **and** a `- [ ]` line at the end of the
   ledger. Both: the table records the ask, the ledger line is the only thing that can later close — a
   prose "status" cell is not a signal anything can read — and it traces to an owner request by
   construction (decision 22).
2. **Reports.** When that line's top-level marker turns `x`, the backend is told once, with the
   conversation entry live at the time as evidence, and the row's status cell is rewritten to say so.
3. **Remembers**, in `.plan-backend.json` beside PLAN.md. The app restarts daily; without it every
   launch would re-ingest and re-report.

PLAN.md is written first, then the state file, then the backend is told — so an interruption can leave
a request in the plan and **not yet acknowledged**, which is why every tracked request with no
`acknowledgedUtc` is retried on the next pass. A duplicated **row** is the one thing this order cannot
produce. The plan is only rewritten if its mtime has not moved since it was read (a session's own save
must not be discarded), and the app records the mtime of its own write so
`LedgerHealth_Tracker.Is_LedgerBehind` does not mistake it for the supervisor paying its ledger debt.

**It refuses rather than guesses**, and every refusal is logged: a title that already names a `[x]` or
`[-]` line (it would be reported closed for work that predates the request), a second request with a
title already tracked (one `[x]`, two deliveries claimed), a request with no id or no title.

## Configuring one

In `config.json` (next to the supervision root's other settings):

```json
{
  "repos": [],
  "planBackend": { "kind": "external", "assembly": "/opt/adapters/Adapter.dll", "type": "Adapter.PlanBackend" }
}
```

`kind: "plan-md"`, or no key at all, is the default. **Any other value is an error, not a default** — a
typo must not silently disconnect the owner's planning system. External types load by reflection and
need a public parameterless constructor. A failed load falls back to PLAN.md alone **and logs why**. The
key is hand-edited; the app never writes it, and `Save` keeps every key it does not own.

## Writing an adapter — what it can rely on

One .NET class implementing `IPlanBackend`, **outside this repository**, reading its own configuration
from its own place. This repo holds no client, no URL, no upstream vocabulary.

- **Lifetime**: one instance per process, shared by every orchestration, constructed once, reloaded only
  when `planBackend` changes, never disposed. Keep no per-orchestration state, and tolerate being called
  for an orchestration you have never heard of.
- **Loading**: plain `Assembly.LoadFrom` — no isolated load context, no `.deps.json` resolution. Ship
  self-contained or put your dependencies beside the app, or the first call throws.
- **Frequency**: `List_ApprovedRequests` once per orchestration per minute. Calls run on a background
  pass, so a slow one costs that pass and not the bridge — but keep it to seconds, not minutes.
- **`ledgerRowRef`** is `PlanRequest_Writer.Flatten(request.Title)` — the title with whitespace
  collapsed. It is your join key.
- **The seam is neutral on what "ready" and "closed" mean.** Each side decides on its own side: the
  backend decides what makes a row ready to hand over — including any blocker that would make an early
  handover a line nobody can finish — and the bridge hands over only what the backend lists as ready;
  the bridge decides what `[x]` means on the ledger and reports it back through the same backend, and
  what the backend does with that report — close the row, hold it for verification, anything else — is
  its own business, not this repo's to prescribe.
- **Only `[x]` is reported.** Started, blocked, blocked-on-owner and not-doing are not, and a row that
  goes `[x]` then back to `[ ]` is never un-reported. `Report_OrchestrationClosed` fires only for an
  orchestration that had at least one request ingested.

## Known limits, stated

- **Authority.** An adapter decides what "approved" means, and every request it hands over becomes a
  line in the owner's denominator. Decision 22 keeps the denominator traceable to owner requests; it
  cannot check that the upstream approval was the owner's.
- The link between a request and its line is the line's **text**. Rewrite or split that line — the normal
  way ledgers evolve — and the link is lost, so the closure is never reported.
- The mtime guard is not a lock. It removes the case that happens (a read taken a tick earlier), not the
  microseconds between the check and the rename.
- Evidence is an **observation** (`PlanRowEvidence`): a marker changed and this is what the channel said.
  It is not proof the work happened.
- A config built in code rather than loaded carries `PlanBackend == null` (the Settings window has no
  field for it); nothing is lost on disk, because `Save` never writes the key.
