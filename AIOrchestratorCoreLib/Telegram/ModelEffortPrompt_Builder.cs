namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// The button prompt a bare /model or /effort is answered with.
///
/// The owner, 2026-09-09: "even better if I just write the command and then I get prompted with
/// the possible options so I don't have to worry about spelling mistakes." So the prompt is the
/// catalogue as buttons, and in a CREW every row offers one value for BOTH roles — supervisor on
/// the left, implementers on the right — so a single tap settles the value and who it is for,
/// with no second prompt asking which role was meant. A basic orchestration has one session and
/// gets one set of buttons, all on the implementer slot the launcher spawns a solo from.
///
/// Labels stay short enough that the option layout's numbering fallback is never needed: a phone
/// shows two of these across without shrinking them to slivers.
/// </summary>
public static class ModelEffortPrompt_Builder
{
    const string BASIC_MODEL_TEXT = "Which model? The session respawns on the tap and picks up from the channel.";
    const string CREW_MODEL_TEXT = "Which model? Left = supervisor, right = implementers. That role's sessions respawn on the tap and pick up from the channel.";
    const string BASIC_EFFORT_TEXT = "Which effort level? The session respawns on the tap and picks up from the channel.";
    const string CREW_EFFORT_TEXT = "Which effort level? Left = supervisor, right = implementers. That role's sessions respawn on the tap and pick up from the channel.";

    /// <summary>Two per row for the four models; the five levels go three then two.</summary>
    static readonly IReadOnlyList<int> MODEL_ROW_SIZES = [2, 2];
    static readonly IReadOnlyList<int> EFFORT_ROW_SIZES = [3, 2];

    public static (string Text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> Rows) Build_ModelPrompt(string orchId, bool hasSupervisor)
    {
        if (hasSupervisor)
            return (CREW_MODEL_TEXT, Build_PairedRows(ModelEffortKinds.Model, orchId, Model_Choices()));

        return (BASIC_MODEL_TEXT, Build_RoleRows(ModelEffortKinds.Model, orchId, ModelEffortButton_Data.IMPLEMENTER_ROLE, Model_Choices(), MODEL_ROW_SIZES));
    }

    public static (string Text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> Rows) Build_EffortPrompt(string orchId, bool hasSupervisor)
    {
        if (hasSupervisor)
            return (CREW_EFFORT_TEXT, Build_PairedRows(ModelEffortKinds.Effort, orchId, Effort_Choices()));

        return (BASIC_EFFORT_TEXT, Build_RoleRows(ModelEffortKinds.Effort, orchId, ModelEffortButton_Data.IMPLEMENTER_ROLE, Effort_Choices(), EFFORT_ROW_SIZES));
    }

    /// <summary>A role was typed but no value ("/model sup"): that role's buttons only.</summary>
    public static (string Text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> Rows) Build_ModelPrompt_ForRole(string orchId, string role)
    {
        return (BASIC_MODEL_TEXT, Build_RoleRows(ModelEffortKinds.Model, orchId, role, Model_Choices(), MODEL_ROW_SIZES));
    }

    /// <summary>A role was typed but no value ("/effort sup"): that role's buttons only.</summary>
    public static (string Text, IReadOnlyList<IReadOnlyList<(string Data, string Label)>> Rows) Build_EffortPrompt_ForRole(string orchId, string role)
    {
        return (BASIC_EFFORT_TEXT, Build_RoleRows(ModelEffortKinds.Effort, orchId, role, Effort_Choices(), EFFORT_ROW_SIZES));
    }

    static IReadOnlyList<(string Value, string Label)> Model_Choices()
    {
        List<(string Value, string Label)> choices = [];

        foreach (var (alias, label) in ModelChoices.ALL)
            choices.Add((alias, label));

        return choices;
    }

    static IReadOnlyList<(string Value, string Label)> Effort_Choices()
    {
        List<(string Value, string Label)> choices = [];

        foreach (var level in EffortLevels.ALL)
            choices.Add((level, level));

        return choices;
    }

    /// <summary>One row per choice: [sup value | imp value].</summary>
    static IReadOnlyList<IReadOnlyList<(string Data, string Label)>> Build_PairedRows(ModelEffortKinds kind, string orchId, IReadOnlyList<(string Value, string Label)> choices)
    {
        List<IReadOnlyList<(string Data, string Label)>> rows = [];

        foreach (var (value, label) in choices)
        {
            rows.Add(
            [
                (ModelEffortButton_Data.Build(kind, orchId, ModelEffortButton_Data.SUPERVISOR_ROLE, value), $"{ModelEffortButton_Data.SUPERVISOR_ROLE} {label}"),
                (ModelEffortButton_Data.Build(kind, orchId, ModelEffortButton_Data.IMPLEMENTER_ROLE, value), $"{ModelEffortButton_Data.IMPLEMENTER_ROLE} {label}"),
            ]);
        }

        return rows;
    }

    /// <summary>The choices for ONE role, chunked into rows of the given sizes.</summary>
    static IReadOnlyList<IReadOnlyList<(string Data, string Label)>> Build_RoleRows(
        ModelEffortKinds kind, string orchId, string role, IReadOnlyList<(string Value, string Label)> choices, IReadOnlyList<int> rowSizes)
    {
        if (rowSizes.Sum() != choices.Count)
            throw new Exception($"row sizes {string.Join("+", rowSizes)} do not add up to the {choices.Count} choices for {kind}");

        List<IReadOnlyList<(string Data, string Label)>> rows = [];
        var next = 0;

        foreach (var size in rowSizes)
        {
            List<(string Data, string Label)> row = [];

            for (var index = 0; index < size; index++)
            {
                var (value, label) = choices[next];
                row.Add((ModelEffortButton_Data.Build(kind, orchId, role, value), label));
                next++;
            }

            rows.Add(row);
        }

        return rows;
    }
}
