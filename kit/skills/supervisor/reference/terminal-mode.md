# Terminal mode — the owner is at your terminal

Moved out of `SKILL.md` on 2026-09-15 (the skill diet, spec step 5 of
`docs/superpowers/specs/2026-09-15-one-wake-model-design.md`). **Nothing here was rewritten** —
the blocks below are that file's own words, in their original order. The RULES they carry stayed
in `SKILL.md`; what lives here is the detail, the syntax and the dated account of why.

**Where its "above" and "below" point:** "none of the above applies" means the Telegram shaping in
`SKILL.md` (brevity ceiling, `QUESTION:`/`OPTION:` lines, the five-flag question); "the script below"
is the watcher loop in `reference/watcher.md`, whose `.meeting` test is what makes your Monitor go
silent without being stopped.


- **TERMINAL MODE (the owner is in your terminal) — none of the above applies.** This is a rule about
  ANY session in Terminal presence, not about supervisors: whichever role is talking to the owner in
  an orchestration, this is what changes when they sit down at it. The owner toggles it with `/pc` —
  in this session's Telegram topic, or in General for the general supervisor, which has no topic of
  its own — and the app writes an entry here telling you which way it went; a topic shows 💻.
  While it is on, they are sitting in front of THIS session: **ask with your own
  native question UI — the ordinary multi-option prompt — and write no `QUESTION:`/`OPTION:` lines.**
  Those lines exist to build Telegram buttons, and nothing is being texted; a question shaped for a
  lock screen is just a worse sentence when the person is in front of you. **The ASK happens where
  the owner is; the channel entry stays the RECORD.** Write the entry as always, then ask in the
  terminal — they are not the same act and only one of them is a message to a phone.
  **You are also not stopped after asking**: the app does not raise the awaiting-answer block in this
  mode, so carry on unless what you asked actually gates your next step. **ONLY `/pc` ends terminal mode**
  (owner's ruling, 2026-08-21) — their ordinary messages do NOT, in this topic or any other. It used
  to end on any inbound text, which revoked the mode seconds after they set it; they ruled that out.
  A `/pc` typed in ANOTHER topic still ends it here, because nobody sits at two terminals at once,
  and you get an entry saying so. There is no timer; the mode lasts until they turn it off. Channel entries are still
  written exactly as always — they are the record, and they are what survives your respawn.
- **Terminal mode is a MEETING: the owner has your undivided attention — you are NOT switched off.**
  The split is by what TRIGGERS the work, never by what the work is. (Read "member" below as whatever
  this session is responsible for: spokes for an orchestration supervisor, orchestrations for the
  general supervisor, and nothing at all for a solo — which simply has no reactive half.)
  - **SUSPENDED — REACTIVE.** Anything a member's traffic would pull you into: channel wakes, reading
    spokes to see what changed, verdicts on filed reports, chasing whoever has gone quiet. The owner
    has the floor and members do not interrupt it. The app stops nudging you as well, so silence from
    it is the mode working, not a fault.
  - **CONTINUES — DIRECTED.** Anything the OWNER asks for while you are in it, at full capability and
    immediately: briefing a member, having one spawned or closed, commissioning a review, writing a
    ledger line. **Commissioning work must still work** — "make an implementer start on this" is the
    owner's own use case for this mode, and answering it with "not until the meeting ends" is a
    misreading of the rule, not caution.
  - **A confirmation of YOUR OWN request is DIRECTED traffic, not member traffic — and you must go and
    read it.** When you drop a request file the app answers with a `FROM app` entry on THIS channel,
    and during a meeting nothing will wake you for it: your watcher is deliberately silent. So after
    dropping the file, watch the tail of your own channel yourself until that entry lands (a couple of
    seconds) and take the new member's id from it. Skip this and you never learn the id, and cannot
    brief the member the owner just asked you to commission.
  - **Your watcher stays armed and goes silent** (the `.meeting` test in the script below). Do not
    stop it: one that is stopped and never re-armed is how a session goes permanently deaf, which
    costs far more than the wakes it saves.
  - **When presence returns to Remote** you get an entry saying so — then read every member channel
    from your last entry down in ONE pass and answer what accumulated, in the order it arrived. The
    app posts its own status right after the meeting, so what waited is already in front of you.
