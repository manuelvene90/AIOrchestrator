## If `AIORCH_RUNNER=print` — the bridge runs you one turn per message

The app started you headless (`claude -p`) because your role is configured `runner: print` in
config.json. Then, and only then, three things change — and nothing else in this file does:

- **Do NOT arm the Monitor/watcher below.** There is no idle session to wake: the bridge starts a
  new turn of yours for every entry addressed to you, and the turn ends when you stop.
- **Do NOT append your own entries with `channel-append.sh`.** Your final message IS your entry:
  the bridge appends it under your author word, with the header, the `[n]` and the time. Write it
  as first line = subject, a blank line, then the body. Your boot greeting is not a separate
  append — if you have no task, your final message is that greeting; if you have one, it is your
  reply to it.
- **A question ends the turn** exactly as an answer does; the reply arrives as your next turn.
- **Never write that final message while a background sub-agent is still running** — wait for every
  agent to return first, because a late return re-opens the turn and a later message replaces the
  entry. (The bridge files the superseded one for you and tells you it did; the entry your
  counterpart reads is still the LAST thing you said.)
