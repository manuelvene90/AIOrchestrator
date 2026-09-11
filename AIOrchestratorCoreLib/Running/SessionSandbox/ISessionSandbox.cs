using AIOrchestratorCoreLib.Running.ClaudeInvocation;

namespace AIOrchestratorCoreLib.Running.SessionSandbox;

/// <summary>
/// THE ONE SEAM BETWEEN "start a claude" AND "start a claude THAT CANNOT TAKE THE HOST DOWN".
/// Both runners start their process from an <see cref="IClaudeInvocation"/>, so wrapping the
/// invocation is the single point of effect for every session this host spawns — a rule enforced
/// here holds for the print runner and the stream runner without either of them restating it.
/// </summary>
public interface ISessionSandbox
{
    /// <summary>
    /// The invocation to actually start: <paramref name="plain"/> unchanged where this machine
    /// cannot isolate a child, or the same command wrapped in a memory-limited transient scope.
    /// Never throws — a sandbox that cannot be built must never stop a session from running.
    /// </summary>
    IClaudeInvocation Wrap(IClaudeInvocation plain);
}
