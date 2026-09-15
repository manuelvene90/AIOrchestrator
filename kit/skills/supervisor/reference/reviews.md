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
