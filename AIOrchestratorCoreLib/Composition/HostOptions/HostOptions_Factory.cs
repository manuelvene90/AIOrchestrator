namespace AIOrchestratorCoreLib.Composition.HostOptions;

public static class HostOptions_Factory
{
    public const string SUPERVISION_ROOT_ENV = "AIORCH_SUPERVISION_ROOT";
    public const string CLAUDE_HOME_ENV = "AIORCH_CLAUDE_HOME";
    public const string ROOT_ARGUMENT = "--root";
    public const string CLAUDE_HOME_ARGUMENT = "--claude-home";

    public static IHostOptions Create(string supervisionRoot, string claudeHome)
    {
        if (string.IsNullOrWhiteSpace(supervisionRoot))
            throw new ArgumentException("The supervision root must be a non-empty path", nameof(supervisionRoot));

        if (string.IsNullOrWhiteSpace(claudeHome))
            throw new ArgumentException("The Claude home must be a non-empty path", nameof(claudeHome));

        return new HostOptionsModel(supervisionRoot, claudeHome);
    }

    /// <summary>The WPF app's paths: ~/.claude/supervision and ~/.claude, no overrides consulted.</summary>
    public static IHostOptions Create_Default()
    {
        return Create_FromArguments([], _ => null, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    /// <summary>
    /// Daemon precedence, highest first: <c>--root DIR</c> / <c>--claude-home DIR</c> on the command
    /// line, then the AIORCH_SUPERVISION_ROOT / AIORCH_CLAUDE_HOME environment (the way a unit file
    /// or a plist configures a service), then the defaults under the user profile. The environment
    /// reader and the profile are parameters so the precedence is testable without setting real
    /// environment variables.
    /// </summary>
    public static IHostOptions Create_FromArguments(
        IReadOnlyList<string> arguments,
        Func<string, string?> readEnvironment,
        string userProfileFolder)
    {
        var claudeHome = Read_Argument_OrNull(arguments, CLAUDE_HOME_ARGUMENT)
            ?? Read_Environment_OrNull(readEnvironment, CLAUDE_HOME_ENV)
            ?? Path.Combine(userProfileFolder, ".claude");

        var supervisionRoot = Read_Argument_OrNull(arguments, ROOT_ARGUMENT)
            ?? Read_Environment_OrNull(readEnvironment, SUPERVISION_ROOT_ENV)
            ?? Path.Combine(userProfileFolder, ".claude", "supervision");

        return Create(supervisionRoot, claudeHome);
    }

    static string? Read_Argument_OrNull(IReadOnlyList<string> arguments, string name)
    {
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i] == name)
            {
                if (i + 1 >= arguments.Count || string.IsNullOrWhiteSpace(arguments[i + 1]))
                    throw new ArgumentException($"{name} needs a directory after it");

                return arguments[i + 1];
            }

            // --root=DIR is accepted as well: unit files and shells both write it that way.
            if (arguments[i].StartsWith(name + "=", StringComparison.Ordinal))
            {
                var value = arguments[i][(name.Length + 1)..];

                if (string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException($"{name} needs a directory after it");

                return value;
            }
        }

        return null;
    }

    static string? Read_Environment_OrNull(Func<string, string?> readEnvironment, string name)
    {
        var value = readEnvironment(name);

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
