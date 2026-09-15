# Briefing an implementer — parallel units and worktrees

Moved out of `SKILL.md` on 2026-09-15 (the skill diet, spec step 5 of
`docs/superpowers/specs/2026-09-15-one-wake-model-design.md`). **Nothing here was rewritten** —
the blocks below are that file's own words, in their original order. The RULES they carry stayed
in `SKILL.md`; what lives here is the detail, the syntax and the dated account of why.


## The `PARALLEL UNITS` block

An implementer can fan out to parallel agents, but it can only parallelise what you handed it as
parallelisable. When you can see a task's independent units, say so in the brief:

```
PARALLEL UNITS (proposal — verify before you dispatch):
- unit A: <what> — files: <paths>
- unit B: <what> — files: <paths>
shared/after: <files only the implementer touches, once the units return>
```

- **It is a PROPOSAL, and say so in those words.** You brief lean and have not read the code, so your
  file sets will sometimes be wrong. The implementer verifies them, collapses the split to sequential
  when the units actually overlap, and tells you why. Being refuted there is the system working.
- **Two units that share a file are ONE unit.** If you cannot name a disjoint file set, do not invent
  one — write the brief sequentially and let the implementer find the split from the code.
- **No `PARALLEL UNITS` block is perfectly fine.** Most tasks are one unit. An invented split is
  worse than none: it costs the implementer a verification pass just to reject it.

## Worktrees — one per implementer, and who points them at it

- **Two implementers must never share a working tree** unless you explicitly coordinate their
  windows — the default is one worktree per implementer (`git worktree add ../<repo>.worktrees/<orch-id>-<member>` or
  the repo's established worktree convention if it has one; check before inventing).
- Spawned implementer terminals start at the REPO ROOT. Your brief must direct each implementer
  into its assigned worktree as its first action, and name the branch it works on.
