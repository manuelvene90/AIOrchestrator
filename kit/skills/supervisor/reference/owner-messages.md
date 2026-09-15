# Writing to the owner — questions, bounds, buttons, pictures and files

Moved out of `SKILL.md` on 2026-09-15 (the skill diet, spec step 5 of
`docs/superpowers/specs/2026-09-15-one-wake-model-design.md`). **Nothing here was rewritten** —
the blocks below are that file's own words, in their original order. The RULES they carry stayed
in `SKILL.md`; what lives here is the detail, the syntax and the dated account of why.


## A question owes five things — the worked call and the full semantics

- **A question to the owner owes FIVE things, and the tool REFUSES to write one that is missing any
  of them.** You pass them as flags — you never type the syntax:

  ```bash
  channel-append.sh --channel "…/owner-channel.md" --to owner \
    --subject   "the merge gate" \
    --question  "Merge branch wf-perf into master now, or hold for your IDE review?" \
    --option    "Merge it" \
    --option    "Hold" \
    --recommend "Hold — you asked to read every merge to master first." \
    --risk high \
    --row FIN-D-277a
  ```

  `--question` — one short, self-contained question (≤2 lines, ideally one). The app sends your body
  first and puts the buttons on their OWN message carrying only this, so it has to stand alone.
  `--option` — repeatable, two to four, spelled out; one option is not a choice and a fifth makes it
  a list. `--recommend` — what you would do and why, one line; it is printed with the question,
  because a recommendation the owner has to scroll back for is a decision deferred. `--risk` —
  `high` or `low`; high means a tap is not enough and they type back a 4-digit code. `--row` — the
  plan row this decision belongs to, or the word `none`, written out.

  **Missing a line? The body still reaches them, the question does not, and you get one entry in
  this channel naming every line you left out.** Nobody will answer a question that was never sent,
  so re-ask with all five. There is no fallback and no derived question any more: the app used to
  mine your last sentence ending in "?" and, failing that, show a bare "Your call:" over the
  buttons — both were rescues of a malformed question, and the rescue is why nobody fixed the shape.

  **`RISK: low` does not unlock anything.** The app ALSO locks any question whose text or options
  name a push, a deploy, a release, production or a destructive command. Your declaration can only
  ever ADD a lock.

## Bounding a question — `--deadline` and `--default`

- **A question that can wait for ever usually does. Bound it: `--deadline` and `--default`.** Two
  more flags on the same call:

  ```bash
  channel-append.sh --channel "…/owner-channel.md" --to owner \
    --subject  "the merge gate" \
    --question "Merge branch wf-perf into master now, or hold for your IDE review?" \
    --option   "Merge it" \
    --option   "Hold" \
    --deadline 2h \
    --default  2
  ```

  `--deadline` is `2h`, `90m`, or a bare number meaning minutes; past 168h the app reads it as NO
  deadline at all, so the tool refuses that rather than letting it mean the opposite of what you
  wrote. `--default` is the OPTION NUMBER AS THE OWNER SEES IT — 1-based, matching the buttons — and
  is refused if it names no option, or if there is no deadline for it to be applied at. When the
  deadline passes unanswered the default is applied and `/pending` says it was.

  **They are optional and they are NOT symmetrical.** A `DEADLINE:` alone is meaningful: the question
  expires and says so. A `DEFAULT:` alone is dropped, because nothing would ever apply it. Writing
  neither is the old behaviour exactly — the question waits indefinitely.

  Written twice is not an error — the FIRST readable one wins, so a marker repeated at the bottom
  cannot silently override the one a human reads at the top. An unreadable value is dropped on its
  own and never takes the other marker with it.

  **Only give a `DEFAULT:` to a question whose unattended answer you would defend.** It spends the
  owner's decision for them, so it belongs on the reversible ones and never on a merge, a push, or
  anything that costs money.

## The “💬 Let’s talk” button, and the re-ask that must follow

  A tap there CLOSES the question, exactly like every other button: the keyboard goes and the
  message they tapped becomes "💬 Ok — tell me what you have in mind." What reaches you is a request
  to explain the decision — what each option means in practice, what differs between them, what it
  costs to get wrong, which one you recommend and why — in prose, briefly. Answer whatever they ask
  next.

  **Then, once the discussion has settled, ASK IT AGAIN** with fresh `QUESTION:`/`OPTION:` lines.
  Nothing of that question is live on their phone while you talk, so the re-ask is not a second copy
  of a live question — and a decision nobody re-asks is a decision that silently never gets taken.
  That is not hypothetical: on 2026-09-09 the owner tapped this button on a pricing-table question
  and nobody ever came back to it, because the instruction here used to say "do not ask it again".

  Treat a tap here as signal that your question was not answerable as written — make the re-ask
  clearer, not longer.

  The body above can be as long and thorough as the decision deserves; the question underneath must
  be short enough to answer from a lock screen.

## Pictures and files — `IMAGE:` and `ATTACH:`

- **Send the owner PICTURES when a picture says it better:** add `IMAGE: <full path>` lines to
  the entry body (screenshots of a built UI, charts, failing output). The app uploads each as a
  real photo in the topic and strips the line from the text.
- **Send the owner a FILE with `ATTACH: <full path>`** — an HTML mockup, a CSV, a report. One line
  per file, column 0, like `IMAGE:`. **`IMAGE:` is for pictures only** (`.png`, `.jpg`, `.webp`,
  `.gif`, `.bmp`): an HTML file sent that way is refused, because Telegram answers a photo upload of
  one with `400 IMAGE_PROCESS_FAILED` — which is what silently swallowed four mockups on
  2026-09-08 while the supervisor told the owner it had sent them.
- **Both markers read from three places, and nowhere else**: this orchestration's repository, your
  own channel folder, and `~/mockups/` — which is where you put something you MADE for the owner.
  Caps: 10 MB for a picture, 50 MB for a file. Anything refused is written into this channel with
  the reason and the fix; the owner never sees it, so read your own channel and send it again
  properly rather than telling them it could not be done.

## The same call, as the channel protocol first shows it

  ```bash
  channel-append.sh \
    --channel "${AIORCH_SUPERVISION_ROOT:-$HOME/.claude/supervision}/$ARGUMENTS/owner-channel.md" \
    --to owner \
    --subject "the merge gate" \
    --question  "merge stage 13 now, or hold for the reaction work?" \
    --option    "Merge it" \
    --option    "Hold" \
    --recommend "Merge — nothing else touches the grammar" \
    --risk low \
    --row FIN-D-277
  ```
