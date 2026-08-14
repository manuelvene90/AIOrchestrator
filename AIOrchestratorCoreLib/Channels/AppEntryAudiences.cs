namespace AIOrchestratorCoreLib.Channels;

/// <summary>
/// Who an app-authored channel entry is written for.
///
/// NOT a recipient. Every app entry lands in a channel that agents read, whatever it says, so the only
/// question one can answer is whether it ALSO reaches the owner's phone — one bit. That is why there is
/// no Both: an <see cref="Owner"/> entry already goes to both places, and a third value would be a
/// synonym for one of these two.
///
/// It is required at every call site rather than defaulted. A default is a way of not asking, and the
/// question has exactly one moment at which the answer is known — when the entry is written, by whoever
/// decided to write it. The predecessor of this enum was a list of subject STRINGS held apart from the
/// call sites, which drifted in both directions at once: four entries matched nothing written any more,
/// while a live one had never been added and was texted to the owner for a day.
/// </summary>
public enum AppEntryAudiences
{
    /// <summary>
    /// For the agent that reads this channel — a supervisor or a member. Never texted. Alerts the owner
    /// cannot act on belong here, per the owner's directive of 2026-08-10.
    /// </summary>
    Agent,

    /// <summary>
    /// News the owner asked for or needs: their request succeeded or failed, an orchestration started or
    /// closed. Lands in the channel AND on their phone.
    /// </summary>
    Owner,
}
