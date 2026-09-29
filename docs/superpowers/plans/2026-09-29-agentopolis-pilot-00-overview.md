# Agentopolis pilot (M0 + M1) — overview of the three plans

**Spec:** `docs/superpowers/specs/2026-09-29-agentopolis-product-design.md` (approved by the owner
2026-09-29 17:00; decisions D1-D12). Read §3, §4, §5, §6, §10 before any plan.

**Deliverable of the whole set:** Guido's four developers use Agentopolis on their slow data routine,
from the desktop app, on their own Linux server with their own Anthropic API key, and the task card
shows a before/after number measured by the house.

## The three plans and their order

| Plan | What | Who (D3) | Depends on |
|---|---|---|---|
| `…-01-foundations.md` | Repository layout (D4), the Runner API contract, the decision records, CI for the C# side | both | — |
| `…-02-engine.md` | Nathan's kernel: API-key mode, several members with a desk each, Slack optional, the Runner API server, the performance playbook, the customer installer | Nathan | 01 (contract) |
| `…-03-cockpit.md` | The minimal Avalonia desktop app: connect, inbox, task board, threads with streaming, approvals | Manu | 01 (contract); runs against 02's fake mode |

Plans 02 and 03 run **in parallel** once 01's contract is merged: the cockpit is built and tested
against the engine's `--fake` mode and a recorded API fixture, never waiting for a real VPS.

## Conventions that bind every plan

- The engine plan follows the agentopolis repository's own plan style (its `CLAUDE.md`, "Come si
  consegna" and "Il rigore si misura sul rischio"): each point has a goal, the files, and "done when";
  full rigour (mutation proofs, bad cases) only on points marked **risk**; one milestone = one branch,
  one PR, one review; the merge to `master` is the owner's.
- The cockpit plan follows the DVFT suite's `CODING_PATTERNS_QUICKREF.md` (copied into the repository
  in plan 01) and its test conventions.
- **A new library is a question to the owner, never a decision** (agentopolis `CLAUDE.md`). The plans
  are written to need none on the engine side (Server-Sent Events on `node:http`, no WebSocket
  library). The cockpit uses only packages the DVFT suite already uses.
- Files, code, commits and logs in English.

## Decisions these plans take, to confirm at M0 (each is the smallest choice that keeps the pilot moving)

- **P1 — How a developer's PC reaches the Runner.** The Runner API binds to `127.0.0.1:4894` only;
  the cockpit opens an SSH tunnel itself (`ssh -N -L`, present on Windows 10+ and macOS) with the
  member's own SSH key. No new port is exposed on the customer's network. A LAN/TLS listener is
  post-pilot.
- **P2 — Server-Sent Events instead of WebSocket.** Live updates are one-way (server → app); every
  action is a plain `POST`. This needs no new library on either side.
- **P3 — Direct Anthropic API key only in the pilot.** Bedrock and Vertex (D12) break the gateway's
  pass-through design (decision 0027) and the IP-pinned wall; they come after the pilot with their own
  decision record.
- **P4 — Debian 13 VM as the only supported Runner target in the pilot.** `deploy/machine.sh`
  refuses anything else today; Ubuntu and WSL2 have unverified gaps (AppArmor user namespaces,
  bubblewrap overlay version, systemd 257 behaviour, resolver, nft `socket cgroupv2`).
- **P5 — Slack becomes optional.** Guido's team works in the desktop app (D5); Slack stays a
  connector a customer may add.
- **P6 — The TypeScript tree stays at the repository root** and the C# app goes under `app/`, the
  contract under `api/`. Moving Nathan's tree into a subfolder would churn every path, CI job and
  deploy script for no gain.

## What must be learned from Guido before M1's playbook point (not code — a conversation)

1. The routine's language and toolchain. The house's check cage installs dependencies with pnpm only
   (`schemas.ts:373`, `check-runner.ts:441`); a Python or .NET routine needs a setup path added in
   plan 02 point 5.
2. A **representative slice**: a frozen dataset and a command that runs in minutes, not hours (a house
   check is capped at 10 minutes; the dataset is copied into RAM; no database or network in the cage).
3. The equivalence rule: what "same results" means for their output (byte-identical, or within a
   tolerance).
4. Their GitHub organization and the repository to connect; who the four members are.
