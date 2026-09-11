namespace AIOrchestratorCoreLib.Kit;

/// <summary>
/// What the two files under a Claude home say about one installed plugin. Immutable, and every field
/// is something a human can be shown: the refusal message quotes them rather than paraphrasing.
/// </summary>
public sealed class InstalledPluginReading(string? version, string? installPath, bool enabled, string? unreadableReason, string? commitSha = null)
{
    /// <summary>Null when the plugin has no install record.</summary>
    public string? Version { get; } = version;

    /// <summary>Where the installed copy lives — the fourth-copy question, answered.</summary>
    public string? InstallPath { get; } = installPath;

    /// <summary>settings.json enabledPlugins. A disabled plugin is installed and loads nothing.</summary>
    public bool Enabled { get; } = enabled;

    /// <summary>Non-null when a file existed but could not be read or parsed. Never guessed.</summary>
    public string? UnreadableReason { get; } = unreadableReason;

    /// <summary>
    /// The commit the installed copy was taken from, as the CLI recorded it (<c>gitCommitSha</c>).
    ///
    /// LAST IN THE CONSTRUCTOR AND OPTIONAL, which reads as an afterthought and is not one: every
    /// existing call site passes the first four positionally, and reordering them to put this beside
    /// Version would have rewritten a dozen tests to add a field none of them are about. Null means
    /// the record carried none — an install from a source with no git behind it, or an older CLI.
    /// </summary>
    public string? CommitSha { get; } = commitSha;

    public bool Is_Installed => Version != null;
}
