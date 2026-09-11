using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running.ClaudeInvocation;
using AIOrchestratorCoreLib.Running.RunnerConfigs;
using AIOrchestratorCoreLib.Running.SessionSandbox;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// A SESSION THAT EATS THE MACHINE MUST DIE ALONE.
///
/// VPS, 2026-09-07: a supervisor asked an implementer to re-measure a suite "under memory
/// pressure", the implementer ran a 6.5 GB allocator, and the <c>aiorchestrator</c> unit was
/// OOM-killed three times in ten minutes. Sessions are spawned as CHILDREN of the daemon, so they
/// share its cgroup and the kernel scores the cgroup: one session's mistake took the bridge, every
/// other session, and every turn in flight.
///
/// The command line below is the whole fix, and it is asserted character for character because it
/// is the only thing that will be running on that machine — a rule written into a role protocol
/// instead would restrain an honest session and stop nobody else (CLAUDE.md decision 21).
/// </summary>
public class SessionSandboxTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationLog _log;

    static readonly IClaudeInvocation PLAIN_POSIX = ClaudeInvocation_Factory.Create("claude", []);
    static readonly IClaudeInvocation PLAIN_WINDOWS = ClaudeInvocation_Factory.Create("cmd.exe", ["/c", "claude"]);

    public SessionSandboxTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-sandbox-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        _log = OrchestrationLog_Factory.Create(_paths);
        Write_Config(null);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that will not delete is not a test result.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// THE LINE THE VPS WILL RUN. If this changes, a session's blast radius changes with it, so it
    /// is written out in full rather than assembled from the same constants the production code uses.
    /// </summary>
    [Fact]
    public void OnLinux_TheCommandIsWrappedInAMemoryLimitedUserScope()
    {
        var wrapped = MemoryLimitedInvocation_Builder.Wrap(PLAIN_POSIX, "3G");

        Assert.Equal(
            "systemd-run --user --scope --quiet -p MemoryMax=3G -p MemorySwapMax=1536M -- claude",
            MemoryLimitedInvocation_Builder.Describe(wrapped));
    }

    /// <summary>
    /// A ceiling without a swap ceiling is a speed bump: the cgroup pages instead of dying and the
    /// runaway allocator keeps allocating, which is the same outcome slower. Half, per the brief.
    /// </summary>
    [Theory]
    [InlineData("3G", "1536M")]
    [InlineData("2G", "1G")]
    [InlineData("512M", "256M")]
    [InlineData("1T", "512G")]
    public void TheSwapCeilingIsHalfTheMemoryOne(string memoryMax, string expectedSwapMax)
    {
        Assert.Contains(
            $"-p MemorySwapMax={expectedSwapMax}",
            MemoryLimitedInvocation_Builder.Describe(MemoryLimitedInvocation_Builder.Wrap(PLAIN_POSIX, memoryMax)));
    }

    /// <summary>
    /// The whole plain invocation goes after the <c>--</c>, leading arguments included — otherwise
    /// a Windows-shaped invocation would lose its <c>/c claude</c> and systemd-run would be handed
    /// a bare shell.
    /// </summary>
    [Fact]
    public void TheWholePlainInvocationSurvivesTheWrapper_LeadingArgumentsIncluded()
    {
        var wrapped = MemoryLimitedInvocation_Builder.Wrap(PLAIN_WINDOWS, "3G");

        Assert.EndsWith("-- cmd.exe /c claude", MemoryLimitedInvocation_Builder.Describe(wrapped));
    }

    /// <summary>
    /// The CLI's own flags are <c>-p</c>-shaped too. Without the terminator systemd-run reads them
    /// as more of its own properties — the same lesson the spawn command builder wrote down when a
    /// variadic flag ate a prompt.
    /// </summary>
    [Fact]
    public void TheArgumentListIsTerminated_SoTheCliOwnFlagsAreNotReadAsScopeProperties()
    {
        var leading = MemoryLimitedInvocation_Builder.Build_LeadingArguments(PLAIN_POSIX, "3G");
        var terminator = leading.ToList().IndexOf("--");

        Assert.True(terminator > 0, $"no '--' in {string.Join(' ', leading)}");
        Assert.DoesNotContain("--", leading.Skip(terminator + 1));
    }

    /// <summary>
    /// macOS and Windows have no cgroups, so the real probe answers no and the command line is the
    /// one it has always been. Asserted through the SANDBOX rather than through the OS check, because
    /// the sandbox is what the runners call.
    /// </summary>
    [Fact]
    public void WhereSystemdRunIsNotUsable_TheCommandIsExactlyWhatItWasBefore()
    {
        var sandbox = SessionSandbox_Factory.Create_WithProbe(Build_Provider(), _log, () => false);

        Assert.Same(PLAIN_POSIX, sandbox.Wrap(PLAIN_POSIX));
    }

    /// <summary>...and on a machine that CAN, with the same config, it is wrapped.</summary>
    [Fact]
    public void WhereSystemdRunIsUsable_TheConfiguredCeilingIsOnTheCommandLine()
    {
        Write_Config("512M");

        var sandbox = SessionSandbox_Factory.Create_WithProbe(Build_Provider(), _log, () => true);
        var wrapped = sandbox.Wrap(PLAIN_POSIX);

        Assert.Equal(MemoryLimitedInvocation_Builder.SYSTEMD_RUN, wrapped.Executable);
        Assert.Contains("-p MemoryMax=512M", MemoryLimitedInvocation_Builder.Describe(wrapped));
    }

    /// <summary>
    /// Turning the cap off is a decision an operator is allowed to take, and it must not read as a
    /// typo — the word forms pass through and the command line is the bare one.
    /// </summary>
    [Theory]
    [InlineData("none")]
    [InlineData("off")]
    [InlineData("0")]
    [InlineData("")]
    public void TheCeilingCanBeTurnedOff_AndThenNothingWraps(string word)
    {
        Write_Config(word);

        var sandbox = SessionSandbox_Factory.Create_WithProbe(Build_Provider(), _log, () => true);

        Assert.Same(PLAIN_POSIX, sandbox.Wrap(PLAIN_POSIX));
    }

    /// <summary>
    /// A size this host cannot read must not become a silent ceiling of the default's size: "300M"
    /// meaning three hundred megabytes and "3 GB" meaning three gigabytes would both quietly become
    /// 3G. The loader refuses it in the owner's own terms and the sandbox does not wrap on it.
    /// </summary>
    [Fact]
    public void AnUnreadableCeiling_IsRefusedInTheOwnersTerms_AndFallsBackToTheDefault()
    {
        var configs = RunnerConfigs_Json.Parse(JsonNode.Parse("""{"runners":{"sessionMemoryMax":"3 GB"}}""") as JsonObject);

        Assert.Equal(RunnerConfigs_Factory.DEFAULT_SESSION_MEMORY_MAX, configs.SessionMemoryMax);
        Assert.Contains(configs.Rejections, line => line.Contains("sessionMemoryMax") && line.Contains("3 GB"));
    }

    /// <summary>
    /// Silence is the failure this repo keeps paying for: an operator who believes their sessions
    /// are capped while nothing caps them has the 2026-09-07 outage waiting for them again.
    /// </summary>
    [Fact]
    public void WhenItCannotWrap_ItSaysSo_NamingThePredicateThatFailed()
    {
        var sandbox = SessionSandbox_Factory.Create_WithProbe(Build_Provider(), _log, () => false);

        sandbox.Wrap(PLAIN_POSIX);
        sandbox.Wrap(PLAIN_POSIX);
        sandbox.Wrap(PLAIN_POSIX);

        var lines = File.ReadAllText(_paths.GlobalLogFile);

        Assert.Contains(MemoryLimitedInvocation_Builder.SYSTEMD_RUN, lines);
        Assert.Contains("UNCAPPED", lines);

        // Once, not per turn: a repeat that is a notification is a waterfall (decision 14).
        Assert.Equal(1, Count_Occurrences(lines, "UNCAPPED"));
    }

    static int Count_Occurrences(string text, string fragment)
    {
        var count = 0;
        var at = text.IndexOf(fragment, StringComparison.Ordinal);

        while (at >= 0)
        {
            count++;
            at = text.IndexOf(fragment, at + fragment.Length, StringComparison.Ordinal);
        }

        return count;
    }


    /// <summary>
    /// The key an operator has to type, and the fact that the app writes it back — so the whole
    /// surface is visible in config.json without reading the source, which is what every other
    /// runner setting already promises.
    /// </summary>
    [Fact]
    public void TheCeiling_RoundTripsThroughConfigJson_UnderRunnersSessionMemoryMax()
    {
        var parsed = RunnerConfigs_Json.Parse(JsonNode.Parse("""{"runners":{"sessionMemoryMax":"1G"}}""") as JsonObject);

        Assert.Equal("1G", parsed.SessionMemoryMax);

        var written = new JsonObject();
        RunnerConfigs_Json.Write(written, parsed);

        Assert.Equal("1G", written[RunnerConfigs_Json.RUNNERS_KEY]?[RunnerConfigs_Json.SESSION_MEMORY_MAX_KEY]?.GetValue<string>());
        Assert.Equal("1G", RunnerConfigs_Json.Parse(written).SessionMemoryMax);
    }

    /// <summary>An absent key is 3G — nothing an operator has to do to be protected.</summary>
    [Fact]
    public void AnAbsentKey_MeansTheDefaultCeiling()
    {
        Assert.Equal("3G", RunnerConfigs_Factory.DEFAULT_SESSION_MEMORY_MAX);
        Assert.Equal("3G", RunnerConfigs_Json.Parse(JsonNode.Parse("""{"repos":[]}""") as JsonObject).SessionMemoryMax);
    }

    /// <summary>
    /// NORMALISED, not echoed: what is stored is handed to systemd verbatim at every spawn, so a
    /// spelling this host accepts and systemd refuses would turn every spawn into a failed start.
    /// </summary>
    [Fact]
    public void TheCeilingIsStoredInSystemdsOwnSpelling()
    {
        Assert.Equal("3G", RunnerConfigs_Json.Parse(JsonNode.Parse("""{"runners":{"sessionMemoryMax":"3072M"}}""") as JsonObject).SessionMemoryMax);
    }

    IOrchestratorConfigProvider Build_Provider()
    {
        return OrchestratorConfigProvider_Factory.Create(_paths);
    }

    void Write_Config(string? sessionMemoryMax)
    {
        var runners = new JsonObject();

        if (sessionMemoryMax != null)
            runners[RunnerConfigs_Json.SESSION_MEMORY_MAX_KEY] = sessionMemoryMax;

        File.WriteAllText(_paths.ConfigFile, new JsonObject { ["repos"] = new JsonArray(), ["runners"] = runners }.ToJsonString());
        File.SetLastWriteTimeUtc(_paths.ConfigFile, DateTime.UtcNow.AddSeconds(1));
    }
}