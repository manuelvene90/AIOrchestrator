# Reading member traffic — declarations, windows, deferred rulings

Moved out of `SKILL.md` on 2026-09-15 (the skill diet, spec step 5 of
`docs/superpowers/specs/2026-09-15-one-wake-model-design.md`). **Nothing here was rewritten** —
the blocks below are that file's own words, in their original order. The RULES they carry stayed
in `SKILL.md`; what lives here is the detail, the syntax and the dated account of why.


## `STANDING BY` — the three worked examples and why the app errs toward a verdict

- **But only when the marker LEADS the subject and stands ALONE there** — the marker, what the member
  is waiting for, and nothing else. Anything sharing that subject is filed work and you still owe a
  verdict on it:

  ```
  STANDING BY — waiting on rev-4              declares, owes you nothing
  STANDING BY — one correction: wrong file    the correction is owed a reply
  review filed, 3 findings. STANDING BY       a report, owed a verdict
  ```

  Those look alike and are opposite states, so read the title, not the last line: the second and third
  are members waiting on YOU, and that is the queue only you can clear.
- **It is a heuristic and it errs toward telling you a verdict is owed.** A spurious reminder costs you
  one wake; the opposite costs a member's filed work its reader, silently. If you are reminded about
  an entry that genuinely asked you for nothing, that is the rule working in the direction it was
  aimed — and worth telling the members so, since the convention only reaches them after a rebuild.
- **Without the marker, the nudge comes to YOU, not to them.** A member that goes quiet after its own
  entry, with no open window, reads as a filed report awaiting your verdict — so the app nudges the
  supervisor about an entry that may have asked for nothing. That is the loop the marker exists to
  end, and it is why you should expect the declaration rather than treat it as optional. The member
  itself is only woken when it left a WRITING WINDOW open, which is the genuine stalled-mid-task case.

## Window markers — the exact phrases, and a ruling that lands at CLOSE

- **When you tell a member to close one, tell it the EXACT phrase, and the matching kind.** The four
  are `WRITING WINDOW OPEN` / `WRITING WINDOW CLOSED` and `MUTATION WINDOW OPEN` /
  `MUTATION WINDOW CLOSED`. The two kinds are tracked SEPARATELY — both can be open at once, and
  closing one does not close the other. **A mis-spelled close does nothing and says nothing**: the
  window stays open and the member keeps rendering as still writing, which you will read as a stalled
  session. On 2026-08-14 two of ten members got this wrong in a day — one wrote a bare
  "WINDOW CLOSED" without the WRITING prefix, one closed a mutation window while a writing window
  stood open. **Never propose relaxing the matcher**: "MUTATION WINDOW CLOSED" CONTAINS
  "WINDOW CLOSED", so accepting the short form would let a mutation close silently close a writing
  window. The matcher is correct; the instruction you give is what has to be exact.
- **A ruling you write while a member's window is open reaches it at CLOSE, not on arrival.** Members
  re-read the channel before writing the close report, so your entry lands above that report rather
  than below it — **it is deferred, not missed.** Do not re-send it, and do not read the report that
  crosses it as the member ignoring you: it was written from what the member knew when it opened the
  window. If the ruling changes what the member should be doing RIGHT NOW rather than what it should
  report, say so in the subject, because that is the case the deferral costs you.
