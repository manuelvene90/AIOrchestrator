using System.Diagnostics;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Tests.Kit;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.TestSupport;

/// <summary>
/// Whether the kit's channel tool can actually be RUN on this machine, and where its pieces are.
///
/// <para>
/// A typed entry needs four things: the script, its grammar, a bash to run them and a jq to read the
/// JSON. Any of them missing means the harness cannot exercise what it claims to — so the tests skip
/// BY NAME rather than passing, which is decision 20's rule applied to a probe instead of a guard: a
/// green that ran nothing is the failure, and a skip is visible in the suite's count.
/// </para>
/// </summary>
public static class ChannelAppendTool
{
    public static string? ScriptPath { get; } = KitRepoFiles.Find(Path.Combine("kit", "bin", "channel-append.sh"));

    public static string? GrammarPath { get; } =
        KitRepoFiles.Find(ChannelGrammar.KIT_RELATIVE_PATH.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Null when it can run; otherwise the reason, for the Skip message.</summary>
    public static string? Unrunnable_Reason { get; } = Find_Reason();

    static string? Find_Reason()
    {
        if (ScriptPath == null)
            return "kit/bin/channel-append.sh was not found from the test binary, so nothing would be exercised.";

        if (GrammarPath == null)
            return $"{ChannelGrammar.KIT_RELATIVE_PATH} was not found, so the tool could not read its grammar.";

        if (!Bash_Locator.Is_Available())
            return "no bash on this machine — the kit's scripts are bash and cannot be run here.";

        if (!Has_Jq())
            return "jq is not on PATH — a typed entry reads the grammar with jq, so nothing would be exercised.";

        return null;
    }

    static bool Has_Jq()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "jq",
                ArgumentList = { "--version" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });

            if (process == null)
                return false;

            process.WaitForExit(5_000);

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>Runs only where <see cref="ChannelAppendTool"/> can actually be invoked.</summary>
public sealed class RequiresChannelToolFactAttribute : FactAttribute
{
    public RequiresChannelToolFactAttribute()
    {
        if (ChannelAppendTool.Unrunnable_Reason != null)
            Skip = ChannelAppendTool.Unrunnable_Reason;
    }
}

/// <summary>The <see cref="TheoryAttribute"/> twin of <see cref="RequiresChannelToolFactAttribute"/>.</summary>
public sealed class RequiresChannelToolTheoryAttribute : TheoryAttribute
{
    public RequiresChannelToolTheoryAttribute()
    {
        if (ChannelAppendTool.Unrunnable_Reason != null)
            Skip = ChannelAppendTool.Unrunnable_Reason;
    }
}
