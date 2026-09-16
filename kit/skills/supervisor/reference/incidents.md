# Incidents — the dated account behind rules that stayed in the skill

Moved out of `SKILL.md` on 2026-09-15 (the skill diet, spec step 5 of
`docs/superpowers/specs/2026-09-15-one-wake-model-design.md`). **Nothing here was rewritten** —
the blocks below are that file's own words, in their original order. The RULES they carry stayed
in `SKILL.md`; what lives here is the detail, the syntax and the dated account of why.


## Why the marker words are not spelled in the skill any more

  **THE MARKER WORDS ARE NOT SPELLED HERE ANY MORE, AND THAT IS DELIBERATE.** `QUESTION:`, `OPTION:`,
  `STATE:`, the header shape — the tool writes them, from ONE file it and the app both read
  (`kit/grammar/channel-grammar.json`). Three pages plus nine code files each carrying their own copy
  is how the question marker came to be written WITH its colon on one side and matched WITHOUT it on
  the other, in three files, each looking correct where it sat.

## Hand-numbering: the duplicate index and the timestamp ten hours out

  **So you compute NEITHER, and hand-numbering is precisely what broke.** "Re-read the LAST header
  and add one" cannot be made safe by trying harder — the window it leaves open IS the write:
  - **`n`**: on 2026-08-10 an `option-lab-2` channel ended up with two `[80]` and two `[81]` entries,
    because the supervisor numbered from a read taken minutes earlier while the app appended in
    between; on 2026-08-13 two writers both read `[71]` and both wrote `[72]`. The index is how we
    cite each other ("act on entry [83]"), so a duplicate makes a citation ambiguous — and the
    multi-write shape that goes with hand-numbering put a reviewer's nine findings under a
    supervisor's header, an audit trail that is confidently wrong.
  - **The timestamp**: a supervisor stamped `2026-08-11 01:34` on an entry written at `15:20` the day
    before — a day ahead and ten hours off. The app measures "time on task" from this field, and a
    future stamp made every member card read "on task under a minute" for hours. The app now refuses
    to display a future stamp, so the cost of getting it wrong is a BLANK where your working time
    should be. The helper stamps from the system clock; your sense of the time never enters it.

## A header in any other shape makes the entry invisible (2026-08-07)

  **A header in any other shape makes the entry INVISIBLE — this is not pedantry, it happened.** On
  2026-08-07 one supervisor wrote headers three ways (`## [SUPERVISOR — date] subject`,
  `## [supervisor] FROM supervisor — …`, `## [2b] FROM …`) and every one of those entries ceased to
  exist as far as the system was concerned: never mirrored to the owner's phone (they never saw the
  message at all), never counted as traffic, and their index numbers stayed free. Nothing errors —
  the text just sits there being ignored. The app now detects malformed headers and posts a
  correction into the channel; when you see one, **re-append the content as a NEW well-formed entry**
  (never edit the broken line — the channel is append-only).

## A day of the owner’s money on an unchecked premise (2026-08-25)

  What it looks like when skipped, 2026-08-25, verbatim: *"A test harness for that level would take
  about a day … Do we build it, or do we keep verifying that level by reading the code?"* The owner
  answered *"Build it"* — and the correction came afterwards: *"Actually they can — there's already
  a fixture designed exactly for this and 13 test files use it. Someone had already found that gap
  before us and closed half of it. I gave an estimate without verifying if the thing already
  existed."* A day of the owner's money was authorised against a premise nobody had tested, and only
  the session's own later honesty caught it.
