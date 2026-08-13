using System.Text;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Channels.DiscoveredChannel;
using AIOrchestratorCoreLib.Tailing.CompletedChannelAppend;
using AIOrchestratorCoreLib.Tailing.TailerPollResult;

namespace AIOrchestratorCoreLib.Tailing.ChannelTailer;

internal sealed class ChannelTailerModel : IChannelTailer
{
    /// <summary>Polls with no growth after which the trailing entry is considered complete.</summary>
    const int QUIET_POLLS_TO_FLUSH = 2;

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
        public int QuietPolls;
    }

    readonly Dictionary<string, FileTailState> _states = [];

    public ChannelTailerModel(IReadOnlyDictionary<string, long> persistedOffsets)
    {
        foreach (var pair in persistedOffsets)
        {
            var state = new FileTailState { Offset = pair.Value };
            _states[pair.Key] = state;
        }
    }

    public ITailerPollResult Poll(IReadOnlyList<IDiscoveredChannel> channels)
    {
        List<ICompletedChannelAppend> completedAppends = [];
        List<string> truncatedFiles = [];
        List<string> unreadableFiles = [];

        foreach (var channel in channels)
        {
            try
            {
                var entries = Poll_OneChannel(channel, truncatedFiles);

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

        return TailerPollResult_Factory.Create(completedAppends, truncatedFiles, unreadableFiles);
    }

    IReadOnlyList<IChannelEntry> Poll_OneChannel(IDiscoveredChannel channel, List<string> truncatedFiles)
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
        Rewind_Unconfirmed(state);

        if (fileLength.Value < state.Offset)
        {
            state.Offset = fileLength.Value;
            state.Pending.Clear();
            state.QuietPolls = 0;
            truncatedFiles.Add(channel.FilePath);
            return [];
        }

        if (fileLength.Value > state.Offset)
        {
            var delta = Read_From(channel.FilePath, state.Offset, fileLength.Value);
            state.Pending.Append(delta);
            state.Offset = fileLength.Value;
            state.QuietPolls = 0;
        }
        else
        {
            state.QuietPolls++;
        }

        return Extract_CompleteEntries(state);
    }

    /// <summary>
    /// The bridge delivered (or deliberately dropped) the entries last emitted for this file, so the
    /// persisted cursor may finally move past them.
    /// </summary>
    public void Confirm_Append(string channelFilePath)
    {
        if (!_states.TryGetValue(channelFilePath, out var state))
            return;

        state.Unconfirmed.Clear();
    }

    public bool Has_UndeliveredEntries(string channelFilePath)
    {
        if (!_states.TryGetValue(channelFilePath, out var state))
            return false;

        // PENDING COUNTS TOO, and testing Unconfirmed alone was a silent-loss bug: every poll starts
        // by draining Unconfirmed back into Pending (Rewind_Unconfirmed), and bytes read but not yet
        // emitted — a trailing entry still serving its quiet-poll window — live in Pending and
        // nowhere else. The compaction guard asking this question was told "nothing owed", rewrote
        // the file underneath the tailer, and Set_Offset then discarded exactly those bytes: the
        // newest entry vanished from Telegram for good while the file on disk stayed intact.
        //
        // The cost, taken deliberately: a file whose last byte is not a newline holds Pending
        // forever and so never compacts. That file's trailing entry is already permanently unemitted
        // (Extract_CompleteEntries needs Ends_WithLineBreak), so this trades a file that grows —
        // visible, recoverable — for a delivery that disappears silently. Header-less noise does not
        // stick: it is cleared after the quiet-poll window.
        return state.Unconfirmed.Length > 0 || state.Pending.Length > 0;
    }

    static void Rewind_Unconfirmed(FileTailState state)
    {
        if (state.Unconfirmed.Length == 0)
            return;

        state.Pending.Insert(0, state.Unconfirmed.ToString());
        state.Unconfirmed.Clear();

        // Those entries were already judged COMPLETE once; making them serve the quiet-poll window
        // a second time would delay every retry by two more polls for no new information.
        state.QuietPolls = QUIET_POLLS_TO_FLUSH;
    }

    public void Set_Offset(string channelFilePath, long offset)
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
        state.QuietPolls = 0;
    }

    public IReadOnlyDictionary<string, long> Get_OffsetsSnapshot()
    {
        Dictionary<string, long> snapshot = [];

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

        return snapshot;
    }

    static IReadOnlyList<IChannelEntry> Extract_CompleteEntries(FileTailState state)
    {
        var pendingText = state.Pending.ToString();

        if (pendingText.Length == 0)
            return [];

        var lines = pendingText.Split('\n');
        var headerLineIndexes = Find_HeaderLineIndexes(lines);

        if (headerLineIndexes.Count == 0)
        {
            // No entry header in the pending text. Once quiet, it is preamble/noise — drop it.
            if (state.QuietPolls >= QUIET_POLLS_TO_FLUSH)
                state.Pending.Clear();

            return [];
        }

        var flushTrailingEntry = state.QuietPolls >= QUIET_POLLS_TO_FLUSH && Ends_WithLineBreak(pendingText);

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

    static string Read_From(string filePath, long fromOffset, long toOffset)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(fromOffset, SeekOrigin.Begin);

        var buffer = new byte[toOffset - fromOffset];
        var read = stream.Read(buffer, 0, buffer.Length);

        return Encoding.UTF8.GetString(buffer, 0, read);
    }
}
