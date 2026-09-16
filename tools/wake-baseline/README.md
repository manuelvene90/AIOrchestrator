# wake-baseline

Measurement for the one-wake-model series. **Aggregates only** — nothing here ever prints a whole
transcript, a whole channel, or any message text. Safe to run read-only against a live machine.

## `baseline.py` — what wakes a session (measured), and what would not (MODELLED)

**The two columns are not the same kind of number.** `..._measured` counts entries that exist and
that the fingerprint monitor fired on. `..._MODELLED` applies the app's policy to those same entries
and says what WOULD have woken a session: this script never reads `orchestrator.log.jsonl` and has
never seen a ticket. The gap is a PREDICTION. The "after" is a different measurement.

**Counting real tickets afterwards: do not grep `wake ticket <n>` naively.** Two different lines
carry that phrase — the emission (`'imp-1': wake ticket 5 — <reason>`, info) and the stall warning
(`'imp-1' was handed wake ticket 5 and has filed no entry…`, warning). Counting both doubles the
figure.

**And check the key actually took.** An unknown or misspelled `wake` value falls back to `watcher`
without an error, by design — so `"tickt"` reads two days later as numbers identical to the before,
and the honest reading of that is "the model does nothing". Confirm the mode is live before starting
the clock.

    python3 tools/wake-baseline/baseline.py --root ~/.claude/supervision

Two readings of the same entries: what wakes a session under the fingerprint watcher (every
author, the app's own bookkeeping included) and what wakes it under the app's policy. The gap
— `wakes_avoided_by_the_ticket` — is what the series moves. Reads live files **and archives**:
`Channel_Compactor` archives from the front, so a live-file-only count is not monotonic
(CLAUDE.md decision 13) and undercounted by 2.6× when this script first ran.

Re-run after each step and compare.

Measurement for the one-wake-model series. **Aggregates only** — nothing here ever prints a whole
transcript, a whole channel, or any message text. Safe to run read-only against a live machine.

## `context_share.py` — how much of a call's context was already paid for

    python3 tools/wake-baseline/context_share.py --projects ~/.claude/projects

Per role: `calls`, `mean_input_tokens`, `mean_inherited_tokens`, `inherited_share`.

`inherited_share` = `cache_read / (cache_read + input + cache_creation)` — of everything the model was
handed, the fraction it had already been handed on an earlier turn. **`cache_creation` counts as new**,
because a call that writes the cache paid for those tokens then and there.

Calls are deduplicated by `message.id` **across every file**, not per file: a resumed session replays
its earlier assistant messages and a sidechain repeats them again elsewhere (token-efficiency spec C7).

`--role-of <file name>=<role>` attributes a transcript to a role; anything unnamed lands in
`unattributed`. Without a session→role map — which this repo does not yet keep — a whole-machine run
is one `unattributed` bucket mixing every session on the box, which is a real number about a different
population than "the supervisor". Say which you ran.

**It exits 2 on a `--projects` path that is not a directory** rather than reporting zero calls: a
harness that cannot find what it measures must not certify its absence (CLAUDE.md decision 20).

Run it before and after any change to the resume regime and record both, with the machine and the
window named.

## Tests

    python3 -m pytest tools/wake-baseline/test_context_share.py -q
