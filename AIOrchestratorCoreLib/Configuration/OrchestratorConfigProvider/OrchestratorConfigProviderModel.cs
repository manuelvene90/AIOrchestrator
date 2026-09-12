using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;

/// <summary>
/// <paramref name="log"/> is threaded through to <see cref="OrchestratorConfig_Loader.Load_OrEmpty(ISupervisionPaths, IOrchestrationLog?)"/>
/// so a mistyped <c>preset</c> word is reported rather than merely swallowed — this is the provider's
/// <c>Get_Current()</c> that runs on EVERY tick with no try/catch above it, the exact path a typo
/// used to be able to take down.
/// </summary>
internal sealed class OrchestratorConfigProviderModel(ISupervisionPaths paths, IOrchestrationLog? log = null) : IOrchestratorConfigProvider
{
    readonly ISupervisionPaths _paths = paths;
    readonly IOrchestrationLog? _log = log;
    readonly Lock _lock = new();

    IOrchestratorConfig? _cached;
    DateTime _cachedConfigStampUtc;
    DateTime _cachedSecretsStampUtc;

    public IOrchestratorConfig Get_Current()
    {
        lock (_lock)
        {
            var configStamp = Get_FileStampUtc(_paths.ConfigFile);
            var secretsStamp = Get_FileStampUtc(_paths.SecretsFile);

            var isCacheFresh = _cached != null
                && configStamp == _cachedConfigStampUtc
                && secretsStamp == _cachedSecretsStampUtc;

            if (!isCacheFresh)
            {
                _cached = OrchestratorConfig_Loader.Load_OrEmpty(_paths, _log);
                _cachedConfigStampUtc = configStamp;
                _cachedSecretsStampUtc = secretsStamp;
            }

            return _cached
                ?? throw new Exception($"Config cache unexpectedly null after reload from '{_paths.ConfigFile}'");
        }
    }

    static DateTime Get_FileStampUtc(string filePath)
    {
        if (!File.Exists(filePath))
            return DateTime.MinValue;

        return File.GetLastWriteTimeUtc(filePath);
    }
}
