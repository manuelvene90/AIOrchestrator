using System.Diagnostics;
using AIOrchestratorCoreLib.Processes;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Processes;

/// <summary>
/// The shell a configured command line runs through is decided at runtime from the OS. The OS is
/// a parameter of the builder so both branches are pinned on one machine — the suite cannot run on
/// three operating systems at once — and one real run on THIS machine proves the chosen shell
/// actually exists and executes.
/// </summary>
public class ShellCommandBuilderTests
{
    [Fact]
    public void OnWindows_ItIsCmdSlashC_WithTheCommandLineLeftForCmdToParse()
    {
        var startInfo = ShellCommand_Builder.Build_StartInfo("whisper \"C:\\voice\\a.ogg\" --model small", isWindows: true);

        Assert.Equal(ShellCommand_Builder.WINDOWS_SHELL, startInfo.FileName);
        Assert.Equal("/c whisper \"C:\\voice\\a.ogg\" --model small", startInfo.Arguments);
        Assert.Empty(startInfo.ArgumentList);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
    }

    [Fact]
    public void Elsewhere_ItIsShDashC_WithTheCommandLineAsOneArgument()
    {
        var startInfo = ShellCommand_Builder.Build_StartInfo("whisper \"/tmp/a.ogg\" --model small", isWindows: false);

        Assert.Equal(ShellCommand_Builder.POSIX_SHELL, startInfo.FileName);
        Assert.Equal(["-c", "whisper \"/tmp/a.ogg\" --model small"], startInfo.ArgumentList);
        Assert.Equal("", startInfo.Arguments);
    }

    [Fact]
    public void AnEmptyCommandLine_IsRefused_NotHandedToAShell()
    {
        Assert.Throws<ArgumentException>(() => ShellCommand_Builder.Build_StartInfo("   ", isWindows: false));
        Assert.Throws<ArgumentException>(() => ShellCommand_Builder.Build_StartInfo("", isWindows: true));
    }

    /// <summary>
    /// The runtime choice, exercised for real: whichever OS runs this suite, the shell it picks
    /// must exist and run a trivial command. `echo` is the one command spelled identically in
    /// cmd and sh.
    /// </summary>
    [Fact]
    public void TheRuntimeChoice_RunsARealCommand_OnThisMachine()
    {
        var startInfo = ShellCommand_Builder.Build_StartInfo("echo stage-two");
        startInfo.RedirectStandardOutput = true;

        using var process = Process.Start(startInfo) ?? throw new Exception("Process.Start returned null");
        var output = process.StandardOutput.ReadToEnd().Trim();
        Assert.True(process.WaitForExit(30_000), "the shell did not exit within 30 s");

        Assert.Equal(0, process.ExitCode);
        Assert.Equal("stage-two", output);
    }
}
