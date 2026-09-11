# Telegram bridge — briefs A–F from the 2026-09-09 audit

Source: the Telegram integration audit of 2026-09-09 (owner's artifact "Audit Telegram AIOrchestrator").
Every finding below was verified on branch source `ours/integration` @ `5737664` (line numbers from
`926cc6b` where noted, shifted by a few lines since) and, where marked **[VPS]**, in the production
channel files / logs read on 2026-09-09 evening (read-only, owner-authorised).

Six packages. A, B, C are three orchestrations, in this order; D, E, F follow C (D after C has been
observed live; E3 is its own orchestration; E1/E2/F are small). Each traces to one OWNER REQUEST row. Anything
found on the way that is not in the row goes to `## PARKED` (decision 22).

Language: this file and every channel entry are English; anything addressed to the owner is written in
the owner's language (decision 11).

---

## Brief A — Buttons: every tap changes the message it was tapped on

**OWNER REQUEST (2026-09-09):** "Let's talk does nothing when I tap it — it stays there, all the other
options stay too. Make it behave like the other buttons: buttons disappear, the message says
'ok, tell me what you have in mind'."

### Facts (verified)
- A tap on `💬 Let's talk` deliberately leaves the message untouched and sends no receipt; the only
  feedback is Telegram's toast `✓` (`PendingDecision_Gate.Describe_ForOwner`, `BridgeEngineModel.cs`
  ~9088-9106, `KeepsGroupOpen`). **[VPS]** On 2026-09-09 the owner tapped it 12 times in `fincanva-5`,
  four of them within one minute (entries 5, 6, 12 at 15:58); every tap was accepted and answered by
  the supervisor within a minute — the button works, it is mute.
- The text a tap sends to the supervisor (`OwnerPush_Policy.TALK_REQUEST`, `MORE_DETAIL_REQUEST`, or the
  option label) is routed as an OWNER message (`Route_TapAsOwnerMessage_Async` → `Route_OwnerMessage_Async`
  → `Close_AnsweredQuestions_Async`, ~9166-9186, ~10953). With exactly ONE other open question in the
  orchestration, `AnswerBinding_Decider` binds that text to it and stamps it `✅ answered: <text>`,
  removing its keyboard. **[VPS]** 2026-09-09 17:53 the owner tapped a high-risk option on the orphaned
  processes question (code shown); 17:55 tapped Let's talk on the pricing-table question; the talk text
  bound to the orphans question and stamped it — nobody ever decided it. Applies to ALL taps, not only
  Let's talk (the tapped question is removed before routing, so "one other open" is common: the log
  says "a second question went out while one was still open" three times that afternoon).
- When a high-risk read-back code lapses, the log says "the question is still open" without reading the
  registry (`~16:03Z` on 2026-09-09 the question had already been stamped closed).

### Scope
1. `Let's talk` behaves like every other option: keyboard removed, message edited to
   "💬 Ok — tell me what you have in mind." (English — app-written strings are English, owner 2026-09-09:
   "if the strings are hardcoded, English; the rule holds"). The supervisor receives the request as today.
2. `❔ Explain the options` and `💬 Let's talk` collapse into ONE button (without "keep the keyboard"
   there is no difference between them). Label: `💬 Let's talk` (owner's choice, 2026-09-09).
3. The supervisor skill: after the discussion settles, RE-ASK with fresh options (the current text
   "do NOT ask it again" is inverted; the question was closed by the tap, so a re-ask is not a second
   copy of a live question — decision 14 is satisfied).
4. Root fix: text originating from a tap never enters the typed-answer binding. Mark the synthetic
   message (a flag on `ITelegramOwnerMessage` or a separate route) so `Close_AnsweredQuestions_Async`
   is skipped for it. This fixes ALL buttons, not only Let's talk.
5. The read-back-lapse log line checks `_openQuestions` before saying "still open"; otherwise it says
   "the question was already closed — by: <what closed it>".
6. Drop `KeepsGroupOpen` and `InDiscussion` if nothing else uses them after 1–4 (grep first; the
   serializer writes both — keep reading them tolerant for old state files).

### Out of scope
Receipts (✓/✓✓) for taps; reactions; anything in briefs B and C.

### Done when
- `LetsTalk_KeepsTheQuestionOpen_AndATypedReplyDoesNotCloseIt_UntilATapDoes` is rewritten to the new
  contract (tap → keyboard gone, message edited, supervisor receives the request).
- New probe: two questions open in one orchestration; tap an option on the FIRST; assert the SECOND
  is still open, still has its keyboard, and was not stamped. Same probe with Let's talk.
- New probe: high-risk code lapses on a question already closed → log line names the closure, not
  "still open".
- Supervisor skill text updated (`kit/skills/supervisor/SKILL.md` around 386-410, and `solo/SKILL.md`
  157-175) — remember decision 17: the kit is delivered by the app build / plugin reinstall, not by the
  edit.
- Full suite green (0 red, 9 skipped, compare names), trial merge into `ours/integration`.

### Files (expected)
`AIOrchestratorCoreLib/Bridge/BridgeEngine/BridgeEngineModel.cs` (tap handler, question send,
`Close_AnsweredQuestions_Async`, read-back lapse), `AIOrchestratorCoreLib/Bridge/OwnerPush_Policy.cs`
(labels/requests), `AIOrchestratorCoreLib/Telegram/TelegramOwnerMessage/*` (origin flag),
`AIOrchestratorCoreLib/Bridge/EngineState/*` (tolerant read), `kit/skills/supervisor/SKILL.md`,
`kit/skills/solo/SKILL.md`, tests under `AIOrchestratorCoreLib.Tests/Bridge/`.

---

## Brief B — The bridge neither loses the owner's messages nor lies about them

**OWNER REQUEST (2026-09-09):** "Fix the six critical defects of the Telegram audit: the bridge must
never lose a message of mine or tell me it was received when it was not."

### Facts (verified, `BridgeEngineModel.cs` @ `926cc6b`)
1. **409 Conflict** (two pollers on one token) is indistinguishable from any other `getUpdates` failure
   (:5960-5972: log + backoff forever). The single-instance lock is a LOCAL file
   (`SingleInstance_Guard`, `.instance.lock`): the Windows app and the VPS daemon can both poll. No
   `getMe`/`deleteWebhook` at startup.
2. **Batch replay:** `_lastUpdateId` advances only after the whole batch is handled (:5947-5951); any
   escaped exception replays every update. **[VPS]** 2026-09-08 01:24-01:26Z: `answerCallbackQuery
   failed: query is too old` ×4 at 20/40/60 s, then `getUpdates failed — DllNotFoundException:
   user32.dll` — a Windows-only call (window focus / screenshot) threw on Linux and the same batch was
   replayed four times.
3. **False ✓:** `Send_ReceivedAck_Async` runs after `Route_OwnerMessage_Async` regardless of outcome
   (:5916-5931); an unknown topic is dropped with a warning (:10938-10946); a CLOSED orchestration's
   topic is written into a dead channel (`Find_ByTelegramTopicId_OrNull` does not filter `ClosedUtc`).
4. **Owner documents are dropped silently:** `TelegramUpdates_Parser.cs:117-122` extracts text, photo,
   voice, callback only; a document without caption produces nothing and still advances the offset.
5. **30-minute give-up:** after `MIRROR_RETRY_WINDOW_MINUTES` of failed sends the append is confirmed and
   the entries never reach the phone (:1312-1325) — one Error log line.
6. **Close deletes the topic** (`deleteForumTopic`, never `closeForumTopic`) fire-and-forget, no retry
   (:4733-4757, :5636-5652). — OWNER DECISION pending (brief E); in this brief only: make the delete
   retried/reconciled OR switch to `closeForumTopic` if the owner has ruled by then.

### Scope
1. Detect 409 explicitly (`TelegramApiException.StatusCode == 409`): one message to the General topic
   naming the HOST that is speaking (machine name) — "another bridge is polling with this token; this is
   <host> — is the other one the Windows app?" — one log line per state change, no per-retry spam;
   polling continues (the other poller may stop). `getMe` + `deleteWebhook(drop_pending_updates: false)`
   at startup, once. **No lease file** (implementer, 2026-09-09, accepted): `SingleInstance_Guard`
   already covers two bridges on a SHARED root, and two hosts with SEPARATE roots cannot be seen by any
   file. Instead a config key `telegramMode` (`poll` | `mirror`) tells a host not to poll; default
   `poll` so nothing silently stops receiving. A `telegramMode` key already exists in config — check its
   current meaning first and extend it or choose a new name.
2. Per-update acknowledgement: advance `_lastUpdateId` after each update is handled (or keep a
   processed-id set for the batch and skip already-processed ids on replay). Callback taps deduplicated
   by `callback_query.id`.
3. `Route_OwnerMessage_Async` returns an outcome (`Routed | UnknownTopic | ClosedOrchestration | Held`);
   the ✓ is sent only for `Routed`; the other outcomes answer the owner in one line in their language
   ("this orchestration is closed — ask the general supervisor to reopen or start a new one").
4. Documents: parse `message.document` (file_id, file_name, mime_type, size ≤ 20 MB download cap);
   download to `media/` beside the channel like photos; append `FILE: <path>` (+ caption) to the owner
   entry; refuse >20 MB with a one-line reply.
5. Give-up: never confirm-and-drop silently. After the window, park the undelivered entries and, when
   the next send succeeds, deliver ONE digest ("N entries from HH:MM–HH:MM did not reach you — attached")
   as a `.md` document via the existing `Send_Document_Async`.
6. Host capabilities (the bridge runs on Windows and Linux today, macOS later — owner, 2026-09-09):
   window focus, layout and screenshots are behind a per-host capability (`IHostWindowing` or the
   existing `WindowFocus/` seam), implemented for Windows, a `NotSupported` implementation for Linux
   and macOS until real ones exist. A command whose capability is missing answers the owner in one line
   ("not available on this host yet") and logs once — it never throws into the inbound loop. Nothing
   OS-specific is called by name from `BridgeEngineModel`.

### Out of scope
Rate-limiter changes, `disable_notification`, reactions (brief C/D); `closeForumTopic` vs delete is the
owner's call (brief E) — here only the retry.

### Done when
- Probe: a fake client returning 409 on `getUpdates` → exactly one General message, one log line,
  backoff continues; a later 200 → one "back" line.
- Probe: a batch of three updates whose second handler throws → the first is not re-routed, the third
  is handled, the offset ends past all three.
- Probe: the same `callback_query.id` delivered twice → one action.
- Probe: owner message into a closed orchestration's topic → no ✓, one explanatory reply, nothing
  appended to the dead channel; unknown topic → same.
- Probe: `document` update without caption → file in `media/`, `FILE:` line in the channel, ✓ sent.
- Probe: mirror failing past the window → entries parked; next success → one digest document.
- Probe (Linux only): `/show` → one refusal line, no exception in the log.
- Full suite green (0 red, 9 skipped, compare names), trial merge into `ours/integration`.

### Files (expected)
`BridgeEngineModel.cs` (inbound loop, routing, mirror settle, command dispatch),
`Telegram/TelegramApiClient/*` (409 surface, `getMe`, `deleteWebhook`), `Telegram/TelegramUpdates_Parser.cs`
(document), `Telegram/TelegramOwnerMessage/*` (document fields), `Composition/SingleInstance_Guard.cs` or a
new `Telegram/PollerLease_*`, `Sessions/OrchestrationSessionStore/*` (closed filter), tests.

---

## Brief C — Noise: the phone rings when the supervisor speaks; the app is silent and rendered

**Status: APPROVED by the owner, 2026-09-09 evening** (points 1–7 discussed one by one; the decisions
below are the owner's wording where quoted).

**OWNER REQUEST (2026-09-09):** "Half of what reaches my phone is not for me — STATUS every 30 minutes,
false 'waiting on your reply' alerts, the same receipt sentence every time, and sometimes bold and
headings arrive as raw asterisks. Make the noise stop and the formatting hold. If the supervisor writes
to me, I must know it — that rings. Status, receipts and app bookkeeping do not ring."

### Facts (verified in code; **[phone]** = seen in the owner's chats of 2026-09-09)
1. **[phone]** STATUS every 30 min as a NEW 15-line message (10 in 5.5 h in `FIN · capability matrix`;
   19:00/19:30/20:00 identical with every member closed). `Push_PeriodicStatus_Async` (:11447) fires
   while `Has_WorkInFlight` (:12529-12535) is true, which reads PLAN.md in-progress lines — not whether
   any session worked. `Post_StatusEntry` (:12385) appends an App entry that the mirror sends as a new
   message.
2. **[phone]** The STATUS/PULSE *content* is half wrong and half jargon (`Build_MemberStatusText_ForSession`
   :8628-8642, `Build_PeriodicStatusText` :12489, `MemberState_Descriptor`, `TopicStatusLine_Builder`):
   - "supervisor: waiting on you" comes from `OwnerOwesReply_Decider` ("the session spoke last") — false
     at 17:00/18:30 after "Nothing more needed from you"; at 18:38 the supervisor was paused for a usage
     limit and the line said "waiting on you", then "idle — waiting" for two hours with no reason/ETA.
   - "N running" counts ledger `[>]` lines, not sessions ("5 running" with all 9 members closed).
   - "1 task blocked, needs you" never says WHICH task or what to do.
   - "now: FIN-D-293a step 6 and step 7" is the first in-progress ledger line — repeated identically
     16:00→21:00, after 293 was merged at 18:00.
   - "idle — writing window left open", "not doing", "new — no traffic · last wrote 1 h ago" are internal
     jargon / self-contradictions.
   - 9 rows "imp-n: closed"; PULSE "unchanged 3 h 15 min" while two merges and three rulings happened;
     PULSE member rows carry the TRUNCATED subject of the last entry ("`sourced` finding is").
   - "done" means MERGED: an evening of work reads "0/4 · 0%".
3. **[phone]** `⚠️ … has been waiting on your reply for 25 min` at 17:30 after "Nothing more needed from
   you", again 18:38 and 20:25; other topic 16:39, 20:25, 20:58. Same decider as above; re-arms on any
   traffic (:1596-1601).
4. **[phone]** Raw Markdown (`**…**`, `## …`, backticks) at 18:00 and 18:18 — the two paths that resend
   "the last thing it said": `Announce_SupervisorFree_Async` (:12751 → `Send_DirectReply_BestEffort_Async`
   :9810 → `Send_Message_Async`, plain) and the silent-deadlock release (:10004 → `Send_AwayNotice_Async`
   :12304, plain). Both skip folding and attachment.
5. Renderer: `Try_AppendRun` renders inner text recursively (`TelegramHtml_Renderer.cs:363-410`), so
   `**\`code\`**` → `<b><code>…</code></b>`. Telegram: bold/italic/underline/strike/spoiler "can contain
   and can be part of any other entities, except pre and code" → 400 → `TelegramProse_Sender` falls back to
   plain text with the Markdown markers literal. **[to measure]** count "Telegram refused the HTML" in the
   VPS logs (read-only) before starting, to know how often this path fires.
6. **[phone]** Receipt: `✓✓ · 🔴 Sup: mid-task. Your message is delivered; they pick it up when this turn
   ends` — two identical lines for ~every owner message (15 in one export); at 17:03 the edited receipt
   AND a separate "mid-task…" message.
7. `disable_notification` is used nowhere (one comment at :8189). The PULSE repost after 10 s of quiet
   is a plain `sendMessage` → rings. `link_preview_options` never passed.
8. `OwnerPush_Policy.Should_Push` (:80-100) SUPPRESSES supervisor entries that are not a question, an
   answer the owner awaits, `BLOCKED ON OWNER`, a file or the greeting; suppressed entries surface only
   through `Break_Silent­Deadlock_Async` after 5 min ("nothing has moved for 5 min — sending you the last
   thing it said") — plain text (fact 4). The owner's quoted example ("La regola ora è completa…")
   reached the phone through that path.
9. Command replies (`/progress`, `/cost`, `/status`, …) are plain text; the client's doc comment claims
   every owner-facing send is HTML.

### Decisions (owner, 2026-09-09)
- **Language of app-written strings: ENGLISH.** Owner, 2026-09-09: "if the strings are hardcoded,
  English; the rule holds." Decision 11 governs what ROLES write to the owner (their language); every
  string the APP itself emits (PULSE labels, receipts, alerts, button labels, edited texts) stays English.
  The Italian phrasings that appear below are illustrations of CONTENT, not the strings to ship.
- **Who rings:** every entry the SUPERVISOR (or solo) writes on the owner channel reaches the phone
  immediately, rendered, WITH sound. Everything the APP writes (PULSE/status, receipts, confirmations,
  "turn ended", coaching) is SILENT. Alerts the owner can act on ring: budget/limit, stall-after-question.
  The narration filter in `OwnerPush_Policy` is removed for supervisor-authored entries; the brake on
  chatter is the skill (write to the owner only what they must know) plus the existing brevity nudge.
- **Stall alert (⚠️):** only when the supervisor's last owner-channel entry is a real question
  (`QUESTION:` block or `BLOCKED ON OWNER`), once per question (key: the question's entry index), never
  while the supervisor is paused for a usage limit.
- **Receipt:** ✓ → ✓✓ silently, nothing else. The "busy" sentence appears only when it says something
  new: wait ≥ 3 min (the existing counting edit), a held message (⏸), a handoff. Never a second message
  for the same state.
- **Periodic STATUS message: removed.** ONE status surface per topic — PULSE — with the SIX FIELDS below.
- **Six fields of PULSE**, in this order:
  1. `⏳ waiting on you · <what, with its source>` — open questions (buttons live) AND ledger lines blocked on
     the owner (e.g. "your browser pass on FIN-D-277 (since 18:18)"). Omitted entirely when nothing waits.
  2. `sup · <state DECLARED by the supervisor at turn end> · declared HH:MM` — the supervisor's own
     one-line state (skill change: every turn on the owner channel ends with a `STATE:` line for the
     bridge, e.g. "waiting for imp-2's review, then I hand you the merge" — the supervisor writes it in the owner's language, the label is English). The app adds only what it knows
     for certain: paused for usage limit + resume time (it already writes that at :18:38 as an App entry).
  3. Live members: `who · task (ledger id + title) · state · for how long`, one row each up to
     4 live members; from 5 live, one line ("imp-1, imp-2, imp-3 working · rev-1 waiting"). Closed
     members as a count only ("7 closed"). Order: waiting on the owner → working → idle. Never the
     truncated subject of the last entry.
  4. `last · HH:MM · <last relevant event>` — last supervisor entry subject or ledger transition
     (e.g. "18:00 · FIN-D-293 merged to staging"), not the first in-progress ledger line.
  5. `<merged>/<total> merged · NN %` — honest word: merged, not done. No "N running" from ledger lines;
     no "not doing".
  6. `updated HH:MM` — the heartbeat.
  Out: "writing window", "not doing", ledger "running", the closed roster, `now:` from the ledger.
- **Form:** PULSE is one message at the BOTTOM of the topic, edited in place, silent; deleted and
  re-posted (silently) only when it is buried by later traffic AND its content changed. Not pinned
  (owner's standing no to pins; a pin also jumps the reader away from the bottom).
- **Topic name glyphs:** only ❓ (waiting on the owner), ⏸ (paused for usage limit), 🏁 (closed). The
  🌙/🔕 mode glyphs move into PULSE's header line instead of the name (fewer renames, fewer service
  messages).
- **Command bar** on PULSE (2 per row, this order):
  `[⏳ /pending] [📋 /left]` / `[👀 /tail sup] [📉 /limits]` / `[🔀 /merge] [🏁 /close]`.
  Removed from the bar: `/screen`, `/show` (Windows-desktop only), `/pc`, `/test` (typed commands stay).
  General topic bar: `/summary`, `/pending`, `/limits`, `/resume`, `/dnd_all`.
- **General dashboard:** one line per orchestration (`name · ❓/⏳ · merged/total · last event HH:MM`),
  edited in place, silent. The check-in ritual ("fammi il riassunto" when the owner returns) is unchanged.
- **DND:** with silence as the default, 🌙 holds only what rings (supervisor entries, actionable alerts);
  PULSE and the dashboard keep updating silently.
- **Rendering:** the two plain paths ("turn ended", "nothing has moved") go through `TelegramProse_Sender`
  (HTML + fold + attachment) like every supervisor entry; the suffix is appended AFTER rendering as its
  own escaped line. Inline code inside bold/italic/strike is emitted OUTSIDE the wrapping tag (or the
  run is not opened when its content holds a code span) — with tests for `**\`x\`**`, `*\`x\`*`,
  `~~\`x\`~~`. Command replies go through the HTML path.
- **Every send** passes `disable_notification` explicitly (a required argument on `ITelegramApiClient`
  send methods, so a loud send is a visible choice at the call site) and `link_preview_options =
  { is_disabled: true }`.

### Out of scope
Reactions as receipts (brief D — after C is observed live for a few evenings); `closeForumTopic`; typed
entries; topic colours; the full menu-command cleanup (brief F). `/pc` and the Windows-desktop commands
themselves (brief B makes them host-capability-gated).

### Done when
- Probe: one busy orchestration, two implementers, ten member entries/hour, no supervisor entry on the
  owner channel → zero non-silent sends in the hour; the PULSE message id is unchanged (edited, not
  re-sent); its `updated` time advances.
- Probe: supervisor writes a plain report (no question) → it reaches the fake client immediately, as
  HTML, with `disable_notification=false`; no 5-minute delay, no "nothing has moved" suffix.
- Probe: supervisor writes "nothing more needed", 30 min quiet → no ⚠️; writes a `QUESTION:` block,
  25 min quiet → one ⚠️; owner replies in prose, another 25 min → no second ⚠️; supervisor paused for a
  usage limit → no ⚠️.
- Probe: supervisor entry `**Two fixes**\n\n## Your pass\n\`x\`` delivered via the turn-ended path → the
  fake client receives HTML with `<b>`, no literal `**`/`##`; via the deadlock path → same.
- Renderer tests: `**\`x\`**` renders without `<b><code>` nesting; all existing renderer tests unchanged.
- Probe: owner message while the supervisor is mid-task → exactly one bot message (✓), edited to ✓✓ with
  no sentence before 3 min; after 3 min the counting edit; never a separate "mid-task" message.
- Probe: PULSE content — a session paused for a usage limit renders "paused for usage limit, resumes
  at HH:MM"; a ledger line blocked on the owner renders under `⏳ waiting on you` with its id; 9 members
  of which 7 closed render two rows + "7 closed"; the last event is the latest supervisor subject, not
  the first `[>]` line.
- Probe: PULSE buried under 3 later messages with unchanged content → not re-posted; content changes →
  one silent re-post at the bottom.
- Every `Send_*` on the fake client asserts `disable_notification` per the rule; `link_preview` disabled
  on all text sends.
- Skill: supervisor/solo end each owner-channel turn with a `STATE:` line the bridge reads for PULSE
  field 2; the skill says explicitly that everything written on the owner channel rings the owner's
  phone (`kit/skills/supervisor/SKILL.md`, `solo/SKILL.md`; decision 17 for delivery).
- **[measured, owner reads]** one evening of the owner's topic before/after: 2026-09-09 ≈ 6 bot
  messages/hour, ~half noise → target: rings only for supervisor entries and actionable alerts; PULSE
  fields read true against the channel.
- Full suite green (0 red, 9 skipped, compare names), trial merge into `ours/integration`.

### Files (expected)
`BridgeEngineModel.cs` (periodic status → removed; PULSE builder; stall alert; turn-ended/deadlock paths;
receipts; sends; dashboard), `Bridge/OwnerPush_Policy.cs` (filter), `Status/OwnerOwesReply_Decider.cs`
(question-based), `Status/MemberState_Descriptor.cs`, `Telegram/TopicStatusLine_*` (six fields, silent
repost-when-changed), `Telegram/TopicCommandButtons.cs` (bar), `Telegram/GeneralDashboard_*`,
`Telegram/TelegramHtml_Renderer.cs`, `Telegram/TelegramProse_Sender.cs`, `Telegram/TelegramApiClient/*`
(`disable_notification`, `link_preview_options` as explicit arguments), `Channels/*` (`STATE:` line
parsing), `kit/skills/supervisor/SKILL.md`, `kit/skills/solo/SKILL.md`, tests.

---

## Brief D — Receipts as reactions (after C has been observed live)

**Status: APPROVED by the owner, 2026-09-09; START ORDERED 2026-09-10** — the owner deploys C and tests it
while D is built ("nel mentre testo e finiamo D"). Depends on C (silence by default), which is merged.

**OWNER REQUEST (2026-09-09):** "Acknowledge my messages with a reaction on my bubble instead of a ✓
message. 👀 → 👌 is fine."

### Facts
- Today every owner message gets a bot message `✓` (carrying the ⏸ Wait button) edited to `✓✓`
  (`Send_ReceivedAck_Async` ~:11352, `Publish_DeliveryReceipt_Async` ~:13135). After C it is silent but
  still a message per owner message.
- Bots may set ONE reaction per message, from a fixed set (Bot API 7.0+; `setMessageReaction`).
  `✅` is NOT in the set; `👀 👌 👍 🫡 ✍ 🤝` are. Setting a reaction needs no extra `allowed_updates`;
  READING the owner's reactions would need `message_reaction` in `allowed_updates` (not in scope).
- `WAIT`/`GO` typed control words already hold and release the aggregation buffer
  (`OwnerControlWords.cs`, `Apply_HoldControlWord_Async` ~:9877-9951). The owner never used the ⏸ button
  in the 2026-09-09 exports but wants the hold to stay reachable.

### Decisions (owner, 2026-09-09)
- Reaction on the owner's own message: `👀` when the bridge has appended it to the channel; replaced by
  `👌` when the session picks it up (turn started with it). No ✓ message at all.
- The "busy" sentence survives only as C defines it (≥ 3 min counting edit, hold, handoff) — as a
  message only when it carries information; otherwise nothing.
- ⏸ Wait moves to the PULSE command bar as a toggle button (`⏸ /wait` ↔ `▶ /go`, showing the held
  count: "⏸ 3 held · ▶ GO"); typed `WAIT`/`GO` stay as shortcuts. The bar grows to 4 rows.
- If `setMessageReaction` fails (400 on an unreactable message, 429), fall back to the silent ✓ message
  for that one message — never lose the acknowledgement.

### Done when
- Probe: owner message → fake client receives `setMessageReaction(👀)` on that message id, no `sendMessage`;
  session picks it up → `setMessageReaction(👌)` replaces it.
- Probe: reaction call fails → exactly one silent ✓ message, edited to ✓✓ later.
- Probe: `/wait` tap → bar shows held count, messages buffered; `/go` → one delivery, bar back.
- Full suite green (0 red, 9 skipped, compare names); trial merge into `ours/integration`.
- **[owner reads]** one evening: the topic shows conversation + PULSE only, reactions on the owner's bubbles.

### Files (expected)
`Telegram/TelegramApiClient/*` (`setMessageReaction`), `BridgeEngineModel.cs` (receipt path, hold),
`Telegram/TopicCommandButtons.cs`, tests.

---

## Brief E — Three owner decisions: close, one brevity ceiling, typed entries

**Status: DECIDED by the owner, 2026-09-09.** E1 and E2 can ride with B/C's follow-up or as one small
package; E3 is its own orchestration, AFTER C is in production.

### E1 — Closing an orchestration DELETES its topic, reliably
**Decision:** keep deleting (the owner will have hundreds, then thousands of topics; closed topics
lingering in the list are noise). Not `closeForumTopic`, no N-day archive.
**Facts:** `deleteForumTopic` is fire-and-forget (`Delete_TelegramTopic_FireAndForget` ~:5636-5652):
a failure leaves an orphan topic Telegram-side with no record.
**Scope:** the delete is retried with backoff and recorded in `session.json` (`TelegramTopicDeletedUtc`
or `TelegramTopicDeletePending`); a reconciliation sweep on every start retries pending deletes; a
delete that keeps failing (permissions) tells the owner once in General. The channel files on disk stay
forever (audit trail); "what did we decide on X" is answered by the general supervisor from the files.
**Done when:** probe — delete returns 429 then 200 → topic deleted, session records it; probe — delete
fails permanently → one General message, one log line per restart, no spam.

### E2 — One ceiling, skills aligned with the code
**Decision (numbers):** 5 lines / 600 characters (the `Brevity_Policy` values); `OwnerMessage_Contract`
reads the SAME constants (one home). Options: 2–4 enforced — a 5th `OPTION:` and beyond → coaching
entry to the supervisor + all buttons numbered (existing layout); option label width 28 text elements in
code AND in the skills (today "≤30 chars"); remove the `noted — …` worked example from
`kit/skills/supervisor/SKILL.md:321` (the receipt-opener check stays — a receipt in place of content is
chatter). Skills state "three pushable kinds" — after C everything the supervisor writes is pushed;
rewrite that paragraph.
**Done when:** grep shows one definition of the ceiling; `OwnerQuestion_Contract` test for 5+ options;
skill texts cite the constants' values; no `noted —` example; suite green.

### E3 — Typed entries: a validating tool instead of parsed prose markers
**Decision:** yes in principle (owner: "if you are sure it is better, ok" — I am sure of the direction:
the fragility is measured — CLAUDE.md decisions 12–13, the 2026-08-08 four header variants, the
"noted" contradiction, 5-vs-6 lines; less sure of the cost, which is why it is its own orchestration,
AFTER C, with a transition).
**Shape:** a small CLI the roles call to append (`channel-append --to owner --question "…" --option "…"
--option "…" --recommend "…" --risk low --row FIN-D-277`, plus `--state`, `--report`, `--attach`), which
validates BEFORE writing (header index/timestamp computed by the tool, never by the model) and writes the
entry in a form the bridge renders deterministically per type. **Dual parser during transition:** the
bridge keeps reading today's marker prose; a session on the old skill is never mute. Skills change to
"call the tool" and stop restating the grammar.

**Requirements added 2026-09-10 (from the token-efficiency agent's review, accepted):**
1. **Writer and recognizer read ONE constant, not "one place".** By construction there are two sides —
   the tool that writes and the parsers that recognise (the bridge's entry parser, the digest that
   must not hold a blocked member, the state pack). All of them take marker words, header shape and
   ceilings from a single `ChannelGrammar` class; no string literal of a marker anywhere else (a test
   greps for it). "The tool is the only place" would recreate today's drift (`QUESTION:` written on one
   side, `QUESTION` recognised on the other) within a month. The legacy-marker path uses the same
   constant.
2. **Role-bound author.** Today `channel-append.sh --author <word>` accepts any word — a member can sign
   as "supervisor" (this is how a model choice got erased from a brief). The launcher already exports
   the session's role and member id to the process; the tool derives the author from them and REFUSES
   a mismatching `--author` (one log line naming both). One line inside a job already planned; a
   package on its own otherwise.
3. **Declared type is persisted in the entry** (a typed field the parsers read), so the state pack and
   the digest read `type` (`question`, `report`, `state`, `brief`, `review`, …) instead of guessing from
   the subject's first word (`BRIEF —`, `REVIEW …`). The defect "a brief that is merely quoted becomes
   the brief" disappears with no extra work.

**Done when:** every entry kind has a typed path; a malformed call is refused before the write with the
missing fields named; old-format entries still render; the marker grammar appears in ONE constant read by
tool and parsers alike (grep test); a mismatching `--author` is refused; the state pack selects the brief
by type; suite green; **[owner reads]** one evening with zero coaching entries about format.

---

## Brief F — Hygiene (one package, low risk, after B)

**Status: APPROVED by the owner, 2026-09-09.**

1. **Topic colour per repo** at `createForumTopic`: Telegram's six `icon_color` values, assigned per repo
   in a stable rotation (7th repo reuses the 1st); same repo → same colour while it exists. Recorded in
   `config.json` beside the repo entry.
2. **Command menu:** keep ALL 32 commands (the brief first said 33 — a counting error; code and
   brief list the same 32), descriptions in ENGLISH (app-string rule), reordered by
   frequency of use (`setMyCommands` preserves order): `/pending /left /progress /tail /limits /cost
   /merge /close /dnd /mute /summary /resume /status /tasks /tokens /context /diff /imp /log /test /done
   /refresh /switch /clear /pc /dnd_all /mute_all /screens /screen /show /organize /organize_mains` —
   the host-only ones last (B makes them answer "not available on this host").
3. `link_preview_options.is_disabled=true` on every text send (if not already landed with C).
4. Chunker: the 4096 budget counts text after entity parsing, not the HTML markup
   (`OwnerMessage_Chunker.cs:117`); no split inside a surrogate pair (`TelegramMessage_Chunker.cs:21-26`,
   `OwnerDocument_Builder.cs:105`).
5. Rate limiter: edits, deletes and `answerCallbackQuery` metered by a second, larger bucket; 429
   `retry_after` honoured for them too (short cap); the bucket starts EMPTY on process start.
6. Inbound filters: callback taps and `forum_topic_edited` service messages accepted only from the
   configured supergroup `chat.id` (`TelegramUpdates_Parser.cs:66-80, 82-106`).
7. File caps checked before sending: photo 10 MB + dimension/ratio rule, document 50 MB, download 20 MB
   (`TelegramApiClientModel.cs:355-380, 517-535`); a refused file is said back to the agent.
8. `HttpClient` with `PooledConnectionLifetime` (the daemon runs for weeks).
9. First tests of the HTTP layer: make `TelegramApiClientModel` testable behind an `HttpMessageHandler`
   fake — getUpdates URL (`allowed_updates`, `offset`, `timeout`), 429 retry path, multipart shape,
   409 surface (from B).
10. `Says_TopicNotModified`/`Is_MessageGone` etc.: one table mapping Telegram `error_code` +
    `description` to the cases the bridge distinguishes, pinned by tests against recorded real bodies.

**Done when:** each item has a unit test or probe; suite green; trial merge into `ours/integration`.

---

## Brief G — The suite is reliably green under load (DECIDED, AFTER A–F)

**Status: owner's decision 2026-09-10 — "finish A B C D E F first, then G".** Not started; nobody may
start it unasked.

**Owner request (2026-09-10):** "The suite must be green even when another suite is running on this
machine; a red must mean a claim about the app was falsified, never 'load'."

**Measured 2026-09-10 (352 test files):** 62 files wait on real time (`Thread.Sleep`/`Task.Delay`; 29 of
them drive the real engine), 91 read the wall clock, 7 are on an injected clock, 144 delete their temp tree
with a bare recursive delete (4 use `TempTree.Delete_BestEffort`). Two full parallel runs of the tip at
load ~10 gave one red each, in two different wall-clock probes, both 5/5 green in isolation; the same
family went red on the untouched base under load (up to 5). Every Done-when in this file says "suite
green", so this blocks requested lines (decision 22 admission).

**G-light first (≈1 agent-day [estimate]):**
1. The 144 bare recursive deletes adopt `TempTree.Delete_BestEffort` (mechanical, scripted, one full run).
2. Wall-clock probes move into an xUnit collection that runs serially while the rest stays parallel — no
   conversion, no assertion touched; cost: tens of seconds on the suite.
3. Working rule, written into the kit skills and CLAUDE-adjacent docs: `pgrep -f "dotnet test"` must be
   empty before a run; whoever touches a wall-clock probe converts it to the injected clock
   (`BridgeEngineTiming`, as stage 7d did).

**Done when (G-light):** 5 consecutive full parallel runs green while another suite is running (load ≥ 10);
no assertion weakened (each converted or moved probe still fails under mutation).

**G-full (only if G-light is not enough; 3–5 agent-days [estimate]):** inject the clock into the remaining
wall-clock probes, the 29 engine probes first through the existing timing seam.

---

## PARKED (from the same audit — outside A–F; owner decides when)

- Docs (Manu's, report only): CLAUDE.md decision 4 describes direction tags that do not exist; the
  2026-08-06 spec has no buttons/receipts/status line; the spec lists the watchdog as cut and built.
- From stage/8a (implementer, 2026-09-09): (1) in the probe harness a supervisor entry appended right
  after an app entry in the same run was never read by the tailer — cause unproven, brief B's family:
  B adds a probe for it; (2) `Build_GeneralCommandMessage` turns a typed `/summary`-style command into
  a canned English request not marked `IsAppComposed`, so with one question open in General it can bind
  as that question's answer — same family as A's root fix: fold into B (routing).
- Post-deploy observations, VPS 2026-09-10 20:56 (tip 481efc9, kit reinstalled, "kit check OK"):
  (1) `editForumTopic` → `400 TOPIC_ID_INVALID` ×6 at start for four CLOSED orchestrations (sandbox-1,
  fincanva-1/3/4) whose topics were deleted before E1 but whose `session.json` still holds a topic id —
  the name sync should forget the id on TOPIC_ID_INVALID (F10's table knows the code) and never retry;
  noise at every restart, no loss. (2) `editMessageText` on the PULSE of the two live topics → `429`
  with retry_after 20–22 s, twice in two minutes after the restart; handled by the retry, but it says
  Telegram throttles same-message edits tighter than F5's "larger" control bucket assumes — watch it;
  if it recurs outside restarts, size the control bucket from these retry_after values.
