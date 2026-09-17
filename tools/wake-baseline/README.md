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

### The boot read — how much of a session's startup read is the app's bookkeeping

A session boots by reading *"`session.json` and every channel file in your home, top to bottom"*
(`kit/skills/supervisor/SKILL.md:131`). That read is the LIVE channel files of one orchestration —
**not** the archive, which is what the compactor exists to keep out of it. So the two readings in this
script disagree about the archive ON PURPOSE, and each says which it is in its own key:

| key | what it is |
|---|---|
| `boot_read_bytes_measured` | byte counts off the disk: live bytes, the app's share of them, the bytes outside any entry, and the archived bytes that are **not** in a boot read |
| `boot_read_bookkeeping_MODELLED` | the 17 `AppNoteKinds` sites plan 02 routes out of the channel, by entry and by byte, plus their share |
| `boot_read_tokens_ESTIMATE_at_3_87_bytes_per_token` | those bytes ÷ 3.87 |

**Why `_MODELLED`.** Which app entries plan 02 routes is decided in C# at the call site by
`AppNoteKinds`, and **nothing in a channel file records which site wrote an entry**. The classification
here is a subject-word heuristic: author `app`, the `[agent]` prefix, and one of 16 family words read
off the constants that produce them (`PrintTurn_Words`, the three `PLAN.md` advisories,
`OrphanEscalation_Decider.Describe_Report`, the five contract-coaching subjects). It is good enough for
a BEFORE figure and must never decide anything at run time. *The plan's own marker list had
`"has gone deaf"` for the orphan report; the writer produces `"<member> may be deaf to wakes"`, so that
marker matched nothing — verified against the constant on 2026-09-17, branch source.*

**Why `ESTIMATE`, and where 3.87 comes from.** There is no tokeniser here. 3.87 bytes/token was
measured on this repo's own channel prose with real `-p` turns; it replaces the 2.8–3.2 an earlier
document estimated, which overstated every derived token figure by 20–38 %. It is one constant,
`BYTES_PER_TOKEN`, and every figure computed through it carries `ESTIMATE` in its key.

**Bytes, not characters.** Channel prose is full of em dashes and accented words; a length measured on
the decoded string understates a UTF-8 channel by several percent. Spans are measured on bytes and
decoded only to match the header.

**One parser, and it is fence-aware.** `read_entries` is the only reader of a channel in this file —
the wake counts and the boot read share it, because two readers of a channel that must agree exactly
is the defect CLAUDE.md decision 12 records. It screens fenced blocks like `ChannelFence_Screen` does,
and **only a CLOSED fence suppresses anything**. For the byte figures this is not a nicety: a
fence-blind reader hands the tail of a supervisor's brief to `app` and inflates the very share this
measures. **The wake columns inherit the screen** — so a re-run on the VPS may come out below plan 01's
6 868, in the direction of removing phantom entries, and the 2026-09-15 figure was taken with a
fence-blind reader.

**It refuses an empty root.** A `--root` that is not a directory, or one with no channel files under
it, exits 2 (CLAUDE.md decision 20). Zeros from a moved supervision home read exactly like a quiet
machine.

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
