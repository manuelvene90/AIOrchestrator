# Request files — the exact JSON the app executes

Moved out of `SKILL.md` on 2026-09-15 (the skill diet, spec step 5 of
`docs/superpowers/specs/2026-09-15-one-wake-model-design.md`). **Nothing here was rewritten** —
the blocks below are that file's own words, in their original order. The RULES they carry stayed
in `SKILL.md`; what lives here is the detail, the syntax and the dated account of why.

**Where its "below" points:** "See 'Reviewers' below" means the REVIEWERS section of `SKILL.md`,
and what a reviewer's brief must carry is in `reference/reviews.md`.


## Add a reviewer, retire a member, mute Telegram, switch the model

- **Add a REVIEWER:** same shape, `{"action":"add-reviewer","orchId":"$ARGUMENTS","reason":"<why, one line>","model":"sonnet|opus"}`.
  You get back `rev-1`, `rev-2`, … — reviewers number separately from implementers. Brief it in
  `rev-<n>/channel.md`. See "Reviewers" below for what to put in that brief.
- **Retire an implementer or a reviewer:** first tell it to wrap up in its channel and wait for its
  final report; then drop
  `{"action":"close-implementer","orchId":"$ARGUMENTS","memberId":"imp-<n>","reason":"<why>"}`
  (the same action closes a `rev-<n>` — pass its member id).

## Do-Not-Disturb

- **Do-Not-Disturb:** if the owner asks you (by text) to stop texting them, drop
  `{"action":"set-telegram-muted","muted":true}` — this pauses ALL app→owner Telegram traffic
  suite-wide until the owner texts again (auto-unmute) or re-enables. Keep working normally:
  your channel entries queue up and reach the owner in one catch-up burst on unmute.

## Model switch for this orchestration

- **Model switch for THIS orchestration** — when the owner says "use fable for this" (or wants a
  different model for the implementers here), drop
  `{"action":"set-model","orchId":"$ARGUMENTS","role":"supervisor|implementer","model":"fable","reason":"<why>"}`.
  It is a PER-ORCHESTRATION override, never a defaults change. The app respawns the affected
  sessions on the new model — for role "supervisor" that means YOUR terminal restarts within
  seconds and you resume from the channels; expect it, don't fight it.

## Name the orchestration — `set-orchestration-name`

As soon as the goal is clear from the owner's first instruction, drop
`{"action":"set-orchestration-name","orchId":"$ARGUMENTS","name":"<2-4 words, 3 is best>"}` in
`$AIORCH_SUPERVISION_ROOT/.requests/` — it renames the app card and the Telegram topic (e.g.
"CRM invoice crash").

## The platform codes every topic name starts with

`SL` Strategy Lab · `AS` Arb Studio · `OL` Option Lab · `SK-C` Skeleton Client · `SK-M` Skeleton Master · `AI-Orch` AI Orchestrator · `SS` Seasonal Studio · `ODP` Option Database Preprocessor · `UPD` Updater · `CRM` CRM · `TKT` Tickets · `SB` Strategy Builder (in SL) · `NO` Noise Adder (in SL) · `DA` Data Analyzer (in SL) · `PB` Portfolio Builder (in SL) · `IS` Invest Studio (in SL) · `TKL` Tracker (in SL) · `API` Trading System Bridge (in SL)
