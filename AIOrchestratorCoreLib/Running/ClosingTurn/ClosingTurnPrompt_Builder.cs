using System.Text;
using AIOrchestratorCoreLib.Running.TurnSource;

namespace AIOrchestratorCoreLib.Running.ClosingTurn;

/// <summary>
/// The stdin prompt of a closing turn: the request id, the instruction
/// (<see cref="ClosingTurn_Words.PROMPT"/>) and the SAME reply contract every other turn is given —
/// borrowed from <see cref="PrintTurnPrompt_Builder.Describe_Contract"/> rather than restated, so a
/// supervisor closing down still learns the <c>TO:</c> addressing and a member still learns that its
/// final message IS its entry. A second copy of that contract is how the two would come to disagree
/// about where a report goes.
/// </summary>
public static class ClosingTurnPrompt_Builder
{
    public static string Build(string closingRequestId, IReadOnlyList<ITurnSource> sources)
    {
        var prompt = new StringBuilder();

        prompt.Append("[bridge turn ").Append(closingRequestId).Append("]\n");
        prompt.Append(ClosingTurn_Words.PROMPT).Append("\n\n");
        prompt.Append(PrintTurnPrompt_Builder.Describe_Contract(sources));

        return prompt.ToString();
    }
}
