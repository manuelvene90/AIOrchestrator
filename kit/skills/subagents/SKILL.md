---
name: subagents
description: Use whenever you are about to delegate — spawn a sub-agent, fan out reads, run parallel workers, or choose a model/effort for a task — and whenever a turn-economy question comes up (how many calls, how to batch). Calibrates model and effort per task, sets the fan-out rules, and keeps git writes in the main session.
---

# Sub-agents, model and effort, turn economy

## Model and effort — a judgement on EVERY call, never inherited

- Omitting `model` makes the sub-agent inherit the parent session's model. That is how an expensive model ends up doing a grep. Passing the same model every time is the same mistake with extra steps: decide each time.
- `effort` defaults to `high` when omitted, so leaving it out is itself an uncalibrated choice.
- Calibrate to what the task actually is:
  - mechanical — grep, file reads, inventories, mapping → Sonnet or Haiku, effort `low`
  - analysis, cross-checking, recon needing judgement → Sonnet or Opus, effort `medium`
  - hard synthesis, adversarial verification, design → Opus, effort `high`
  - In doubt, go one tier up on the model, not on the effort.
- Fable is forbidden everywhere — never select it for a sub-agent. The only way Fable runs is because Nathan asked for it or chose it as the session model. If a task looks like it wants Fable, ask. Why: ~2× Opus and ~5× Sonnet, thinking cannot be disabled (even a grep pays for reasoning), 30-day data retention, and its safety classifiers can return an empty refusal with HTTP 200 and no error.

## Fan-out — reading is parallel by default, writing is disjoint

- Reading fan-out is the default for whoever does the work: explore, hunt call sites, read docs, run independent suites — in parallel, with different lenses or targets. N identical agents find the same thing N times.
- Parallel writers only on disjoint, named file sets, stated in each agent's prompt. Environment files (project files, DI, shared constants) stay with whoever coordinates the fan-out, never assigned to a unit.
- No sub-agent runs git that writes — no `add`, `commit`, `stash`, branch or merge operations. Staging and commits stay with the main session. Reading (`log`, `diff`, `rev-parse`) is free.
- If you coordinate other sessions and your turn is Nathan's only channel, do not block it with a sub-agent for work longer than minutes: delegate to a separate session. If you are doing the work yourself, your turn is meant to be busy.
- A sub-agent's report is a claim: before reporting, read the real diff, run the suite yourself, count the tests yourself. Never forward an agent's summary as your own result.

## Turn economy

- Cost = context size × number of turns. Every turn re-bills the whole accumulated context, so what matters is the number of round-trips, not the output size of any single command.
- Chain related steps into one call: verifications as `a && b && c` (stops at the first failure exactly like the sequence — no signal lost); git inspection and commit steps batched into one opening turn and one closing turn.
- This never means verifying less. Verification depth is not a token lever.
- A screenshot stays resident and is re-billed on every later turn: never a screenshot sweep.

## Defaults per role, choice per task

The bridge's config gives only a DEFAULT model per role — `supervisorModel`, `implementerModel`,
`communicatorModel`, `generalSupervisorModel` (read in `OrchestratorConfig_Loader.cs`, one key per
role; there is no `reviewerModel` or `soloModel` — those roles ride the implementer's default). That
default is a floor for how the role's own terminal session is spawned, not a ceiling on what it asks
for: when a supervisor requests a member for a task, IT picks the model for that task the same way
this skill calibrates a sub-agent above — mechanical, well-specified work → Sonnet; judgement,
adversarial review, design, or anything touching a money path → Opus; Fable never, the same rule as
everywhere else. The config default is what a role starts as; the task in front of it decides what it
asks for next.
