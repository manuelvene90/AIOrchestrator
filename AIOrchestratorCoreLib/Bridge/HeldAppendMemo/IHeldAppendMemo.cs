using AIOrchestratorCoreLib.Channels.ChannelEntry;

namespace AIOrchestratorCoreLib.Bridge.HeldAppendMemo;

/// <summary>
/// WHICH ENTRIES OF A HELD APPEND THE OWNER ALREADY HAS.
///
/// <para>
/// A channel append can be delivered in part: the mirror sends entries one by one, and the
/// one-question-at-a-time hold can stop it half way. The append is then left unconfirmed on purpose,
/// so the tailer re-emits the WHOLE of it on the next poll — and this is what stops the entries that
/// already reached the phone from arriving a second time. A duplicate here would be the waterfall
/// coming back through the door built to stop it.
/// </para>
/// <para>
/// IT REMEMBERS ENTRIES, NOT A COUNT, and that is the whole point of it being a component.
/// </para>
/// </summary>
public interface IHeldAppendMemo
{
    /// <summary>Whether this exact entry of this channel's held append already reached the owner.</summary>
    bool Was_Delivered(string channelFilePath, IChannelEntry entry);

    /// <summary>Records everything delivered from the append being held, replacing any earlier note.</summary>
    void Remember_Delivered(string channelFilePath, IReadOnlyList<IChannelEntry> delivered);

    /// <summary>Drops the note — the append it described is settled, or the file it described is gone.</summary>
    void Forget(string channelFilePath);
}
