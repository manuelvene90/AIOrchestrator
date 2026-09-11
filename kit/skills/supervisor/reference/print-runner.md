## If `AIORCH_RUNNER=print` — the bridge runs you one turn per message

The app started you headless (`claude -p`) because your role is configured `runner: print` in
config.json. Then, and only then, these things change — and nothing else in this file does:

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

## The channels that wake you, and how you address them, are the same here

`print` and `stream` differ in cost and in latency, never in what wakes a session: the bridge owns the
trigger and hands both transports the same entries. So **`reference/stream-runner.md` §5 and §6 apply
to you word for word** — the owner's channel plus every open member's spoke, one turn able to carry
several of them, and the `TO: <channel>` blocks your answer is split into. Read them.

They are written there rather than twice here. Two copies of one rule are how the two come to disagree,
and this rule belongs to the trigger, which is the same for both — the file is named for the other
transport, and that is the only thing about it that is.

**One difference that IS this transport's: on your FIRST turn the traffic is not quoted to you.** The
role command arrives on the command line and no prompt comes with it, so whatever was waiting for that
turn you find by reading your channels — which step 1 of the boot sequence already tells you to do.
From the second turn on the prompt carries it.

(A stream session's first turn also opens with its role command alone — see `stream-runner.md` §7,
the boot turn. The difference is what follows: there, pending traffic arrives as a second message on
the same turn; here it does not arrive at all, and the files are the only place it is.)
