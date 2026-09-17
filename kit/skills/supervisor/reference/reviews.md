# Reviews — depth, the reviewer brief, and re-reviews

Moved out of `SKILL.md` on 2026-09-15 (the skill diet, spec step 5 of
`docs/superpowers/specs/2026-09-15-one-wake-model-design.md`). **Nothing here was rewritten** —
the blocks below are that file's own words, in their original order. The RULES they carry stayed
in `SKILL.md`; what lives here is the detail, the syntax and the dated account of why.


### Choosing the DEPTH — this is your call, and it is a spending decision

Depth is chosen from **blast radius** — what breaks if this is wrong, and how reversible it is —
never from how big the diff looks. Name it explicitly in the brief; the reviewer will honour it and
report what it actually spent.

| Depth | The reviewer runs | Where it usually fits |
|---|---|---|
| `quick` | nothing — it reads the diff itself | A re-review of a fix, a docs/config edit, a re-check of one earlier finding. |
| `low`, `medium` | `/code-review low` or `medium` | Small, local, easily reverted changes. |
| `high` | `/code-review high` | Ordinary feature work or a bug fix on a branch. |
| `xhigh`, `max` | `/code-review xhigh` or `max` | Engine/algorithm changes, money or order paths, shared libraries; irreversible or safety-critical work. |

**The depth IS the `/code-review` level, and choosing it is yours — any level, the low ones included;
the third column is guidance, not a mapping** (owner, 2026-09-11). The reviewer
spawns no finders of its own — its only fan-out is what the skill does at the level you named — and
the implementer runs no `/code-review` at all, only `/simplify` on its own diff. Before this, every
round paid for two heavy reviews of the same code: the implementer's own pass (127 of 143 calls at
`xhigh`) and then a reviewer with up to nine hand-built finders.

- **A `max` review of a two-line change wastes the owner's money; a `quick` skim of an irreversible
  migration is negligence.** Both errors are yours to avoid.
- **When you genuinely cannot tell, ASK THE OWNER — do not guess.** One message with a `QUESTION:`
  line naming what is being reviewed, plus `OPTION:` lines
  (`OPTION: medium — code-review medium`, `OPTION: high — code-review high`, `OPTION: xhigh — code-review xhigh`)
  costs one tap and is far cheaper than either failure. Say what the work touches and what you'd
  recommend; the owner is paying for the difference.
- **Say the cost in the `reason`**, so the owner sees it on their phone: "deep adversarial review of
  the order-sizing rewrite, /code-review xhigh".
- Expect the reviewer to push back on the depth before it starts. That pushback is the system
  working — take it, and re-decide (or ask the owner) rather than overruling it.

### Briefing a reviewer

Its first entry from you must carry: **exactly what to review** (branch, commit range, files, or
"the diff between X and Y"), **the depth**, **what the work was supposed to do** (it cannot judge
correctness against an unstated intent), and any known-risky areas to attack first. Ask for the
report in its channel; it never talks to the owner.

**A RE-REVIEW BRIEF names the FIX, not the branch** (owner, 2026-09-11). After the first round, the
brief carries the last reviewed commit, the new one, and the findings to check — and the reviewer
reviews `git diff <last>..<new>` plus those findings — usually at `quick`, since the delta is small by
construction, though the level is yours to pick (reviewer, "A RE-REVIEW reviews the FIX"). Three rules, because the rounds
are where review money goes:

- **Never brief an open hunt on a branch already reviewed** — "find the fourth", "find the eighth".
  It sends a fresh reader over the whole branch, and a whole branch always yields another finding:
  `fincanva-3`'s five reviews of FIN-D-282a cost $49 and found 9, 11, 10, 8 and 7.
- **No deep pass on a branch already cleared** unless the fix touches a money, auth or gate path.
  `fincanva-5` spent $18.77 on a fifth pass of FIN-D-279a that found one LOW, and $25.69 on a late
  deep pass of FIN-CLEANUP-2 that found nothing that blocked.
- **Two rounds, then it is your call.** From the third round on the same work, only a CONFIRMED
  finding at HIGH or above that the latest fix introduced sends it back; everything else is recorded
  as a stated limitation or parked, and the line closes. The supervisors of `fincanva-3` and
  `fincanva-5` each had to invent this rule mid-night, under two different names.

### Routing a re-review instead of relaying it — `REROUTE:` (2026-09-15, new here)

A fix round comes back to you twice: once when the implementer reports the fix, once when the
reviewer reports on the fix. The first of those is a HANDOVER, and a handover can be DECLARED when
you write the fix brief instead of performed after it. The declaration is one marker line, and its
argument is the two facts a re-review needs that the app cannot know: who reviews, and from which
commit.

**Write it in the BODY of the fix brief, and write the brief under it:**

```
REROUTE: rev-1 from abc1234
Check F1 and F3 only. F2 was accepted as stated.
The delta is the fix — do not re-read the branch. Depth: quick.
```

It declares: when that implementer answers with `FIXED: <commit>`, hand that reviewer the delta from
`abc1234` and everything written under the line. **Everything under the line is the brief, carried
verbatim** — not parsed, not summarised, not reordered — so what you write there is exactly what the
reviewer reads. Write what you would have written by hand: what this round covers, which findings, at
what depth. Nothing composes a brief for you, and nothing ever will: that is the rule the feature is
built on, not a limitation of it.

The shape, exactly, because every one of these refusals is silent-by-design rather than an error:

- **In the BODY, at the start of a line.** The marker in a SUBJECT declares nothing — there is
  nothing underneath a subject, so there would be no brief, and a contract with no brief is a round
  you did not write.
- **A reviewer that EXISTS** — `rev-1`, `rev-2`, …, named literally. Nothing matches a name to the
  nearest running member.
- **The commit that reviewer LAST reviewed**, 7 to 40 hex characters, because the delta is measured
  from it. HEAD and a branch name are refused: a moving name is not a commit.
- **The FIRST such line wins.** A second one further down is left in the brief, where you put it.
- **Do not also brief that reviewer yourself.** It would get the round twice, and the second copy is
  a whole session's worth of money.
- **`REROUTE: cancel`** retracts the declaration open on that implementer's channel. One is open
  per implementer at a time, so declare one round at a time.
- **Nothing parses, nothing happens.** A line the app cannot read whole is not a declaration: the
  verdict stays ordinary traffic. Same for the ordinary case — **no `REROUTE:` line means the round
  works exactly as it always has**, which is what almost every round will keep doing.

**It moves the legwork and never the verdict.** The reviewer's findings remain input to YOUR verdict
(`SKILL.md`, "Governance — do not spend the reviewer's independence"), the re-review rules above are
unchanged, and the two-rounds rule still counts these rounds. **The round is yours until you have
written that re-verdict** — so if a re-review does not come back, pick the fix report up yourself,
exactly as you did before this line existed.
