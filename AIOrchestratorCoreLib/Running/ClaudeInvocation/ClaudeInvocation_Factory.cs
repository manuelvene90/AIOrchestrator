namespace AIOrchestratorCoreLib.Running.ClaudeInvocation;

public static class ClaudeInvocation_Factory
{
    public static IClaudeInvocation Create(string executable, IReadOnlyList<string> leadingArguments)
    {
        if (string.IsNullOrWhiteSpace(executable))
            throw new ArgumentException("Claude executable must be non-empty");

        return new ClaudeInvocationModel(executable, leadingArguments);
    }
}
