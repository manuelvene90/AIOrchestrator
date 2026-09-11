# token-gate — read-only token measurement

One Python 3 script (stdlib only), `token_gate.py`, run against an installation's
`~/.claude/supervision` and `~/.claude/projects`.

It **reads metadata only** — `usage`, timestamps, model, ids, tool NAMES and the file-path or
command argument of a tool call. Never the text of a message. It **writes nothing**: run it over
stdin so it leaves no file on the box.

```
ssh orch@<vps> 'python3 - tokens --group-by day'                      < tools/token-gate/token_gate.py
ssh orch@<vps> 'python3 - tokens --group-by role --since 2026-09-10'  < tools/token-gate/token_gate.py
ssh orch@<vps> 'python3 - turns  --since 2026-09-08 --until 2026-09-09' < tools/token-gate/token_gate.py
ssh orch@<vps> 'python3 - mix    --since 2026-09-10'                  < tools/token-gate/token_gate.py
```

## The three classes — never one number

A raw sum of the four `usage` fields is not a quantity. Measured 2026-09-10 on the VPS, **97 % of
it is `cache_read`**: context the model was already given, passed again. The three are reported
apart and never added:

| class | fields | what it is |
|---|---|---|
| `new` | `input_tokens` + `cache_creation_input_tokens` | text processed from scratch this call |
| `reread` | `cache_read_input_tokens` | identical to the previous request |
| `out` | `output_tokens` | answer + thinking |

`ctx` = `new + reread` is the context a call carried, and is the metric the fresh-turn work moves.
A cache miss does not change `ctx` — it moves tokens from `reread` to `new`.

## Commands

| command | answers | source |
|---|---|---|
| `tokens` | the three classes, calls, context per call and new per call, grouped by `--group-by` (day, hour, role, member, orch, model, project); plus the 5-minute / 1-hour cache-write split | transcripts, deduped `(sessionId, requestId \| message.id)` |
| `turns` | per turn and per role: calls per turn, **first-call context and first-call new** (`usage.iterations[0]` — the turn's cold start), context per turn, output per turn, duration, errors | `turns.jsonl` and `.supervisor.turns.jsonl` |
| `mix` | production tool calls (Edit/Write/NotebookEdit + `git commit`) vs orientation (Read/Grep/Glob/other Bash) vs `Agent`, per role | transcripts, tool names + the Bash command argument |

Every command takes `--since` / `--until` (any ISO prefix), `--orch`, `--supervision-root`,
`--projects-root`. Nothing about a window, a role or a divisor is compiled in.

## What this replaced, and why (2026-09-10 review, finding F5)

`gate_delivery.py` and `gate_turns.py` answered a real question and could not be reused. Six
defects, each of which changed the answer:

1. **The three classes were summed into one**, so `cache_read` — 97 % of the total — set the
   result. The published −23 % was a movement in that sum.
2. **The cut instant was compiled in, in two incompatible spellings** —
   `"2026-09-08T20:34:00"` in one file and `"…Z"` in the other, both string-compared against ISO
   transcript stamps. `norm_ts()` now normalises once, at the boundary.
3. **Supervisors came from a hardcoded allowlist of two session ids**, so a third supervisor was
   invisible. The app has always written the answer to disk: `<orch>/.supervisor.turns.jsonl`, a
   DOTTED file at the orchestration level, which a `*/*/turns.jsonl` glob cannot see. That is why
   the allowlist existed. `build_role_map()` reads both shapes. On 2026-09-08 this moves 423 calls
   at 539 k context each out of "unattributed" and into `supervisor`.
4. **Every member that was not an implementer was declared a reviewer** —
   `def role(m): return "imp" if m.startswith("imp") else "rev"`. `ROLE_BY_PREFIX` is explicit and
   anything unrecognised keeps its own name and is counted.
5. **The per-item divisors were eyeballed and inline** — `23` with the comment
   `# fincanva-1..4 lines closed before: 5+22+1+0=28? use fincanva-2+3 = 23`, and `7`. `mix` prints
   per-item rates only when `--items` is passed: a divisor is a measurement and belongs to whoever
   counted it.
6. **No 5-minute / 1-hour cache-write split**, which the transcripts carry.

**And one this rebuild nearly repeated:** a first draft printed a "first call" column in `tokens`.
Windowed transcript data cannot tell a session's cold start from the first call that happens to
fall inside the window, and the column read a median of 0 — a mid-session call is a full cache hit.
It was removed rather than qualified. `turns` reads `usage.iterations[0]`, which is the turn's real
first call, and is where that number belongs.

## Known limits

- `num_turns` in the CLI result over-counts API calls by ~1.5–2× (measured 2026-09-09), so
  `calls/turn` from `turns` is an upper bound; the transcript call count in `tokens` is the one to
  trust.
- `result.usage` excludes sub-agents and resets at background-task notifications. Anthropic's own
  cost-tracking guidance says the same: the top-level `usage` undercounts as soon as nesting
  occurs. Sub-agents are counted here from their own transcripts, under the `subagent` role.
- **A day in progress is not a day.** Any window ending "now" is partial and its totals will keep
  growing; compare matched windows, never a full day against a running one.
- **Comparing two live periods does not control for task mix.** Context *per call* within one role
  at a constant model removes volume and model, not the mix. The only design that removes it by
  construction is the same task set run under both configurations.
