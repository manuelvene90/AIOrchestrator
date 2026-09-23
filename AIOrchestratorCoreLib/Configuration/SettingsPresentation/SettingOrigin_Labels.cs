using AIOrchestratorCoreLib.Configuration.SettingsCatalog;

namespace AIOrchestratorCoreLib.Configuration.SettingsPresentation;

/// <summary>
/// THE WORDS FOR WHERE A VALUE CAME FROM — one copy for all three renderers (CLAUDE.md decision 12). The
/// origin is the half of a settings row the owner actually asks about: "why is my phone on reactions when
/// the catalogue says ticks" is answered by "from preset quiet" and by nothing else on the screen (spec
/// §6.2), so a WPF label saying "preset" while the Telegram menu says "from preset quiet" would be the
/// same fact told two ways, and only one of them useful.
///
/// <para>
/// THE PRESET NAME IS A PARAMETER, NOT A LOOKUP, for the reason <see cref="Settings_Resolver"/> takes its
/// layers as parameters: a formatter that read config.json to find the preset word would be a formatter
/// with a disk dependency, untestable without a file and wrong the moment the file changed between the
/// resolve and the label. The caller that resolved the preset is the one that knows its name.
/// </para>
/// </summary>
public static class SettingOrigin_Labels
{
    public const string SHIPPED_DEFAULT = "shipped default";

    /// <summary>
    /// config.json (or, for an Orchestration-scoped row read without a session, still config.json). "Here"
    /// is this machine, which is where every renderer writes — so the word is true on the phone, the web
    /// page and the desktop alike.
    /// </summary>
    public const string CONFIG_FILE = "set here";

    /// <summary>
    /// An orchestration's own session.json value, the top rung. Only a view that carries a session can show
    /// it, and under D3 that view is read-only.
    /// </summary>
    public const string SESSION = "set for this orchestration";

    const string PRESET_PREFIX = "from preset ";

    /// <summary>
    /// "shipped default", "from preset classic", "set here" or "set for this orchestration". A
    /// <see cref="SettingOrigins.Preset"/> origin with no preset name is a caller's bug — some layer handed
    /// the resolver a preset tree and then forgot which one — and it throws rather than printing "from
    /// preset " with nothing after it.
    /// </summary>
    public static string Describe(SettingOrigins origin, string presetName)
    {
        return origin switch
        {
            SettingOrigins.ShippedDefault => SHIPPED_DEFAULT,
            SettingOrigins.Preset => PRESET_PREFIX + Require_PresetName(presetName),
            SettingOrigins.ConfigFile => CONFIG_FILE,
            SettingOrigins.Session => SESSION,
            _ => throw new InvalidOperationException($"Unhandled SettingOrigins: {origin}"),
        };
    }

    static string Require_PresetName(string presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName))
            throw new ArgumentException($"A value resolved from a preset but the preset name is '{presetName}' — the caller must say which preset it resolved.");

        return presetName;
    }
}
