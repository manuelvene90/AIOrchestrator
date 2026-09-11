namespace AIOrchestratorCoreLib.Running.ClaudeInvocation;

/// <summary>
/// How to start `claude` on this machine: the executable and any arguments that come BEFORE the
/// CLI's own. The bridge's flags are appended after these, so swapping the invocation swaps the
/// binary and nothing else — which is how the tests run the fake.
/// </summary>
public interface IClaudeInvocation
{
    string Executable { get; }
    IReadOnlyList<string> LeadingArguments { get; }
}
