## If `AIORCH_RUNNER=print` — the bridge runs you one turn per message

The app started you headless (`claude -p`) because your role is configured `runner: print` in
config.json. Then, and only then, four things change — and nothing else in this file does:

- **Do NOT arm the Monitor/watcher below.** There is no idle session to wake: the bridge starts a
  new turn of yours for every entry addressed to you, and the turn ends when you stop.
- **Do NOT append your own entries with `channel-append.sh`.** Your final message IS your entry:
  the bridge appends it under your author word, with the header, the `[n]` and the time. Write it
  as first line = subject, a blank line, then the body. Your boot greeting is not a separate
  append — if you have no task, your final message is that greeting; if you have one, it is your
  reply to it.
- **A question ends the turn** exactly as an answer does; the reply arrives as your next turn.
- **SAVE YOUR PROGRESS AS YOU GO, not only at the end.** After every part of the review you have
  finished, append ONE line to
  `"${AIORCH_SUPERVISION_ROOT:-$HOME/.claude/supervision}/$AIORCH_ID/$AIORCH_MEMBER/progress.md"`:
  what you have reviewed, what you found there, and what is next. Append with `>>`, never rewrite
  the file — and it is the ONE file you write, since a review changes nothing in the repo. A turn
  can be cut — by the silence brake, the loop detector or the ceiling — and your final message is
  the only entry a print turn gets, so this note is the only record of how far you got. Your next
  turn starts fresh, and its pack hands the note back under "Your progress note": resume from it
  instead of reviewing the same files again (research for the owner, 2026-09-11).
- **Never write that final message while a background sub-agent is still running** — wait for every
  agent to return first, because a late return re-opens the turn and a later message replaces the
  entry. (The bridge files the superseded one for you and tells you it did; the entry your
  counterpart reads is still the LAST thing you said.)
