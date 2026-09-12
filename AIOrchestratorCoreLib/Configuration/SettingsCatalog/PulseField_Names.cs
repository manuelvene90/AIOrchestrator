namespace AIOrchestratorCoreLib.Configuration.SettingsCatalog;

/// <summary>
/// THE EIGHT FIELDS A PULSE MAY CARRY, in the order the shipped default lists them. The words, not
/// the builders: `pulse.fields` is an ORDERED LIST setting whose values are these, and plan 03 makes
/// TopicStatusLine_Builder.Build iterate the resolved list calling one private per word. Registered
/// here in plan 02 so the setting can be validated before anything reads it — a list validated
/// against a builder that does not iterate it yet is still a list that cannot hold a typo.
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

    public static readonly IReadOnlyList<string> ALL =
        [WAITING_ON_YOU, SUPERVISOR, MEMBERS, CLOSED_COUNT, LAST_EVENT, MERGED, MODEL_EFFORT, UPDATED];
}
