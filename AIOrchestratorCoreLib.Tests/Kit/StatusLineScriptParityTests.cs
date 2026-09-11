using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// STATUSLINE.SH RENDERS THE SAME BYTES AS STATUSLINE.PS1. The .ps1 is the reference (Windows,
/// unchanged); the .sh is its twin for macOS and Linux, and the two must agree line for line or the
/// terminal names a session differently on two machines of the same owner.
///
/// The fixtures under kit/statusline/fixtures are the contract: the status line JSON in the same
/// shapes the Limits/ parser and SessionContextUsage_Factory are tested on (rate_limits.*.used_percentage,
/// context_window.used_percentage), the AIORCH_* environment the spawner sets, the session.json /
/// .progress.json the app writes, and the EXACT expected line. Every fixture runs the bash script;
/// on Windows with PowerShell present it ALSO runs the .ps1 against the same expectation, which is
/// what makes the expectations a parity claim rather than a description of one script.
///
/// Nothing here skips. No bash, no fixtures, no script: the test FAILS (decision 20 — a harness
/// that cannot find what it tests must refuse to run).
/// </summary>
public class StatusLineScriptParityTests
{
    static readonly string FIXTURES_FOLDER = Path.Combine(Find_KitStatuslineFolder_OrFail(), "fixtures");

    public static TheoryData<string> Fixture_Names()
    {
        var data = new TheoryData<string>();

        foreach (var file in Directory.EnumerateFiles(FIXTURES_FOLDER, "*.json").Order())
            data.Add(Path.GetFileNameWithoutExtension(file));

        return data;
    }

    [Fact]
    public void TheFixtureSet_CoversEveryRenderedRole_SoAnEmptiedFolderCannotPassAsParity()
    {
        var roles = Directory.EnumerateFiles(FIXTURES_FOLDER, "*.json")
            .Select(file => Load_Fixture(Path.GetFileNameWithoutExtension(file))["env"]?["AIORCH_ROLE"]?.GetValue<string>() ?? "")
            .ToHashSet();

        Assert.NotEmpty(roles);

        foreach (var role in new[] { "supervisor", "solo", "implementer", "reviewer", "communicator", "general", "" })
            Assert.Contains(role, roles);
    }

    [Fact]
    public void TheBashTwin_NeedsNoPython3_OnlyBashAndJq()
    {
        var script = File.ReadAllText(Path.Combine(Find_KitStatuslineFolder_OrFail(), "statusline.sh"));
        var codeLines = script.Split('\n').Where(line => !line.TrimStart().StartsWith('#'));

        Assert.DoesNotContain(codeLines, line => line.Contains("python"));
        Assert.Contains(codeLines, line => line.Contains("jq "));
    }

    [Theory]
    [MemberData(nameof(Fixture_Names))]
    public void TheBashScript_RendersTheExpectedLine_AndWritesTheProbe(string fixtureName)
    {
        var fixture = Load_Fixture(fixtureName);
        using var home = new FixtureHome(fixture);

        var output = Run_Script(Bash_Locator.Find_OrFail(), $"\"{Find_KitStatuslineFolder_OrFail().Replace('\\', '/')}/statusline.sh\"", home, fixture, "HOME");

        Assert.Equal(Read_Expected(fixture), output);
        Assert_ProbeFile(fixture, home);
    }

    /// <summary>
    /// The reference script against the same fixtures — only where it can run. Windows PowerShell
    /// is what the .ps1 targets (USERPROFILE, backslash joins), so on any other OS this asserts
    /// nothing and says so by not being there: the bash test above is the one that always runs.
    /// UNVERIFIED outside Windows by construction; run the suite on Windows to close it.
    /// </summary>
    [Theory]
    [MemberData(nameof(Fixture_Names))]
    public void ThePowerShellReference_RendersTheSameLine_WhereItCanRun(string fixtureName)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var fixture = Load_Fixture(fixtureName);
        using var home = new FixtureHome(fixture);
        var script = Path.Combine(Find_KitStatuslineFolder_OrFail(), "statusline.ps1");

        var output = Run_Script("powershell", $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"", home, fixture, "USERPROFILE");

        Assert.Equal(Read_Expected(fixture), output);
        Assert_ProbeFile(fixture, home);
    }

    static string Run_Script(string fileName, string arguments, FixtureHome home, JsonObject fixture, string homeVariable)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardInputEncoding = new UTF8Encoding(false),
        };

        startInfo.Environment[homeVariable] = home.Root;

        foreach (var variable in fixture["env"]?.AsObject() ?? [])
            startInfo.Environment[variable.Key] = variable.Value?.GetValue<string>() ?? "";

        using var process = Process.Start(startInfo) ?? throw new Exception($"could not start {fileName}");

        process.StandardInput.Write(Read_StdinText(fixture));
        process.StandardInput.Close();

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();

        if (!process.WaitForExit(60_000))
            throw new Exception($"{fileName} did not exit within 60 s");

        Assert.True(process.ExitCode == 0, $"{fileName} exited {process.ExitCode}: {error}");

        return output.TrimEnd('\r', '\n');
    }

    static void Assert_ProbeFile(JsonObject fixture, FixtureHome home)
    {
        var expectedProbe = fixture["expectedUsageFile"]?.GetValue<string>();
        var forbiddenProbe = fixture["forbiddenUsageFile"]?.GetValue<string>();

        if (expectedProbe != null)
        {
            var probeFile = home.Resolve(expectedProbe);
            Assert.True(File.Exists(probeFile), $"the telemetry probe was not written at {probeFile}");
            Assert.Equal(Read_StdinText(fixture).Trim(), File.ReadAllText(probeFile).TrimStart('\uFEFF').Trim());
        }

        if (forbiddenProbe != null)
            Assert.False(File.Exists(home.Resolve(forbiddenProbe)), "a probe was written with nothing on stdin to dump");
    }

    static string Read_Expected(JsonObject fixture)
    {
        return fixture["expected"]?.GetValue<string>() ?? throw new Exception("fixture has no 'expected'");
    }

    static string Read_StdinText(JsonObject fixture)
    {
        if (fixture["stdinText"] is JsonValue text)
            return text.GetValue<string>();

        return fixture["stdin"]?.ToJsonString() ?? "";
    }

    static JsonObject Load_Fixture(string name)
    {
        var file = Path.Combine(FIXTURES_FOLDER, name + ".json");

        return JsonNode.Parse(File.ReadAllText(file)) as JsonObject
            ?? throw new Exception($"{file} is not a JSON object");
    }

    /// <summary>A throw-away profile folder laid out the way the scripts expect: .claude/supervision/&lt;files&gt;.</summary>
    sealed class FixtureHome : IDisposable
    {
        public string Root { get; }
        readonly string _supervision;

        public FixtureHome(JsonObject fixture)
        {
            Root = Path.Combine(Path.GetTempPath(), $"aiorch-statusline-{Guid.NewGuid():N}");
            _supervision = Path.Combine(Root, ".claude", "supervision");
            Directory.CreateDirectory(_supervision);

            foreach (var entry in fixture["files"]?.AsObject() ?? [])
                Write(entry.Key, entry.Key.EndsWith(".keep") ? "" : entry.Value?.ToJsonString() ?? "");

            foreach (var entry in fixture["rawFiles"]?.AsObject() ?? [])
                Write(entry.Key, entry.Value?.GetValue<string>() ?? "");

            var progressAge = fixture["progressAgeSeconds"]?.GetValue<int>() ?? 0;

            if (progressAge > 0)
            {
                foreach (var progressFile in Directory.EnumerateFiles(_supervision, ".progress.json", SearchOption.AllDirectories))
                    File.SetLastWriteTimeUtc(progressFile, DateTime.UtcNow.AddSeconds(-progressAge));
            }
        }

        public string Resolve(string relativePath)
        {
            return Path.Combine(_supervision, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        void Write(string relativePath, string content)
        {
            var file = Resolve(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, content, new UTF8Encoding(false));
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    static string Find_KitStatuslineFolder_OrFail()
    {
        var folder = AppContext.BaseDirectory;

        for (var depth = 0; depth < 8; depth++)
        {
            var candidate = Path.Combine(folder, "kit", "statusline");

            if (File.Exists(Path.Combine(candidate, "statusline.ps1")) && File.Exists(Path.Combine(candidate, "statusline.sh")))
                return candidate;

            var parent = Directory.GetParent(folder);

            if (parent == null)
                break;

            folder = parent.FullName;
        }

        throw new Exception($"kit/statusline with both scripts was not found walking up from {AppContext.BaseDirectory} — nothing under test, so no pass");
    }
}
