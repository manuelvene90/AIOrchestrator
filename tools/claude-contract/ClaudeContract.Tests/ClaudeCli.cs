using System.Runtime.InteropServices;

namespace ClaudeContract.Tests;

/// <summary>
/// The two ways this suite invokes a `claude`: the fake (<c>dotnet FakeClaude.dll …</c>, every
/// platform) and the real one (<c>claude</c> on PATH; on Windows the npm shim is a .cmd, which
/// only cmd.exe can start — the same resolution the bridge's translator uses).
/// </summary>
public static class ClaudeCli
{
    public static (string Executable, IReadOnlyList<string> LeadingArguments) Fake()
    {
        return ("dotnet", [Repo_Locator.Find_FakeClaudeDll()]);
    }

    public static (string Executable, IReadOnlyList<string> LeadingArguments) Real()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ("cmd.exe", ["/c", "claude"])
            : ("claude", []);
    }

    public static ProcessRun Run(
        (string Executable, IReadOnlyList<string> LeadingArguments) cli,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string? stdin,
        IReadOnlyDictionary<string, string>? environment,
        TimeSpan timeout)
    {
        return Process_Runner.Run(cli.Executable, [.. cli.LeadingArguments, .. arguments], workingDirectory, stdin, environment, timeout);
    }
}
