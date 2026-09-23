namespace AIOrchestratorCoreLib.Configuration.SettingsCatalog;

/// <summary>
/// THE NINE FIELDS A PULSE MAY CARRY, in the order the shipped default lists them. The words, not
/// the builders: `pulse.fields` is an ORDERED LIST setting whose values are these, and plan 03 makes
/// TopicStatusLine_Builder.Build iterate the resolved list calling one private per word. Registered
/// here in plan 02 so the setting can be validated before anything reads it — a list validated
/// against a builder that does not iterate it yet is still a list that cannot hold a typo.
///
/// <para>
/// <see cref="PROGRESS"/> IS THE NINTH, and the only one with a place outside the list's order (owner,
/// 2026-09-23: *"I want the pulse message have the task count 1/12 (8%) at the very top of the message
/// because it's the most important information. And I don't want to have useless words like 1/23
/// merged 4%. Just 1/23 (4%)."*). It is the ledger reading <see cref="MERGED"/> carries, with the label
/// and the "unchanged" clause gone, and when it is FIRST in the list it is drawn above the header — the
/// first line of the message, which is what a notification preview shows. Appended to <see cref="ALL"/>
/// rather than slotted beside MERGED: ALL is also the validator's error text, and its order is not a
/// promise to anyone.
/// </para>
/// </summary>
public static class PulseField_Names
{
    public const string WAITING_ON_YOU = "waitingOnYou";
    public const string SUPERVISOR = "supervisor";
    public const string MEMBERS = "members";
    public const string CLOSED_COUNT = "closedCount";
    public const string LAST_EVENT = "lastEvent";
    public const string MERGED = "merged";
    public const string MODEL_EFFORT = "modelEffort";
    public const string UPDATED = "updated";
    public const string PROGRESS = "progress";

    public static readonly IReadOnlyList<string> ALL =
        [WAITING_ON_YOU, SUPERVISOR, MEMBERS, CLOSED_COUNT, LAST_EVENT, MERGED, MODEL_EFFORT, UPDATED, PROGRESS];
}
