# Git and boundaries of this fork

This is `nathanthegrey/AIOrchestrator`, a fork of `manuelvene90/AIOrchestrator` (upstream, read-only). Nathan owns the fork; Manu owns the origin.

- **`master` = Manu's master, untouched.** Never commit on it; sync it only with `git fetch upstream && git merge --ff-only upstream/master`.
- **`ours/integration`** is our version (the fork's default branch): everything we use runs from here. **`stage/<n>[letter]-<topic>`** branches are the packages to show Manu, one per stage, each kept mergeable onto `master` on its own. A fix that belongs to a stage is cherry-picked back onto that stage branch.
- **Standing exception (Nathan, 2026-09-06): pushing `ours/integration` and any `stage/*` branch to `origin` needs no approval.** `master` of the fork and anything toward `upstream` do. This never extends to another repository.
- Work in a worktree per session (`git worktree add ../AIOrchestrator-<name> -b <branch> ours/integration`); never `git checkout` another session's branch; remove the worktree only after its merge is confirmed. Stage explicit paths, never `git add -A`/`.`; never `--no-verify`.
- Commits in English, `type(scope): a descriptive clause`, body with the why and dated evidence. One commit per defect or per concern.
- Do not modify `docs/investigations/`, `HANDOFF.md` or `CLAUDE.md` — they are Manu's; a fact for him goes in the report. Never contact the upstream author from a session (no issues, no PRs, no messages): that conversation is Nathan's.
- The local copy `/Users/nvene/Visual Studio/AIOrchestrator-master` is read-only reference; the VPS (`orch@…`) pulls `ours/integration` — never patch it by hand.
- Before claiming done: full suite green (0 red, 9 skipped — compare the names, not the count), trial merge into `ours/integration`, report with commands and outputs. A red is a signal, not an expected cost: the six macOS file-lock cases became honest skips at `b2dde29` and the lock-diagnostics intermittent was fixed on 2026-09-09.
