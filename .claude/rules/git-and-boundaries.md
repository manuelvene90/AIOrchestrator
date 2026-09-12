# Git and boundaries

This is `manuelvene90/AIOrchestrator` — the owner's own repository, and the only one. It is no longer
a fork checkout: the work developed on `nathanthegrey/AIOrchestrator` was merged here on 2026-09-11
(CLAUDE.md decision 26, `docs/MODIFICHE-DEL-FORK.md`), and everything below that read as a fork's
boundary is marked as retired rather than quietly deleted, so a session reading an old report can
tell which rules were dropped and why.

- **`master` is the owner's branch.** Work on a topic branch and merge into it; the merge itself is
  the owner's call, asked for explicitly.
- **`integration/fork-merge`** carries the merge of the fork's work and is what plan 01 builds on.
- Work in a worktree per session (`git worktree add ../AIOrchestrator-<name> -b <branch> <base>`);
  never `git checkout` another session's branch; remove a worktree only after its merge is confirmed.
- **Build in the worktree you are working in, never in the main checkout** — and never start the WPF
  host from a session: the running app is the bridge that spawned the session (CLAUDE.md decisions
  17, 23).
- Stage explicit paths, never `git add -A` / `git add .`; never `--no-verify`. Multi-line messages via
  `git commit -F <tempfile>`.
- Commits in English, `type(scope): a descriptive clause`, body with the why and dated evidence. One
  commit per defect or per concern.
- Before claiming done: the tests you touched and their neighbours green, a named account of any red
  you did not cause, and a report with the commands and their output. A red is a signal, not an
  expected cost. Known reds are recorded per plan — at the time of writing, the four
  `OrchestratorConfigFactoryTests` cases are a ruled exception for plan 01, and the file-lock /
  wall-clock family under `Bridge/` is a known flakiness campaign (plan 03), not a regression.

## Retired with the merge — these no longer apply

- **The upstream/fork split.** There is no `upstream` remote to `--ff-only` from, no `ours/integration`
  default branch, and no `stage/<n>-<topic>` packages "to show Manu": those existed because the fork
  had to hand work across a repository boundary. Work now lands on branches of this repo directly.
  (A local `fork` remote may still point at a scratch clone used for the merge; it is history, not a
  destination.)
- **"Never contact the upstream author."** That was the fork's boundary with this repo's owner. The
  owner of this repo is the person the session reports to.
- **"Do not modify `CLAUDE.md`."** It was off-limits to the fork because it was the other side's file.
  Here it is this project's context store and is edited when a decision changes — on the owner's
  instruction, and stating what the tree actually contains (CLAUDE.md decisions 11 and 25 were both
  rewritten this way). `docs/investigations/` and `HANDOFF.md` are likewise ordinary files of this
  repo; touch them when the work is theirs, not as a side errand.
- **The macOS/VPS notes** (a read-only `/Users/nvene/…` reference copy, an `orch@…` VPS pulling
  `ours/integration`) described the fork's machines. This repo's CI is the two-OS GitHub workflow;
  the owner's machine is Windows.

Nothing above changes the rules that are this project's and not the fork's: the parked-work
discipline (decision 22) and "say which copy you read" (decision 18).
