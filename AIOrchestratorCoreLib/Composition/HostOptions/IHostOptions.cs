namespace AIOrchestratorCoreLib.Composition.HostOptions;

/// <summary>
/// Where a host keeps its state. Both default to what the WPF app has always used; the overrides
/// exist so a daemon can be tried on a machine — or a plist tested — without touching the owner's
/// real supervision home, role commands or Claude settings.
/// </summary>
public interface IHostOptions
{
    /// <summary>The supervision root (default ~/.claude/supervision).</summary>
    string SupervisionRoot { get; }

    /// <summary>
    /// The Claude Code home the kit installs into — commands/, hooks/, settings.json
    /// (default ~/.claude).
    /// </summary>
    string ClaudeHome { get; }
}
