# Investigation — do background watchers die and orphan sessions? (2026-08-07)

Subject: `imp-1` of `da-vinci-fintech-suite-2` sat alive-but-unreachable for ~50 minutes and was
revived only by a human typing into its terminal.

Method note honoured: **no conclusion below rests on a session's own account of its liveness.**
Everything comes from transcripts, file mtimes and the app's source.

## The finding — it was NOT a watcher that died

The tail of imp-1's transcript (`3000d7af-1ae1-4ca8-b5d9-f886249f873e.jsonl`) ends:

```
assistant  "All three suites green. Committing the item 6 contract/engine half."
assistant  Write(<temp>/COMMIT_MSG_TEMP.txt)
user       tool_result — file written
           <nothing>
"Wake up"  ← the human
```

The session stopped **mid-turn, right after a tool result**, with a commit half-finished. It never
reached the end of its turn, therefore it never reached the point where a watcher is armed. There
was no listener because none was ever created — which is exactly what the owner saw at the
terminal.

This also disproves imp-1's own story (it claimed the Stop hook had it doing `/style-check` work
during the gap; the transcript shows it mid-commit, and no `/style-check` appears in that window).
CONFIRMED by transcript.

## Answers

1. **Can the app kill a background shell of a still-running session?** Only through deliberate
   termination. `SessionTerminator.Kill_SessionTree_ByPidFile` is the single kill path
   (`SessionTerminator.cs:17`) and its only callers are set-model respawn
   (`BridgeEngineModel.cs:983,996`), `close-implementer` (`:1192`), `close-orchestration`
   (`:1219`) and app exit (`Kill_AllSessions`). The watchdog never kills — it only spawns.
   CONFIRMED by code.

   One latent hazard found and fixed: that kill had **no pid-recycling guard** (the watchdog's
   liveness check has one). A stale pid file whose pid Windows had recycled could have killed an
   unrelated process tree. It now refuses to kill anything that is not a PowerShell session host.

2. **Does ADDING an implementer kill existing sessions' shells?** REFUTED, twice over. `Add_
   Implementer → Respawn_Implementer` contains no kill (`OrchestrationLauncherModel.cs`), and the
   timing disagrees: imp-3 spawned 10:43 local (its `.pid` mtime), while imp-1's transcript kept
   growing until 10:53 and only then stopped.

3. **Was imp-3's watcher dead or blind?** BLIND, per the companion report: its baseline was
   computed after the supervisor's overwrite, so `count > base` could never become true. That is
   a different bug (fixed: content fingerprints + baseline captured before reading).

4. **Is "alive + idle + no listener" detectable without false-positiving a long operation?** YES,
   and it is now implemented. The insight is that **the nudge is the probe**: the app appends a
   `FROM app` entry, which CHANGES the channel. A live watcher fires on that within seconds. So
   the honest test is a conjunction, not a timeout:
   - the session's transcript is frozen (not mid-turn), AND
   - its channel changed after that freeze (our nudge), AND
   - it still has not reacted `ORPHAN_CONFIRM_MINUTES` later.

   A session running a long build, a big read or a hook keeps its transcript growing, so it never
   satisfies the first condition — the false positive that fooled both the supervisor and the
   owner about imp-1 cannot arise.

5. **Should wake-up be the app's job?** Partly, and now it is. The app cannot inject a turn into a
   running CLI, but it does not need to: once a session is proven orphaned, the app kills and
   respawns it. Files and channel survive; the role command's boot re-reads the channel, which is
   the designed durable state. In-conversation context is lost, which is why this only runs after
   the probe has failed, and why the owner is told it happened.

## Not established

- **Why imp-1 stopped mid-turn.** The transcript shows the model received a tool result and never
  produced the next message. Whether that was an API error, a usage-limit pause or a harness
  hiccup is not visible from the app's side.
- **The two `killed` background tasks** (`bl54eknpa`, `bpqk9emcs`). Nothing in this repo can kill
  them, so the cause is in the Claude Code harness. The recovery above makes it survivable
  regardless of cause, which is the point.
