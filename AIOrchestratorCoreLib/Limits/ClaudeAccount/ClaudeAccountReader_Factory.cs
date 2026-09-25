namespace AIOrchestratorCoreLib.Limits.ClaudeAccount;

public static class ClaudeAccountReader_Factory
{
    /// <summary>
    /// The global config the CLI of THIS user writes: <c>$CLAUDE_CONFIG_DIR/.claude.json</c> when that
    /// variable is set, <c>~/.claude.json</c> otherwise.
    /// </summary>
    public static IClaudeAccountReader Create_ForThisUser()
    {
        var configDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");

        var path = string.IsNullOrWhiteSpace(configDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json")
            : Path.Combine(configDir, ".claude.json");

        return new ClaudeAccountReaderModel(path);
    }

    /// <summary>A named file — the test seam, and the same reader production uses.</summary>
    public static IClaudeAccountReader Create_FromFile(string globalConfigPath)
    {
        return new ClaudeAccountReaderModel(globalConfigPath);
    }

    /// <summary>Always "cannot tell": an account change is never observed, so no pause is ever lifted by one.</summary>
    public static IClaudeAccountReader Create_Unknown()
    {
        return new ClaudeAccountReaderModel(null);
    }
}
