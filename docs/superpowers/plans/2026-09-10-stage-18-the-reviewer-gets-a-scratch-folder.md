# stage/18 — the reviewer gets a scratch folder, and mutation testing stops being imaginary

**Date:** 2026-09-10 · **Branch:** `stage/18-the-reviewer-gets-a-scratch-folder` (worktree `../AIOrchestrator-stage18`), from `ours/integration` @ `a67a83b`. · **Scope:** `kit/hooks/reviewer-readonly-check.sh`, its harness, and `kit/skills/reviewer/SKILL.md`. Nothing under `AIOrchestratorCoreLib` is touched, so the .NET suite is not part of this unit's evidence.

**OWNER DECISION (2026-09-10):** a per-session scratch folder outside the repo, at `<supervision root>/<AIORCH_ID>/<AIORCH_MEMBER>/scratch/`, where the reviewer may write freely — so that a mutant of a source file can be written and the suite run against it. Everything outside that subtree stays exactly as denied.

**THE DEFECT.** `reviewer-readonly-check.sh` denied every `FILE_VERB` (`rm`, `mv`, `cp`, `mkdir`, `truncate`, `dd`, …) without ever looking at its target, denied `sed -i`/`perl -i` outright, and allowed a write redirect only as `>>` into the member's own channel folder. So a reviewer had nowhere to put a mutant, and `kit/skills/reviewer/SKILL.md:129` only asked it to *"ask what a mutation of the changed line would do"* — an imagined mutation, which is a weaker instrument wearing the same word. Mutation testing is the detector that caught four tests passing for the wrong reason on these branches when nobody reading the code did.

## What changed (branch source)

| # | Was | Now |
|---|---|---|
| 1 | No containment predicate at all — the only path question the hook could answer was "is this file DIRECTLY inside the member's own folder" (`is_file_in_own_folder`). | `is_in_own_scratch(resolved)` beside it: the same segment identity test (`supervision` / `<orch>` / `<member>`), one segment deeper on the literal `scratch`, and **recursive** — a mutant arrives with an `obj/` folder. It admits the folder ITSELF, because nothing else can create it for the reviewer or clean it up. Paths are expanded from the command line's own assignments and normalised lexically first, exactly as the own-channel rule already does, so `scratch/../..` walks back out and is refused. |
| 2 | `if command in FILE_VERBS: return "files"` — a flat denial, no target inspection. | Target-aware for `rm`, `rmdir`, `mkdir`, `cp`, `mv` only. `cp` is judged by its DESTINATION (the last word) because a copy READS its sources and they may be anywhere, the repo included; every other verb must have **all** its operands contained, since `mv` out of the repo is a repo mutation. `truncate` and `dd` and the PowerShell spellings stay denied wherever they point — the widening is what was ordered and no more. |
| 3 | `cp -t <dest>` would have been read by a last-word rule as a contained write while writing into the repo. | Any `-t`/`--target-directory` refuses. Refused rather than parsed: this guard widens only where it is sure. |
| 4 | `sed -i` / `perl -i` denied outright. | Allowed when **every** file operand is inside scratch — sed rewrites as many files as you give it, so one contained file cannot carry the list. `operands_of()` drops flags and the values they take, off the same per-command tables `is_in_place_edit` already uses; sed's bare first word is subtracted as its SCRIPT unless `-e`/`-f` named one. |
| 5 | `write_target_allowed` gated everything behind `operator != ">>"`. | The scratch clause sits ABOVE that gate, so `>`, `>>`, `&>`, `>|`, `>&` and `tee` (with or without `-a`) all write inside scratch. Below it, nothing moved: the own-channel exemption is still append-only, and the `watch-base` clause is untouched. |
| 6 | Header comment claimed `KitAssets_Installer` overwrites `~/.claude/hooks` at every app start. | Stale since kit/ became a plugin — the hooks are loaded from the checkout (`kit/install.sh:10-14`). Corrected in place; the stub it justifies stays, because a truncated helper still leaves the function undefined. |

Untouched by design: the git denials, the package-installer denials, the `UNDECIDABLE` fail-open posture, the own-channel `>>` exemption, the `watch-base` clause, and every rule about quoting, heredocs, continuations and indirection.

## Two pre-existing reds that had to be closed to reach exit 0

`bash kit/hooks/hook-behaviour-check.sh` was **already failing at `a67a83b`, before this unit's first line** — 250 cases, 2 red. Both block the done-when, so both are fixed here, and both are separable from the feature:

- **`the marker is three lines`** — `hook-log.sh` grew the marker to five lines (hook, predicate, reason, member, fingerprint) at `116ad2f`, "the guard marker says WHO tripped it and WHICH COPY wrote it", and this assertion was left behind. The hook is the truth; the case and the comment above it now say five. **Fails on any machine** — this is not environmental.
- **`an ANSI-C quoted verb is the verb`** — environmental, and worth the paragraph. The hook's reducer is a heredoc INSIDE a command substitution, and bash 3.2 (the `/bin/bash` macOS ships) re-scans that body looking for the closing paren instead of leaving a quoted heredoc alone. The two bytes `$` `"` in `buf[-1] == "$"` read as a locale-translation opener and the dollar was **deleted before python ever saw the line**: the comparison arrived as `buf[-1] == ""`, always false, ANSI-C stripping switched off, and `$'rm' -rf build` was ALLOWED. Measured by teeing the delivered heredoc and diffing it against the file — one line differed. The character is now written `chr(36)`, so no such byte pair exists. The class is the one this kit keeps paying for: silent, and it looks fine.

## Probes

One new harness section, `THE SCRATCH FOLDER: THE ONE PLACE A REVIEWER MAY WRITE FREELY`, in `kit/hooks/hook-behaviour-check.sh` — **42 cases, every ALLOW row with its DENY twin**:

- `mkdir` inside, deeper inside, the root itself; `mkdir` elsewhere denied; a sibling folder beside scratch denied (the member folder is NOT the scratch folder — its channel lives there and is append-only); another member's scratch denied.
- traversal out of scratch, through `mkdir` and through `rm`.
- `cp` a repo file in, `cp -r` a repo folder in; `cp` back OUT into the repo denied, repo→repo denied, `cp -t <repo>` denied.
- `mv` within scratch; `mv` out into the repo denied; `mv` a REPO file in denied.
- `rm -rf` a case folder; `rm` elsewhere denied; `rm` of a scratch file AND a repo file denied.
- `truncate` inside scratch still denied — the verb that was deliberately not widened.
- `sed -i`, `sed -i -e`, `perl -pi` on a scratch file; `sed -i` on a repo file denied; `sed -i` on a scratch file AND a repo file denied.
- `>` and `>>` into scratch; `>` into the repo denied; `>` onto the reviewer's own CHANNEL denied — scratch must not make the channel truncatable.
- `tee` with and without `-a`; a scratch target plus a second file denied.
- a quoted path with spaces; the Windows-backslash spelling; a quoted Windows path outside it denied.
- the variable spelling (`s="<scratch>"; mkdir "$s/case2"`) and a variable naming no scratch folder.
- indirection: `bash -c` carrying a scratch `mkdir` allowed and carrying a repo `rm` denied; `xargs -I{}` carrying a scratch `mkdir` allowed — and `xargs mkdir` with the path arriving on the PIPE still denied, because the reducer sees no target at all and "no operand" must never read as "nothing outside scratch".
- the control the section rests on: `rm -rf build` is still `files`.

**Counted:** 250 cases before (2 red), **292 after, exit 0.**

## Mutation check

Seven mutations, harness run against each, hook restored after every one:

| Mutation | What reddened |
|---|---|
| `is_in_own_scratch` always true | **the harness VOIDS** — its own canary (`rm -rf build` must DENY) allows, and a suite that cannot evaluate its subject refuses to report. Measured directly on the two payloads instead, against the real hook and the mutant, same fixture: `cp <scratch>/Foo.mutant.cs <repo>/Foo.cs` DENY → **ALLOW**, `echo x > <repo>/Foo.cs` DENY → **ALLOW**, `rm -rf build` DENY → **ALLOW**, and `cp <repo>/Foo.cs <scratch>/Foo.mutant.cs` ALLOW either way. |
| **the first attempt at that mutation hit `is_file_in_own_folder`** — the twin's first four lines are identical, and `replace(…, 1)` took the earlier one | every OWN-CHANNEL case, and **not one scratch case**. Kept in this table because it is the finding: the new predicate is close enough to the old one to be mutated by accident, and a scratch case reddening under it would have been evidence about the wrong function. |
| `mv` judged by its destination only | `mv a REPO file into scratch is denied` |
| the `-t` guard removed | `cp -t into the repo is denied` |
| scratch consulted only for appends | `a truncating write into scratch`, `tee into scratch, no -a` |
| `all()` → `any()` in the in-place-edit rule | `sed -i on a scratch file AND a repo file` |
| the `scratch` segment not required (the member folder itself would do) | `a sibling folder beside scratch is not scratch`, `a traversal out of scratch, through rm`, `scratch does not make the CHANNEL truncatable` — and two PRE-EXISTING own-folder cases, `descending below the own folder is not inside it` and `tee WITHOUT -a into its own channel is denied`, which is the harness saying the two rules are still one segment apart |
| `all()` → `any()` in the file-verb rule | `mv out of scratch into the repo`, `mv a REPO file into scratch`, `rm of a scratch file AND a repo file` |

## The skill

`kit/skills/reviewer/SKILL.md` — the bullet that said "ask what a mutation would do" now names the folder, tells the reviewer to copy the file in and break the COPY, and states the limit plainly: **the suite does not follow the copy.** A runner that takes a path can be pointed at the mutant and a script can be run from scratch, but a compiled suite (`dotnet test`) still builds the repo — and swapping the file in the repo to close that gap is not available. The reviewer reports which mutation it ran and how. The repo stays read-only, and that is said in capitals.

## PARKED (found on the way, not in the row — decision 22)

- **The hook does not honour `AIORCH_SUPERVISION_ROOT`.** Both path predicates — the pre-existing own-folder one and the new scratch one — require a literal `supervision` segment, while `hook-log.sh` reads the variable (`b828090`) and `SKILL.md` tells members the root moves whenever the bridge is started with `--root`. Under such a root the reviewer's own channel append is denied and so is its scratch. Pre-existing, out of this unit's scope, and the new predicate deliberately MIRRORS the old one rather than diverging from it — two path rules disagreeing would be worse than one that is wrong in a known way.
- The PowerShell asymmetry the hook's own comment already files (`remove-item`/`new-item` present, `copy-item`/`move-item` absent) is untouched, and the scratch widening does not reach those spellings.
- The `watch-base` clause may still be dead (nothing in this repo reads or writes such a file); it is left exactly as found, as its comment asks.
- **A `$` before a quote inside this hook's heredoc is not safe on bash 3.2.** One instance was found and fixed; nobody has swept the other hooks in `kit/hooks/` for the same byte pair.
