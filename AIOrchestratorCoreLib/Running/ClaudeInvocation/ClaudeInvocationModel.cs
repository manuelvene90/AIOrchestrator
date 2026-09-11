namespace AIOrchestratorCoreLib.Running.ClaudeInvocation;

internal sealed class ClaudeInvocationModel(string executable, IReadOnlyList<string> leadingArguments) : IClaudeInvocation
{
    public string Executable { get; } = executable;
    public IReadOnlyList<string> LeadingArguments { get; } = leadingArguments;
}
