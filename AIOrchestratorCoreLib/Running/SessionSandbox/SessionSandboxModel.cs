using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running.ClaudeInvocation;

namespace AIOrchestratorCoreLib.Running.SessionSandbox;

/// <summary>
/// Reads the ceiling live (like every other runner setting: an edit to config.json applies to the
/// next spawn) and asks the machine once whether it can enforce it.
///
/// <para>
/// EVERY REASON IT DOES NOT WRAP IS SAID OUT LOUD, EXACTLY ONCE. Decision 21's corollary: a guard
/// that cannot evaluate its predicate says which predicate failed and allows — silence here would
/// mean an operator believing their sessions are capped while nothing caps them, which is worse
/// than no cap at all. Once, because a line per turn is a waterfall (decision 14).
/// </para>
/// </summary>
internal sealed class SessionSandboxModel(IOrchestratorConfigProvider configProvider, Func<bool> isSystemdRunAvailable, IOrchestrationLog log) : ISessionSandbox
{
    readonly IOrchestratorConfigProvider _configProvider = configProvider;
    readonly Func<bool> _isSystemdRunAvailable = isSystemdRunAvailable;
    readonly IOrchestrationLog _log = log;

    readonly Lock _lock = new();
    readonly HashSet<string> _saidOnce = [];

    public IClaudeInvocation Wrap(IClaudeInvocation plain)
    {
        string memoryMax;

        try
        {
            memoryMax = _configProvider.Get_Current().Runners.SessionMemoryMax;
        }
        catch (Exception ex)
        {
            Say_Once("config", $"The session memory ceiling could not be read from config.json, so sessions run UNCAPPED and one of them can still take this host down: {ex.Message}");
            return plain;
        }

        if (MemorySize_Parser.Means_NoLimit(memoryMax))
        {
            Say_Once("off", $"Session memory ceiling is OFF ('{RunnerConfigs.RunnerConfigs_Json.RUNNERS_KEY}.{RunnerConfigs.RunnerConfigs_Json.SESSION_MEMORY_MAX_KEY}' = '{memoryMax}') — sessions share this host's memory and an allocation in one can kill all of them");
            return plain;
        }

        if (MemorySize_Parser.Halve_OrNull(memoryMax) == null)
        {
            Say_Once("unreadable", $"'{RunnerConfigs.RunnerConfigs_Json.RUNNERS_KEY}.{RunnerConfigs.RunnerConfigs_Json.SESSION_MEMORY_MAX_KEY}' = '{memoryMax}' is not a size this host can read (expected e.g. '3G', '3072M') — sessions run UNCAPPED");
            return plain;
        }

        if (!_isSystemdRunAvailable())
        {
            Say_Once("absent", $"'{MemoryLimitedInvocation_Builder.SYSTEMD_RUN} --user --scope' is not usable on this machine (not installed, no user manager, or no cgroup memory controller) — sessions run UNCAPPED at their configured ceiling of {memoryMax}");
            return plain;
        }

        var wrapped = MemoryLimitedInvocation_Builder.Wrap(plain, memoryMax);

        Say_Once($"on:{memoryMax}", $"Sessions run in their own memory-limited scope: {MemoryLimitedInvocation_Builder.Describe(wrapped)} … — one that exceeds {memoryMax} dies alone");

        return wrapped;
    }

    void Say_Once(string key, string line)
    {
        lock (_lock)
        {
            if (!_saidOnce.Add(key))
                return;
        }

        _log.Log_Info("", line);
    }
}

/// <summary>The shape for a host that does not isolate its children at all — the tests' default, and Windows'.</summary>
internal sealed class NoSessionSandboxModel : ISessionSandbox
{
    public IClaudeInvocation Wrap(IClaudeInvocation plain)
    {
        return plain;
    }
}
