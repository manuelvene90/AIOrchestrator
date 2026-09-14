# Porting the fork's work into master — the census and the rulings

Date: 2026-09-14 · Fork: `nathanthegrey/AIOrchestrator` @ `ours/integration` `f5c277f` ·
Upstream: `manuelvene90/AIOrchestrator` @ `upstream/master` `19f1a1c` ·
Common ancestor: `dbb6e4e` (2026-09-11 13:42).

This document is the input to the port, not the port. It says what the fork has that master
does not, what the two trees genuinely disagree about, and which differences are a matter of
whose phone it is rather than of correctness.

---

## 1. The situation, in numbers

| | |
|---|---|
| Commits on master since the ancestor | 94 |
| Commits on the fork since the ancestor | 47 |
| Files the fork touched | 132 (43 new, 89 modified) |
| Files master touched | 164 |
| Files touched by **both** | 9 |
| **Files that actually conflict** | **2** |

The last row is measured, not estimated: `git merge-tree --write-tree upstream/master
ours/integration` reports exactly two conflicted paths and nine clean auto-merges.

```
CONFLICT (content): AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs
CONFLICT (content): AIOrchestratorCoreLib.Tests/Bridge/QuestionContractProbeTests.cs
```

Both sit in one domain: the owner's questions. Everything else in the port either merges by
itself or is a file master has not touched since the ancestor.

Two facts make this port cheaper than the commit counts suggest. First, master already
absorbed the fork once, at the ancestor, and resolved every conflicted file **toward the
fork's structure** (`91d3402`), so the two trees share a file layout. Second, the 94
upstream commits were spent almost entirely on ground the fork never touched — the settings
catalogue, terminal respawn, the statusline, a two-OS CI.

---

## 2. Method, and what was actually verified

Ten subsystem passes, each answering the same three questions against both trees: does
master already do this, under any name; if both trees changed the same behaviour, which is
right and what is the failing case; and is this a difference of correctness or of taste.

Rules the passes were held to, and which shaped the rulings below:

- **Search by behaviour, never by filename.** A file only the fork touched can still
  duplicate something master implements elsewhere under another name.
- **On a genuine contest the default verdict is master's.** The fork wins only where a
  concrete failing case can be named — inputs and state leading to a wrong result. "Ours is
  newer, cleaner, better tested" is not a reason.
- **Say which tree a claim comes from.** The working tree is the fork; master is reachable
  only through `git show upstream/master:<path>`. One pass reported "there is no settings
  catalogue in this repo" after looking in the working directory; the catalogue has 16 files in
  master's library, 22 counting its tests. That finding was withdrawn.

**Verified on this machine, macOS, .NET 10.0.401, 2026-09-14:**

- The fork's suite: `Failed: 0, Passed: 3572, Skipped: 9, Total: 3581` — the nine skips are
  the nine the project expects, compared by name.
- The conflict surface above, by trial merge.
- Every load-bearing claim reproduced below under "verified".

**NOT verified, and it must be before anything lands:**

- The **merged** tree has been built by nobody. Master has post-ancestor Windows
  file-locking work the fork has never run, and the fork has 47 commits master has never
  compiled. The suite must be re-run on the merged result; neither side's current green says
  anything about it.
- The WPF app builds only on Windows (`net10.0-windows`, `UseWPF`). Nothing in this document
  was verified against a running app.
- The member silence brake counts live processes below a turn. On Windows the CLI is wrapped
  in `cmd.exe /c claude`, so that signal is always true and the brake's evidence is
  meaningless there; the fork disables the long ceiling on Windows for this reason. The
  Windows process-tree reader has never been measured on Windows.

---

## 3. Rulings already taken by the fork owner

These close questions that would otherwise be open, and they shape sections B and C.

1. **One question at a time is adopted — master's rule wins.** The fork built its
   question-answer linking on the premise that several open questions are acceptable; the
   fork owner has since ruled that one at a time is fine, provided the Telegram reply
   gesture works. The proposed `phone.openQuestions` setting is therefore **withdrawn**, the
   hold (`QuestionHold_Policy`) stands as master wrote it, and the fork's second-layer
   repeat guards are offered as additions behind it, never as replacements.
2. **Where the two owners' preferences differ, the shipped default is master's behaviour**
   and the fork's preference lives in the `quiet` preset. This follows the catalogue's own
   convention: a preset names only keys that differ from the shipped default.
3. **Every taste key was chosen, not inherited.** The fork owner walked the whole catalogue
   on 2026-09-14 rather than letting defaults apply by omission. §7.3 records the result,
   including the keys he reviewed and chose to leave alone — which the preset file cannot
   hold, because a preset names only differences. One default is reversed knowingly
   (`topic.modeGlyphs`), and the reversal is argued there rather than buried in a diff.

---

## 4. The answer to the fork owner's opening question — already built, upstream

The fork owner wants the Telegram PULSE but not the periodic status line, and wants a say
over the buttons beneath it. **Master has already built the machinery and already written
the fork owner's preferences down by name.** Nothing needs to be negotiated and no new key
is needed.

`upstream/master:kit/presets/quiet.json`, whose own comment reads "Nathan's way":

```json
"phone.status.periodic": false,
"phone.appMessagesRing": false,
"phone.receipts": "reactions",
"topic.onClose": "delete"
```

`upstream/master:kit/presets/classic.json`, "Manu's way", carries `pulse.fields` and
`pulse.buttons` — so both the fields inside the pulse and the buttons under it are already
settings, with master's own choices in master's own preset.

**What is missing is only the wiring.** Every one of these keys carries master's own
doc-string: `REGISTERED BUT READ BY NOTHING YET — the engine starts obeying this key in plan
03`. `TopicStatusLine_Builder.Build` still hard-codes its field order, and no periodic
message is sent by any code in either tree today.

So this is not a porting item at all. It is a note to whoever executes plan 03, and one
caution worth carrying into it: the periodic status was removed on 2026-09-09 precisely
because it notified every thirty minutes, which this repo's decision 14 calls a waterfall.
Re-enabling it for `classic` reintroduces that defect unless the periodic send **edits one
message** rather than sending a new one.

---

## 5. Section A — clean ports

Behaviour master does not have, under any name, where nothing of master's is displaced.
Grouped by subsystem; every item was checked against master by behaviour, not by path.

### A1 · Usage limits — the live window is the one the latest reading names

The window in force is decided by which probe file was written last, not by which reading
carries the latest reset stamp. Master still uses the stamp rule (`Compare_Instance`), and
its own premise — that `resets_at` grows with time — is refuted by measurement: on
2026-09-11/12 the weekly window's reset moved **backwards**, 09-16 05:00 → 09-14 10:00.

**Failing case in master today.** Two readings of `seven_day`: a file written 09-10 16:29
reporting 60% with reset 09-16, and one written 09-11 23:59 reporting 90% with reset 09-14.
Master elects the first — later stamp — and reports 60% until 09-16 while the account sits
at 90%. The same comparison drives the alert latch, so **no 90/95/97/98/99/100% alert can
fire** while the account climbs. This is the money path.

**Port both consumers together.** Master's own comment above `Read_CurrentLimitWindows`
states the invariant: one reader, two consumers, and a second copy of the loop is a second
place for the two rules to drift, "at which point the app could pause on one reading and
alert about another". Porting only the `RateLimits_Reader` half creates exactly that.

**One risk the fork has not observed and cannot rule out.** Master's rule elects
`max(stamp)`, which is monotone, so the window identity is stable. The fork's rule elects
the last-written file, so two concurrently live sessions that disagree about `resets_at`
would flip the identity every tick, and each flip resets the alert latch and re-fires every
crossed threshold — a phone waterfall, the thing decision 14 exists to prevent. The fork's
own measurement does not contain this case, because there the disagreeing probes were idle
and stopped being rewritten. Mitigation worth taking during the port: key the latch to the
instance held on the previous tick rather than re-deriving it.

Files: `Limits/WindowInstance_Order.cs` (new `Compare_Reading`, `Compare_Instance` kept for
"is this the same window"), `Limits/RateLimits_Reader.cs`, `Usage/UsageTotals_Reader.cs`
(`Read_LastWriteUtc_Safe`), `BridgeEngineModel.Read_CurrentLimitWindows`. All auto-merge
clean except the engine file. **Verified:** the one-reader invariant for cost and tokens
holds in master, in the fork, and in the merged result — the fork adds a file stat, not a
second figure.

### A2 · The Telegram reply gesture actually binds the answer

**This is the item the fork owner asked for, and it is a correctness fix, not a preference.**

In master, the text quoted by a Telegram reply is prepended to the owner's words
(`BridgeEngineModel.cs:13468`) and the **composite** is what reaches the answer binding
(`:13491`). The binder is ask-shaped-first: if the text looks like a question it is not an
answer (`AnswerBinding_Decider.cs:70`), and its own comment notes this "reads the same
whether one question is open or five".

**Failing case in master today, with exactly one question open.** The owner long-presses the
agent's question "Merge stage 13 now?" and replies "ok". The composite ends up containing a
question mark inside the quote, the binder returns `NothingItIsAQuestion`, and the owner's
"ok" binds nothing — the question stays open with live buttons. The quote is context the app
composed; it was never the owner's answer.

The fix judges the owner's **own words** and keeps the quote as context. It is independent
of how many questions are open, so it survives the ruling in §3.1 intact.

The rest of the linking — outbound `reply_parameters`, `ReplyToMessageId` inbound, the
`ReplyLinks` store, the index-returning `ChannelAppender` appends — travels with it and is
what makes a reply resolvable to a specific question in the cases master's hold does not
cover, which master itself names: terminal presence raises no awaiting-answer flag, the
ten-minute cap expires a flag with the question still open, and `/pc` lifts a standing block.

Porting surface is wide — parser, owner-message interface, prose sender, channel appender, a
new dispatcher member — and the mirror-loop half lands inside the method master split into
`Mirror_Append_Async` / `Mirror_AppendEntries_Async`. See §6.

### A3 · Questions the owner already dealt with

Three guards, all additive behind master's hold, each with a dated incident:

- **The owner's own question does not count as an answer.** Master stamps "replied in words"
  unconditionally. So the owner taps «Sì» on a high-risk merge question, then — before typing
  the read-back code — asks something unrelated; that question stamps the reply, the
  supervisor's next question supersedes the tapped one, and **the owner's held answer is
  discarded** while a live confirmation code remains for a decision that no longer exists.
  (fincanva-6, 2026-09-11 08:35–08:43 UTC.)
- **A tapped question awaiting its read-back is never superseded.** Master's supersede path
  actively removes it from `_pendingConfirmations`. Same incident, same loss.
- **A question the owner already closed is withheld once.** Tapping "💬 Let's talk" closes the
  question and clears the flag; the protocol then has the session re-ask it, and master's hold
  does not apply because nothing is outstanding. The identical question arrives again with
  fresh buttons. (ai-orch-6, 2026-09-12: *"per esempio in sta chat mi hai fatto la stessa
  domanda 2 volte"*.)

The third needs a persisted-schema change — `OpenQuestionRecord.Prompt` in the snapshot and
the serializer. **It fails silently if half-ported**: the guard compares against a field that
is never written, matches nothing, and no test catches it unless the test writes and reloads
state.

### A4 · False alarms on the owner's phone

- **"An answer is coming" only when one can.** Master sends that receipt unconditionally. On
  2026-09-11 the supervisor could not log in — every attempt 08:16–08:27 returned "Please run
  /login" — and the phone twice read "nudged, an answer is coming", each about twenty seconds
  after a stall alert said the opposite. The owner resent their message into a dead session.
- **The loop asks about the session the owner is waiting on.** Master hard-wires the question
  to `(Supervisor, "sup")`, a registration a basic orchestration never makes, so a working
  headless solo reads as idle for its whole turn and gets nudged. (ai-orch-2: a turn started
  14:42:22 was told at 14:44:49 that it had "ended without answering"; 16 nudges in one evening.)
- **"Never handed" is not a verdict a cursor can give mid-turn.** The supervisor's cursor
  advances only at turn end, so closing an implementer while the supervisor is mid-turn
  reading that very report logs the report as never delivered. (2026-09-10: entry [72] filed
  14:06:13, declared lost 14:12:43, delivered 14:12:58.) The fix distinguishes a genuine drop
  from traffic in flight and never suppresses either.
- **One stalled turn is one message, not three.** Master's five stall call sites append with
  no deduplication (fincanva-6, 2026-09-11: three identical texts for one stall) — decision 14.
- **Case-insensitive session keys.** Master's in-flight dictionaries are case-sensitive while
  the path-based store resolves case-insensitively, so an orchestration id differing only in
  case silently resurrects the false alarm above.

### A5 · Member brakes and turn liveness

A print-runner turn is killed for **silence** — no output, no transcript write by it or a
sub-agent, no live process below it — rather than for elapsed time; a turn that repeats
itself is stopped too; a braked turn gets a long ceiling; a killed turn reports where it got
to instead of being redone from zero; and the host's shutdown grace is sized for the longest
possible turn rather than the default, or a stop cuts the drain.

Master has none of this: the stream runner's silence brake predates the ancestor and is
shared; the print runner has only a flat deadline.

Two honest caveats to carry to the review: the loop-detector thresholds (4 repeats, 3 error
repeats, 6 ping-pong steps) were taken from another project's description and have **never
been validated against this project's transcripts**; and the Windows caveat in §2.

### A6 · The outbound door to Telegram

`Telegram/Outbound/` — the vocabulary (bands, kinds, intents, outcomes), the store (jobs keep
their order, slots keep only their latest value) and the scheduler (the band that may knock
anyway). It implements §4.2 of `docs/superpowers/specs/2026-09-10-one-door-to-telegram-design.md`,
**which is already in master**.

It is complementary to master's existing rate-limit machinery, not a replacement: the
cooldown book answers "is this target currently refused", the token bucket "how fast", the
retry policy "should this caller try again" — none of them answers "given several things all
wanting to be said, which goes first". Master's own code says so: `OutboundCooldown_Gate.cs:72-76`
declares `For_Chat` *"Unused by the per-target slice and here for the queue that follows it"*,
and a test already pins it. **Verified:** nothing of master's becomes dead when this lands.

**Port it unwired.** `OutboundPump` — the consumer that would replace the direct client calls
scattered through the engine — exists in neither tree, and the design document itself calls
that conversion the largest edit in the subsystem's history. Land the scaffolding; build and
wire the pump as its own reviewable stage. **Verified:** nothing outside the folder references
these types today.

### A7 · Turn execution, state packs, rendering

- **Narration is not a final message.** Text a session emits before a tool call never reaches
  the phone as a report.
- **A reply nobody waited for is not the answer to the next message.**
- **The app's notes to a headless session ride its next turn.**
- **Refused config settings are reported at boot**, not on the dispatcher's first tick.
- **A member saves progress while it works** (`progress.md`) and a cut turn resumes from it.
  This is **complementary to master's `--resume`, not competing with it**: master's restores
  the CLI conversation for terminal supervisor/solo and for `resume: transcript` print roles;
  the fork's restores work-in-progress for fresh print turns, which structurally have no
  conversation to restore. They are gated by the same `fresh` boolean and never both apply.
  Master's `--resume` is not decision 8's forbidden `--continue`; master's own doc-strings
  distinguish them, and implementers and reviewers are explicitly exempt.
- **Rendering fixes:** bold survives a stray asterisk (`3*5 e **uno**` currently renders with
  the bold destroyed and both markers literal); the pulse summary strips markdown before
  counting words (a cut currently slices a `**…**` pair and leaves the asterisks on the status
  line); markdown tables render as lines rather than reaching the phone as pipes and dashes.
  **Verified:** the "a refused render never loses the text" fallback exists intact in master —
  there is no data-loss gap here.
- **The nudge window gets a shared home**, removing two prose copies of the number 8 that
  cannot reference a private constant.
- **An orchestration gets its Telegram topic at creation**, not at its first mirrorable entry.
  An orchestration whose only entry is the owner's own message has nothing to mirror, so with
  a slow first turn the owner sees no topic and cannot tell a slow start from a lost one.

### A8 · Protocol and rules

- **The general supervisor reads an orchestration's state now, never from its own earlier
  entry** — with three concrete tests (mid-turn is working; an app entry is not evidence of a
  stall; blocked on the owner means a line that says so). **Verified:** every log line this
  rule names is emitted by master's own build.
- **`NOTICED` and `MISSED EARLIER` are plain words, not backticked markers.** This is
  mechanically required, not cosmetic: master ships the guard that fails on any backticked
  all-caps phrase the matcher does not act on, and that test is byte-identical in both trees.
  It must land in the same commit as the prose that introduces the phrases.
- **Absence is not a measurement** (`.claude/rules/code-conventions.md`): on a file another
  process is still writing, presence is a valid conclusion and absence is not. The temporal
  sibling of decision 18 — 18 asks which copy you read, this asks when.

---

## 6. Section B — the two files that conflict, and how to resolve them

Both are in the questions domain, and both conflict because master rewrote exactly the region
the fork extended.

### B1 · `BridgeEngineModel.cs`

Master's post-ancestor work on this file built three things the fork has never seen: the
question hold with its third mirror outcome and its positional prefix memo; `/pause`; and
`/model` and `/effort` from the phone with the answer-credit rework. `Mirror_Append_Async` was
split in two, and the question send now happens inside the per-entry loop.

**The fork's repeat guards land in the 44 lines master deleted.** Master removed the
second-open-question coaching block, arguing the hold makes a second live question impossible.
That argument holds for remote presence and only until the owner answers; the fork's guards
cover what it leaves:

- Terminal presence raises no awaiting-answer flag at all, so the hold never engages and a
  verbatim re-ask goes straight out.
- The hold only delays. Owner answers at 10:50, the flag clears, the held append resumes on
  the next poll, and the verbatim re-ask of the question just answered is delivered at
  10:50:02 — a duplicate rescheduled to the worst possible moment rather than avoided.

**Resolution: the guards are ported as a second layer behind the hold, never as a substitute.**
Two hazards that a mechanical merge would hide:

- `Handle_RepeatedQuestion` calls `Raise_AwaitingAnswerFlag`, which in master's tree now means
  *hold the entire owner channel*. Harmless where the flag is already up, but it must be
  re-reasoned rather than ported blind — the diff cannot show this change of meaning.
- The early return from the question send must leave master's `deliveredHere` counter running
  in the caller. It does, but this is precisely the positional memo master's own comments say
  goes silently wrong.

The reply-threading hunk and the topic-at-creation re-signature both land inside the method
master split, where every line of context has moved or changed meaning. Getting the held-prefix
memo off by one silently re-sends or silently skips entries, with no log line either way.

### B2 · `QuestionContractProbeTests.cs`

The fork appended six probes; three of them reach a two-questions-open state by appending both
questions side by side before the engine runs. Master deleted exactly that setup from its own
probes and routes them through a helper that backdates the flag past the hold's cap and waits
for expiry, because — in master's words — the old setup "manufactured a state the app
deliberately no longer produces".

**Verdict: master's. Port the six probe bodies onto master's helper; do not port the setup
lines.** A probe ported unchanged will hang waiting for a keyboard the hold prevents, or worse
assert against a state that never forms. The fork's own file already admits this fixture's
fragility in a comment.

---

## 7. Section C — taste, chosen rather than inherited

Every proposal follows the catalogue's convention: **shipped default = master's behaviour
today**, the fork owner's preference carried by `quiet`, and `classic` left silent.

The fork owner went through the whole catalogue on 2026-09-14 rather than accepting defaults.
What follows is the result of that pass, and it is deliberately recorded here in full —
including the keys he left where they were — because of a property of the preset convention
that is easy to miss.

### 7.1 · Why choices that match the default are recorded here and not in the preset

A preset names only keys whose value differs from the shipped default; master's own comment
gives the reason, and it is a good one: *"a preset that restates a default is a default that
can never move."*

The consequence is that **choosing a value identical to the shipped default does not pin
it**. If the default later moves, the fork owner moves with it, having decided nothing. He
reviewed each of the keys in §7.3 and chose to keep its current value; that decision has no
home in the preset file, so it is recorded here instead. If master later changes one of these
defaults, this section is the record that someone had looked at it and meant it.

### 7.2 · New keys this port introduces

| Key | Kind | Values | Shipped default (master) | `quiet` (the fork) |
|---|---|---|---|---|
| `phone.renderMarkdownTables` | Bool | on/off | `false` — tables arrive as written | `true` |
| `pulse.taskSummaryTruncationMark` | Enum | `none`, `ellipsis` | `none` — full text lives in the feed | `ellipsis` |
| `topic.createAt` | Enum | `firstMessage`, `creation` | `firstMessage` | `creation` |
| `receipts.nudgePromise` | Enum | `always`, `onlyWhenPossible` | `always` | `onlyWhenPossible` |
| `phone.repeatQuestionWindowMinutes` | Int, 0–480 | 0 = off | `0` | `120` |
| `limits.memberSilenceMinutes` | Int, 0–1440 | 0 = brake off | `0` — master has no member brake | `15` |
| `limits.memberTurnTimeoutMinutes` | Int, 1–1440 | | `120`, inert while the brake is off | `120` |

Two of these ship "off" by the convention above, which means **the member brakes and the
repeated-question guard arrive disabled by default**, with the fork's behaviour confined to
its preset. That cedes the default and not merely the argument; it is the shape most likely
to be accepted, and it was chosen with that trade understood.

The first two guards in A3 are deliberately **not** offered as settings: each is a case where
the app records a decision nobody took or discards an answer the owner gave. That is a
correctness bar, not a taste.

### 7.3 · The fork owner's profile, as chosen on 2026-09-14

**Changed from what the fork owner would otherwise inherit — one line to add to `quiet.json`:**

```json
"topic.modeGlyphs": "name"
```

The mode glyphs (🌙 🔕 ✈ 🤐 💻) move from PULSE's header onto the topic **name**, so the
delivery mode is visible from the topic list without opening anything.

**This reverses the fork's own earlier reasoning, knowingly.** The glyphs were moved off the
name because each rename is an `editForumTopic` call and every rename writes a service
message, so an app-wide mode change wrote a line into every one of the owner's threads to
tell them something they had just done themselves. The fork owner has weighed that and judged
the visibility worth the noise, on the grounds that he changes mode rarely. Recorded here so
that the reversal reads as a decision rather than as an oversight, and so that whoever meets
the service-message noise later knows it was priced in.

*A reasonable improvement for whoever touches this code: rename only when the glyph actually
changes, not on every recalculation. That would give the visibility without the noise, and it
would make the trade above disappear rather than be endured.*

**Confirmed, already in `quiet.json` — master chose these on the fork owner's behalf and they
are now ratified rather than merely inherited:**

| Key | Value | What it means |
|---|---|---|
| `phone.push` | `everything` | every channel entry reaches the phone, not only what asks or blocks |
| `phone.status.periodic` | `false` | the PULSE only; no periodic status message |
| `phone.appMessagesRing` | `false` | the app's own messages arrive silently — an agent's question still rings |
| `phone.receipts` | `reactions` | the app reacts to the owner's message rather than editing a receipt line |
| `topic.onClose` | `delete` | closing an orchestration deletes its topic rather than archiving it |

**Reviewed on 2026-09-14 and deliberately left at the shipped default** — no preset line, by
the reasoning in §7.1:

| Key | Value kept | Note |
|---|---|---|
| `pulse.fields` | the shipped seven | waitingOnYou · supervisor · members · closedCount · lastEvent · merged · updated. `modelEffort` exists and was declined; master takes it and drops three others |
| `pulse.buttons` | the shipped six | ⏳ pending · 📋 left · 👀 tail sup · 📉 limits · 🔀 merge · 🏁 close |
| `general.buttons` | the shipped set | 📊 summary · ⏳ pending · 📉 limits — master ships himself none |
| `pulse.holdToggle` | `true` | the ⏸/▶ toggle stays on the PULSE bar rather than the receipt |
| `phone.replyKeyboard` | `off` | the slash-command bar stays off; it re-shows itself whenever the phone keyboard hides |

Worth stating plainly, because "inheriting the default" sounds like taking master's side and
here it is often the opposite: several shipped defaults encode the **fork's** reasoning, and
master overrides them in `classic`. `topic.modeGlyphs` is the clearest case — its shipped
default exists because of the fork's service-message finding, which is exactly the default the
fork owner has now chosen to leave.

## 8. Things a merge must not decide by itself

- **The review protocol lives in two files.** The depth ladder is named in `supervisor/SKILL.md`
  (contested) and executed in `reviewer/SKILL.md` (fork-only). Resolving them per-file by the
  default rule takes master's ladder and the fork's executor, leaving the supervisor briefing a
  depth the reviewer's table does not contain. **Port both or neither.** The same applies to
  master's instruction that a reviewer pre-declare its planned agent count, which becomes an
  order to declare a number that is structurally zero.
- **The progress-note protocol must ship with its app half.** The kit bullet promises the pack
  hands the note back; master's tree has no `PROGRESS_FILE_NAME`, no heading, no input field.
  Ported alone it is a protocol that lies to the session.
- **Solo has no reviewer.** After the rule that the implementer runs `/simplify` rather than
  `/code-review`, only the reviewer role knows `/code-review` exists — and a solo orchestration
  has no reviewer. Both trees share the hole; this change makes it permanent. Decide it rather
  than inherit it.
- **`.claude/rules/git-and-boundaries.md`** was rewritten upstream to describe a single repo
  rather than a fork. Nothing to port, but if work continues on `ours/integration` after this,
  that file must not be taken from master without a decision: it deletes the fork's approval
  boundaries from the file a session reads.
- **A residue to fix on the way in:** the fork's supervisor skill still uses `deep` as a level
  name in an example, in a table that no longer contains it.

---

## 9. Suggested order

1. **Independent and cheap first** — A1 (both consumers in one commit), A4, A7's rendering
   fixes, A8's rules and the marker un-backticking (same commit as its prose).
2. **The door**, unwired, as scaffolding for the rest of master's own design (A6).
3. **The brakes and the state pack** (A5, A7), with the two catalogue keys added in the same
   pass and the Windows caveat stated in the review.
4. **The questions domain last, by hand** — B1 and B2 together with A2 and A3, after the
   settled ruling that the hold stands. This is the only part where a mechanical merge is
   actively dangerous.
5. **Not a porting item:** §4. A note on master's plan 03, with the waterfall caution.

---

## 10. What is owed before any of this is called done

- Build and run the full suite **on the merged tree**, on both Windows and Linux. Neither
  side's current green transfers.
- Re-measure the limits behaviour against real probe files after A1 lands, including the
  two-live-sessions case the fork has not observed.
- Validate the loop-detector thresholds against real transcripts, or ship them documented as
  unvalidated.
- Measure the Windows process-tree reader on Windows, or keep the ceiling disabled there.
