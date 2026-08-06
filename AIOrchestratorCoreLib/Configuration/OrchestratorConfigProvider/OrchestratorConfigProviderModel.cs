using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;

internal sealed class OrchestratorConfigProviderModel(ISupervisionPaths paths) : IOrchestratorConfigProvider
{
    readonly ISupervisionPaths _paths = paths;
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
                _cached = OrchestratorConfig_Loader.Load_OrEmpty(_paths);
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
