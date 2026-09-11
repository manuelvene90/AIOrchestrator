using Xunit;

namespace ClaudeContract.Tests.Live;

/// <summary>
/// A test that drives the REAL `claude` binary (costs tokens, needs a logged-in CLI, takes
/// minutes). Skipped unless <c>CLAUDE_CONTRACT_LIVE=1</c>, and the skip says so — a silent skip
/// would let a CLI upgrade pass this suite without ever being measured.
/// </summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public const string ENABLE_ENV = "CLAUDE_CONTRACT_LIVE";

    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(ENABLE_ENV) != "1")
            Skip = $"Live contract test — set {ENABLE_ENV}=1 to run it against the real claude CLI (uses --model haiku; every session it starts is stopped and removed)";
    }
}
