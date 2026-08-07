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

        foreach (var channel in channels)
        {
            var fileLength = Get_FileLength_OrNull(channel.FilePath);
            if (fileLength == null)
                continue;

            if (!_states.TryGetValue(channel.FilePath, out var state))
            {
                state = new FileTailState { Offset = fileLength.Value };
                _states[channel.FilePath] = state;
                continue;
            }

            if (fileLength.Value < state.Offset)
            {
                state.Offset = fileLength.Value;
                state.Pending.Clear();
                state.QuietPolls = 0;
                truncatedFiles.Add(channel.FilePath);
                continue;
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

            var entries = Extract_CompleteEntries(state);
            if (entries.Count > 0)
                completedAppends.Add(CompletedChannelAppend_Factory.Create(channel, entries));
        }

        return TailerPollResult_Factory.Create(completedAppends, truncatedFiles);
    }

    public void Set_Offset(string channelFilePath, long offset)
    {
        if (!_states.TryGetValue(channelFilePath, out var state))
        {
            _states[channelFilePath] = new FileTailState { Offset = offset };
            return;
        }

        // Pending bytes belong to the pre-rewrite file — dropping them is the point: everything
        // up to the new length has already been mirrored.
        state.Offset = offset;
        state.Pending.Clear();
        state.QuietPolls = 0;
    }

    public IReadOnlyDictionary<string, long> Get_OffsetsSnapshot()
    {
        Dictionary<string, long> snapshot = [];

        foreach (var pair in _states)
        {
            // Un-emitted pending bytes stay "unread" in the snapshot so a restart re-reads them.
            snapshot[pair.Key] = pair.Value.Offset - Encoding.UTF8.GetByteCount(pair.Value.Pending.ToString());
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
        state.Pending.Clear();
        state.Pending.Append(remainingText);

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
