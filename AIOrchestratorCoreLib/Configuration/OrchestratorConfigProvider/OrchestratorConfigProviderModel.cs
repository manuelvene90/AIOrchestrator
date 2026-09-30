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
    string? _cachedConfigText;
    string? _cachedSecretsText;

    /// <summary>
    /// How long after its last write a file's stamp is not trusted on its own. Windows moves a
    /// last-write time only every ~15.6 ms, so two writes inside one tick carry the SAME stamp, and a
    /// cache keyed on the stamp keeps the first. Found on the Windows CI runner (2026-09-30): the
    /// engine read config.json while being built, the test rewrote it inside the same tick, and the
    /// provider served the stale `highRiskConfirmation` for the whole test
    /// (HighRiskAndDeadlineProbeTests, 2 of the last 6 runs). Two seconds covers every filesystem's
    /// granularity this runs on, FAT's 2 s included.
    /// </summary>
    static readonly TimeSpan RACY_STAMP_WINDOW = TimeSpan.FromSeconds(2);

    /// <summary>
    /// THE STAMP DECIDES WHEN IT IS OLD ENOUGH TO BE TRUSTED; THE TEXT DECIDES WHILE IT IS YOUNG. This is
    /// git's "racily clean" rule: a file written within <see cref="RACY_STAMP_WINDOW"/> of now may still
    /// be rewritten inside the same stamp, so its text is compared as well. Equal text keeps the SAME
    /// instance — callers read reference equality as "the config changed", so reloading on every call
    /// inside the window would announce two seconds of changes that never happened.
    /// </summary>
    public IOrchestratorConfig Get_Current()
    {
        lock (_lock)
        {
            var configStamp = Get_FileStampUtc(_paths.ConfigFile);
            var secretsStamp = Get_FileStampUtc(_paths.SecretsFile);

            var stampsMatch = _cached != null
                && configStamp == _cachedConfigStampUtc
                && secretsStamp == _cachedSecretsStampUtc;

            if (stampsMatch && !Is_Racy(configStamp, secretsStamp))
                return _cached!;

            var (configReadable, configText) = Try_Read_Text(_paths.ConfigFile);
            var (secretsReadable, secretsText) = Try_Read_Text(_paths.SecretsFile);

            // AN UNREADABLE FILE IS UNKNOWN, NEVER A CHANGE. With the stamps unchanged, the cached config
            // is the last good reading and it stands: reloading here would hand the loader a file held
            // open without sharing, which it reads as EMPTY — and the engine would lose its Telegram
            // chat id for as long as the file was held. Caught the day this rule was written
            // (StatusScreenshotsCommandTests.Screens_WithConfigJsonHeld_…, red twice in a row).
            if (stampsMatch && (!configReadable || !secretsReadable))
                return _cached!;

            if (stampsMatch && configText == _cachedConfigText && secretsText == _cachedSecretsText)
                return _cached!;

            _cached = OrchestratorConfig_Loader.Load_OrEmpty(_paths, _log);
            _cachedConfigStampUtc = configStamp;
            _cachedSecretsStampUtc = secretsStamp;
            _cachedConfigText = configText;
            _cachedSecretsText = secretsText;

            return _cached
                ?? throw new Exception($"Config cache unexpectedly null after reload from '{_paths.ConfigFile}'");
        }
    }

    static bool Is_Racy(DateTime configStampUtc, DateTime secretsStampUtc)
    {
        var newest = configStampUtc > secretsStampUtc ? configStampUtc : secretsStampUtc;

        return DateTime.UtcNow - newest < RACY_STAMP_WINDOW;
    }

    /// <summary>
    /// The file's text for the racy comparison: (true, null) when the file is absent, (false, null) when
    /// it exists and cannot be read right now. ONE attempt, no retry loop — this runs on every tick's
    /// config read for two seconds after a write, and a retry budget spent here is a slow tick; an
    /// unreadable file simply keeps the cache (see Get_Current). This read never decides what the config
    /// IS, only whether to ask the loader again.
    /// </summary>
    static (bool Readable, string? Text) Try_Read_Text(string filePath)
    {
        if (!File.Exists(filePath))
            return (true, null);

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            return (true, reader.ReadToEnd());
        }
        catch (IOException)
        {
            return (false, null);
        }
        catch (UnauthorizedAccessException)
        {
            return (false, null);
        }
    }

    static DateTime Get_FileStampUtc(string filePath)
    {
        if (!File.Exists(filePath))
            return DateTime.MinValue;

        return File.GetLastWriteTimeUtc(filePath);
    }
}
