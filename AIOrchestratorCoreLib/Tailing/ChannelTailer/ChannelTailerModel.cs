using System.Text;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Channels.DiscoveredChannel;
using AIOrchestratorCoreLib.Tailing.CompletedChannelAppend;
using AIOrchestratorCoreLib.Tailing.TailerPollResult;
using AIOrchestratorCoreLib.Time.Clock;

namespace AIOrchestratorCoreLib.Tailing.ChannelTailer;

internal sealed class ChannelTailerModel : IChannelTailer
{
    sealed class FileTailState
    {
        public long Offset;
        public readonly StringBuilder Pending = new();

        /// <summary>
        /// Text of entries already emitted to the bridge but not yet acknowledged as delivered.
        /// It is subtracted from the persisted offset and re-emitted on the next poll, so a send
        /// that failed is retried instead of being silently skipped.
        /// </summary>
        public readonly StringBuilder Unconfirmed = new();

        /// <summary>
        /// WHEN THIS FILE WAS LAST SEEN TO GROW — the start of the current quiet stretch, and the only
        /// thing the trailing-entry release is measured against. Null until a poll has looked at the
        /// file at all.
        ///
        /// <para>
        /// IT WAS A POLL COUNT UNTIL 2026-09-09, and that is the defect this field exists to close. The
        /// rule was "two polls with no growth", so the protection it bought was always two times the
        /// poll interval — never written down anywhere, and true only while that interval was the 2000 ms
        /// mirror tick. <c>ChannelChangeWaker</c> then made the loop poll at 200 ms after a wake without
        /// touching the count, and the guarantee silently shrank 12×: swept against a real stalling
        /// writer, an entry survived a 300 ms pause and was TORN at 400 ms, where the pre-merge cadence
        /// carried it to 4000 ms. A duration cannot be moved by somebody changing how often the loop runs.
        /// </para>
        /// </summary>
        public DateTime? QuietSinceUtc;
    }

    /// <summary>
    /// Guards <see cref="_states"/>, <see cref="_lastPolledFiles"/> AND the contents of every
    /// <see cref="FileTailState"/> they hold. Two bridge loops share this object: the mirror loop
    /// polls, confirms and re-anchors, while the inbound loop persists the offsets snapshot. Before
    /// this, the mirror side took no lock at all and the inbound side took one the mirror side never
    /// touched — a dictionary mutated while another thread enumerated it (an exception surfacing as
    /// a Telegram fault), or a cursor persisted mid-update and silently wrong.
    /// <para>
    /// TAKE IT AT THE PUBLIC BOUNDARY, for the WHOLE operation — never per dictionary lookup. The
    /// state a caller reads is a StringBuilder that a poll appends to, so serialising the lookup
    /// while leaving the read of Pending outside the lock would look safe and protect nothing.
    /// Every public method below opens with it; the private helpers assume it is already held.
    /// </para>
    /// </summary>
    readonly Lock _statesLock = new();

    readonly Dictionary<string, FileTailState> _states = [];

    /// <summary>
    /// The channels handed to the last <see cref="Poll"/>. Keyed exactly like <see cref="_states"/>,
    /// so a path that finds its state also finds its poll record — the two must never disagree.
    /// </summary>
    readonly HashSet<string> _lastPolledFiles = [];

    /// <summary>
    /// How long a file must have stopped growing before its LAST entry may be released — see
    /// <see cref="ChannelTailer_Factory.TRAILING_ENTRY_QUIET_MILLISECONDS"/> for the number and why it
    /// is the number. A duration, never a count of polls: how often the loop polls is a latency
    /// decision belonging to the mirror loop, and it must not be able to move this.
    /// </summary>
    readonly TimeSpan _trailingEntryQuiet;

    /// <summary>
    /// Injected because the release above is a DEADLINE READ, which is exactly the case
    /// <see cref="IClock"/> exists for: a test that drives polls synchronously has no wall clock to
    /// wait out, and sleeping through four real seconds per assertion is not a test suite.
    /// </summary>
    readonly IClock _clock;

    public ChannelTailerModel(IReadOnlyDictionary<string, long> persistedOffsets, TimeSpan trailingEntryQuiet, IClock clock)
    {
        _trailingEntryQuiet = trailingEntryQuiet;
        _clock = clock;

        foreach (var pair in persistedOffsets)
        {
            var state = new FileTailState { Offset = pair.Value };
            _states[pair.Key] = state;
        }
    }

    public ITailerPollResult Poll(IReadOnlyList<IDiscoveredChannel> channels)
    {
        // ONE clock read for the whole poll, and every channel is judged against it. Reading per file
        // would let a poll that takes a second over twenty channels hold the first to a stricter quiet
        // period than the last, for no reason a reader could ever reconstruct.
        var nowUtc = _clock.UtcNow;

        // The file reads happen under the lock too. They are the only reason a poll takes long
        // enough to matter, and dropping the lock around them would hand the snapshot reader a
        // half-updated cursor — precisely the state this lock exists to make unobservable.
        lock (_statesLock)
        {
            return Poll_AllChannels(channels, nowUtc);
        }
    }

    /// <summary>Runs with <see cref="_statesLock"/> HELD, as does everything it calls.</summary>
    ITailerPollResult Poll_AllChannels(IReadOnlyList<IDiscoveredChannel> channels, DateTime nowUtc)
    {
        List<ICompletedChannelAppend> completedAppends = [];
        List<string> truncatedFiles = [];
        List<string> unreadableFiles = [];
        List<string> heldTrailingEntryFiles = [];

        // Recorded BEFORE the work, and rebuilt from scratch every poll: a channel that drops out of
        // the active set (deferred topic, held owner channel, closed member) must stop counting as
        // polled on the very next tick, or its frozen cursor is fair game for compaction again.
        _lastPolledFiles.Clear();

        foreach (var channel in channels)
            _lastPolledFiles.Add(channel.FilePath);

        foreach (var channel in channels)
        {
            try
            {
                var entries = Poll_OneChannel(channel, nowUtc, truncatedFiles, heldTrailingEntryFiles);

                if (entries.Count > 0)
                    completedAppends.Add(CompletedChannelAppend_Factory.Create(channel, entries));
            }
            catch (Exception ex)
            {
                // One unreadable channel must never stop the others. Before this, an IOException on
                // any file threw out of Poll and DISCARDED the appends already built for earlier
                // channels — whose offsets had advanced, so those entries were never mirrored and
                // never came back. Reported to the caller, which owns the log; the next poll retries
                // this file from the same offset, so nothing here is lost either.
                unreadableFiles.Add($"{channel.FilePath} — {ex.GetType().Name}: {ex.Message}");
            }
        }

        return TailerPollResult_Factory.Create(completedAppends, truncatedFiles, unreadableFiles, heldTrailingEntryFiles);
    }

    /// <summary>Runs with <see cref="_statesLock"/> HELD.</summary>
    IReadOnlyList<IChannelEntry> Poll_OneChannel(IDiscoveredChannel channel, DateTime nowUtc, List<string> truncatedFiles, List<string> heldTrailingEntryFiles)
    {
        var fileLength = Get_FileLength_OrNull(channel.FilePath);
        if (fileLength == null)
            return [];

        if (!_states.TryGetValue(channel.FilePath, out var state))
        {
            state = new FileTailState { Offset = fileLength.Value };
            _states[channel.FilePath] = state;
            return [];
        }

        // Anything still unconfirmed was emitted on an earlier poll and never acknowledged — a
        // Telegram send that failed, or a tick that died before it could confirm. It goes back to
        // the FRONT of the pending text and is re-emitted, in order, ahead of anything new. This is
        // what makes the mirror at-least-once: entries leave only when the bridge says they were
        // delivered, never as a side effect of an offset that ran ahead of the send.
        Rewind_Unconfirmed(state, nowUtc);

        if (fileLength.Value < state.Offset)
        {
            state.Offset = fileLength.Value;
            state.Pending.Clear();
            state.QuietSinceUtc = nowUtc;
            truncatedFiles.Add(channel.FilePath);
            return [];
        }

        if (fileLength.Value > state.Offset)
        {
            var (text, byteCount) = Read_From(channel.FilePath, state.Offset, fileLength.Value);
            state.Pending.Append(text);

            // ADVANCE BY WHAT WAS READ, NEVER BY THE FILE LENGTH. Stream.Read may legally return
            // fewer bytes than asked for; taking the length on trust moved the cursor past bytes
            // nobody had seen, and nothing ever read them again — silent loss, in the one component
            // whose contract is at-least-once delivery to the owner's phone, and invisible
            // afterwards because the channel file on disk is perfectly intact.
            state.Offset += byteCount;

            // A read that returned NOTHING is not activity. Counting it as such would restart the
            // quiet stretch on every poll, and a trailing entry waiting it out would never flush.
            if (byteCount > 0)
                state.QuietSinceUtc = nowUtc;
            else
                state.QuietSinceUtc ??= nowUtc;
        }
        else
        {
            state.QuietSinceUtc ??= nowUtc;
        }

        return Extract_CompleteEntries(state, channel.FilePath, nowUtc, _trailingEntryQuiet, heldTrailingEntryFiles);
    }

    /// <summary>
    /// The bridge delivered (or deliberately dropped) the entries last emitted for this file, so the
    /// persisted cursor may finally move past them.
    /// </summary>
    public void Confirm_Append(string channelFilePath)
    {
        lock (_statesLock)
        {
            if (!_states.TryGetValue(channelFilePath, out var state))
                return;

            state.Unconfirmed.Clear();
        }
    }

    public bool Has_UndeliveredEntries(string channelFilePath, out string? unevaluableReason)
    {
        lock (_statesLock)
        {
            unevaluableReason = null;

            if (!_states.TryGetValue(channelFilePath, out var state))
            {
                // A REAL answer, not a silent fail-open: no state means this tailer has never read
                // this file, so it is owed nothing by it. The caller's own poll guard has already
                // refused anything the last poll skipped.
                return false;
            }

            // PENDING COUNTS TOO, and testing Unconfirmed alone was a silent-loss bug: every poll
            // starts by draining Unconfirmed back into Pending (Rewind_Unconfirmed), and bytes read
            // but not yet emitted — a trailing entry still serving its quiet period — live in
            // Pending and nowhere else. The compaction guard asking this question was told "nothing
            // owed", rewrote the file underneath the tailer, and Set_Offset then discarded exactly
            // those bytes: the newest entry vanished from Telegram for good while the file on disk
            // stayed intact.
            //
            // The cost, taken deliberately: a file whose last byte is not a newline holds Pending
            // forever and so never compacts. That file's trailing entry is already permanently
            // unemitted (Extract_CompleteEntries needs Ends_WithLineBreak), so this trades a file
            // that grows — visible, recoverable — for a delivery that disappears silently.
            // Header-less noise does not stick: it is cleared once the file has been quiet.
            if (state.Unconfirmed.Length > 0 || state.Pending.Length > 0)
                return true;

            // THE FILE IS THE THIRD PLACE AN ENTRY CAN BE OWED FROM, and it was the blind spot that
            // made the two clauses above insufficient. The mirror tick appends to channel files
            // BETWEEN the poll and compaction, on the same thread — Check_LedgerHealth_Async,
            // Check_ChannelShapes_Async and Push_PeriodicStatus_Async all write entries after the
            // poll has run. Those bytes are in no buffer here, so both clauses answered false;
            // compaction then kept the new entry among the newest 45, returned EOF, and Set_Offset
            // parked the cursor past it. Gone from Telegram, intact on disk, and invisible to the
            // log — the per-entry log line lives inside the mirror send that never happened.
            return Has_BytesPastCursor(state, channelFilePath, out unevaluableReason);
        }
    }

    /// <summary>
    /// Runs with <see cref="_statesLock"/> HELD. Costs one stat per channel per tick, against a
    /// compactor that reads and rewrites whole files — and it is what makes this predicate answer
    /// the question its contract asks, rather than only the part held in memory.
    /// <para>
    /// WHEN IT CANNOT TELL, IT SAYS SO AND REFUSES — on the hazard, not on an incident. The first
    /// draft of this clause answered "clear" for a file it could not stat, which WOULD have let
    /// compaction rewrite a channel on the strength of a question nobody managed to ask. It never
    /// shipped: it was caught on the branch that wrote it, and this comment does not get to borrow
    /// the authority of a failure that never happened. The rule stands without it — a guard that
    /// cannot evaluate its predicate must not invent either answer. It names the predicate that
    /// failed, the caller logs that, and compaction does not run; compaction is optional and retries
    /// next tick, while a lost entry does not come back.
    /// </para>
    /// </summary>
    static bool Has_BytesPastCursor(FileTailState state, string channelFilePath, out string? unevaluableReason)
    {
        unevaluableReason = null;

        long? fileLength;

        try
        {
            fileLength = Get_FileLength_OrNull(channelFilePath);
        }
        catch (Exception ex)
        {
            unevaluableReason = $"could not stat the channel file — {ex.GetType().Name}: {ex.Message}";
            return true;
        }

        if (fileLength == null)
        {
            // The file is GONE while this tailer still holds a cursor into it. Whether it owed a
            // delivery is now unanswerable, and a channel file vanishing is worth a line of its own.
            unevaluableReason = "the channel file does not exist, so what it still owed cannot be determined";
            return true;
        }

        // A file SHORTER than the cursor is the append-only protocol breaking rather than a delivery
        // owed: the next poll reports it as truncated and re-anchors.
        return fileLength.Value > state.Offset;
    }

    public bool Was_PolledInLastPoll(string channelFilePath)
    {
        lock (_statesLock)
        {
            return _lastPolledFiles.Contains(channelFilePath);
        }
    }

    void Rewind_Unconfirmed(FileTailState state, DateTime nowUtc)
    {
        if (state.Unconfirmed.Length == 0)
            return;

        state.Pending.Insert(0, state.Unconfirmed.ToString());
        state.Unconfirmed.Clear();

        // Those entries were already judged COMPLETE once; making them serve the quiet period a
        // second time would delay every retry by the whole of it for no new information. Backdated
        // rather than flagged, so there is one rule for "is this file quiet" and not two.
        state.QuietSinceUtc = nowUtc - _trailingEntryQuiet;
    }

    public void Set_Offset(string channelFilePath, long offset)
    {
        lock (_statesLock)
        {
            if (!_states.TryGetValue(channelFilePath, out var state))
            {
                _states[channelFilePath] = new FileTailState { Offset = offset };
                return;
            }

            // Pending bytes belong to the pre-rewrite file — dropping them is the point: everything
            // up to the new length has already been mirrored. Unconfirmed bytes go with them: they
            // point into a file that no longer exists in that shape. The bridge does not compact a
            // channel that still owes a delivery, so this discards nothing in practice.
            state.Offset = offset;
            state.Pending.Clear();
            state.Unconfirmed.Clear();

            // NULL, not "now": the rewrite discarded everything that could have been released, so
            // there is nothing for a quiet stretch to be about until the next poll looks at the file.
            state.QuietSinceUtc = null;
        }
    }

    public IReadOnlyDictionary<string, long> Get_OffsetsSnapshot()
    {
        Dictionary<string, long> snapshot = [];

        // This runs on the INBOUND loop while the mirror loop polls. Enumerating the dictionary and
        // reading those StringBuilders unlocked is the race: an exception out of the enumerator that
        // presents as a Telegram fault, or an offset persisted from a half-applied poll.
        lock (_statesLock)
        {
            foreach (var pair in _states)
            {
                // Un-emitted pending bytes stay "unread" in the snapshot so a restart re-reads them —
                // and so do UNCONFIRMED ones, which is what carries at-least-once across a restart: a
                // send that never landed is re-read and re-sent by the next process rather than lost
                // with the memory of the one that failed.
                snapshot[pair.Key] = pair.Value.Offset
                    - Encoding.UTF8.GetByteCount(pair.Value.Pending.ToString())
                    - Encoding.UTF8.GetByteCount(pair.Value.Unconfirmed.ToString());
            }
        }

        return snapshot;
    }

    static IReadOnlyList<IChannelEntry> Extract_CompleteEntries(
        FileTailState state,
        string channelFilePath,
        DateTime nowUtc,
        TimeSpan trailingEntryQuiet,
        List<string> heldTrailingEntryFiles)
    {
        var pendingText = state.Pending.ToString();

        if (pendingText.Length == 0)
            return [];

        var lines = pendingText.Split('\n');
        var headerLineIndexes = Find_HeaderLineIndexes(lines);

        var quiet = Is_Quiet(state, nowUtc, trailingEntryQuiet);

        if (headerLineIndexes.Count == 0)
        {
            // No entry header in the pending text. Once quiet, it is preamble/noise — drop it.
            if (quiet)
                state.Pending.Clear();

            return [];
        }

        var flushTrailingEntry = quiet && Ends_WithLineBreak(pendingText);

        // HELD, AND SAID SO. Quiet but unterminated means the trailing entry parses and cannot be
        // released: the flush is the last entry's only exit, and it requires the line break. The
        // channel sits invisible until somebody appends a header — the sender believing it was
        // delivered — so the tailer reports the condition even though it will not act on it.
        //
        // It does not flush anyway, and the reason is what the remainder would cost. If this fired
        // while a write was merely slow, the rest of the entry would arrive with no header of its
        // own, be read as noise by the branch above, and be DROPPED. A truncated entry plus a
        // silently discarded tail is strictly worse than the delay it would cure, and nothing in the
        // content can tell the two cases apart — the newline cannot either, since a writer pausing
        // after any completed line leaves the file ending in one mid-entry.
        if (quiet && !flushTrailingEntry)
            heldTrailingEntryFiles.Add(channelFilePath);

        int consumedUpToLine;
        if (flushTrailingEntry)
            consumedUpToLine = lines.Length;
        else
            consumedUpToLine = headerLineIndexes[headerLineIndexes.Count - 1];

        if (consumedUpToLine == 0)
            return [];

        var completeText = string.Join('\n', lines.Take(consumedUpToLine));
        var entries = ChannelEntry_Parser.Parse_All(completeText);

        if (entries.Count == 0 && !flushTrailingEntry)
            return [];

        var remainingText = string.Join('\n', lines.Skip(consumedUpToLine));

        // The exact bytes these entries occupy — remainingText is a suffix of pendingText, so the
        // difference is the consumed prefix with no re-joining guesswork about line breaks.
        var consumedText = pendingText[..(pendingText.Length - remainingText.Length)];

        state.Pending.Clear();
        state.Pending.Append(remainingText);

        // Only text that produced ENTRIES is held for confirmation. Consumed noise (a preamble, a
        // header-less block) has nothing to deliver, so holding it would freeze the cursor waiting
        // for an acknowledgement that can never come.
        if (entries.Count > 0)
            state.Unconfirmed.Append(consumedText);

        return entries;
    }

    static IReadOnlyList<int> Find_HeaderLineIndexes(IReadOnlyList<string> lines)
    {
        List<int> indexes = [];

        for (var i = 0; i < lines.Count; i++)
        {
            if (ChannelEntry_Parser.Is_HeaderLine(lines[i]))
                indexes.Add(i);
        }

        return indexes;
    }

    /// <summary>
    /// WHETHER THIS FILE HAS STOPPED BEING WRITTEN, as far as anyone here can tell — the one question
    /// the trailing entry's release turns on, asked in ONE place so it cannot grow a second answer.
    /// <para>
    /// A null stretch is NOT quiet: it means no poll has looked at the file yet, and answering "quiet"
    /// from an observation nobody made is the shape of the failure this whole class documents.
    /// </para>
    /// </summary>
    static bool Is_Quiet(FileTailState state, DateTime nowUtc, TimeSpan trailingEntryQuiet)
    {
        return state.QuietSinceUtc != null && nowUtc - state.QuietSinceUtc.Value >= trailingEntryQuiet;
    }

    static bool Ends_WithLineBreak(string text)
    {
        return text.EndsWith('\n') || text.EndsWith("\r\n", StringComparison.Ordinal);
    }

    static long? Get_FileLength_OrNull(string filePath)
    {
        var info = new FileInfo(filePath);

        if (!info.Exists)
            return null;

        return info.Length;
    }

    /// <summary>
    /// Reads the delta and reports how many bytes it ACTUALLY got — see
    /// <see cref="StreamDelta_Reader"/> for why the count is the whole point.
    /// </summary>
    static (string Text, long ByteCount) Read_From(string filePath, long fromOffset, long toOffset)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(fromOffset, SeekOrigin.Begin);

        return StreamDelta_Reader.Read_Delta(stream, toOffset - fromOffset);
    }
}
