using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.GeneralSupervision;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.ClaudeInvocation;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.PrintTurnDispatcher;
using AIOrchestratorCoreLib.Running.TurnExecutor;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.Running.SessionRunner;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// A supervision root, a repo folder the fake runs in, a config.json with the print runner on for
/// the roles a test names, and a dispatcher wired to FakeClaude — everything the behavioural tests
/// share. The fake reads its scenario from the repo folder and logs every invocation there.
/// </summary>
public sealed class PrintRunnerTestHarness : IDisposable
{
    public string TempRoot { get; }
    public string RepoPath { get; }
    public ISupervisionPaths Paths { get; }
    public IOrchestrationSessionStore Store { get; }
    public IOrchestratorConfigProvider ConfigProvider { get; }
    public IOrchestrationLog Log { get; }
    public string ScenarioFile => Path.Combine(RepoPath, "fake-claude-scenario.json");
    public string InvocationLog => Path.Combine(RepoPath, "fake-claude-invocations.jsonl");

    readonly double _coalesceSeconds;
    readonly double _turnTimeoutMinutes;
    readonly int _maxConcurrent;
    readonly int _maxPerOrchestration;
    readonly string _resumeForGeneral;
    readonly string _resumeForMembers;
    readonly double _streamSilenceSeconds;
    readonly double _memberDigestMinutes;

    /// <summary>Null writes no key, so the suite runs on the production default — see memberDigestMinutes.</summary>
    readonly double? _memberSilenceMinutes;

    /// <summary>
    /// THE TURN TIMEOUT A TEST NAMES IS MEANT FOR EVERY ROLE, members included, unless it names the
    /// member ceiling separately. Since 2026-09-11 a braked member has its own two-hour ceiling, so a
    /// deadline test driving an implementer at five seconds would otherwise wait for the silence brake
    /// and pass or fail for the wrong reason.
    /// </summary>
    readonly double _memberTurnTimeoutMinutes;

    /// <param name="memberDigestMinutes">
    /// The supervisor's wake-up digest (<c>WakeUp_Policy</c>) — THE PRODUCTION WINDOW by default, so
    /// the suite observes what ships.
    ///
    /// <para>
    /// IT DEFAULTED TO OFF, AND THAT WAS A REVIEW FINDING OF 2026-09-09: switching every pre-existing
    /// test to digest-off removed the suite's only coverage of member → supervisor delivery at the
    /// window production runs on, so the change could not be observed by anything except the four
    /// cases written for it. A default that turns off the mechanism under test everywhere else is a
    /// default that certifies the old behaviour.
    /// </para>
    /// <para>
    /// AND IT COSTS NO WALL CLOCK, which was the fear behind the old default. Nothing sleeps through a
    /// window: a member's FIRST entry is never held (the greeting rule), so a case that appends one
    /// report and waits behaves as it always did, and the cases that drive a LATER report hand
    /// <c>Tick</c> a later instant through <see cref="Drive_Until_At"/>. A test that genuinely wants
    /// the pre-digest behaviour passes <c>0</c> and says why.
    /// </para>
    /// </param>
    public PrintRunnerTestHarness(string printRoles, double coalesceSeconds = 0, double turnTimeoutMinutes = 5, int maxConcurrent = 10, int maxPerOrchestration = 3, string resumeForGeneral = "fresh", double streamSilenceSeconds = 120, string resumeForMembers = "transcript", double memberDigestMinutes = 5, double? memberSilenceMinutes = null, double? memberTurnTimeoutMinutes = null)
    {
        TempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-print-runner-{Guid.NewGuid():N}");
        RepoPath = Path.Combine(TempRoot, "repo");
        Directory.CreateDirectory(RepoPath);
        Paths = SupervisionPaths_Factory.Create(TempRoot);
        Store = OrchestrationSessionStore_Factory.Create(Paths);
        Log = OrchestrationLog_Factory.Create(Paths);

        _coalesceSeconds = coalesceSeconds;
        _turnTimeoutMinutes = turnTimeoutMinutes;
        _maxConcurrent = maxConcurrent;
        _maxPerOrchestration = maxPerOrchestration;
        _resumeForGeneral = resumeForGeneral;
        _resumeForMembers = resumeForMembers;
        _streamSilenceSeconds = streamSilenceSeconds;
        _memberDigestMinutes = memberDigestMinutes;
        _memberSilenceMinutes = memberSilenceMinutes;
        _memberTurnTimeoutMinutes = memberTurnTimeoutMinutes ?? turnTimeoutMinutes;

        Directory.CreateDirectory(Paths.Root);
        Write_Config(printRoles);
        ConfigProvider = OrchestratorConfigProvider_Factory.Create(Paths);
    }

    /// <summary>
    /// Rewrites config.json with these roles on print — so a test can flip a role BACK to terminal
    /// the way the owner would. The write stamp is pushed forward because the provider reloads on
    /// it, and two writes inside one filesystem tick would otherwise serve the stale config.
    /// </summary>
    /// <summary>
    /// <paramref name="printRoles"/> is a comma-separated list, each entry either a role
    /// ("implementer") or a role and its runner ("supervisor:stream"). The bare form still means
    /// print, so every test written before the stream runner existed says exactly what it meant.
    /// </summary>
    public void Write_Config(string printRoles)
    {
        var runners = new JsonObject();

        foreach (var entry in printRoles.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split(':', 2);
            var role = parts[0].Trim();

            runners[role] = new JsonObject
            {
                ["runner"] = parts.Length > 1 ? parts[1].Trim() : "print",
                ["resume"] = role == "general" ? _resumeForGeneral : _resumeForMembers,
            };
        }

        var config = new JsonObject
        {
            ["repos"] = new JsonArray(new JsonObject { ["name"] = "Repo", ["path"] = RepoPath }),
            ["runners"] = runners,
            ["printRunner"] = new JsonObject
            {
                ["maxConcurrentTurns"] = _maxConcurrent,
                ["maxConcurrentTurnsPerOrchestration"] = _maxPerOrchestration,
                ["turnTimeoutMinutes"] = _turnTimeoutMinutes,
                ["coalesceSeconds"] = _coalesceSeconds,
                ["streamSilenceSeconds"] = _streamSilenceSeconds,
                ["memberDigestMinutes"] = _memberDigestMinutes,
                ["memberTurnTimeoutMinutes"] = _memberTurnTimeoutMinutes,
            },
        };

        if (_memberSilenceMinutes != null)
            config["printRunner"]!["memberSilenceMinutes"] = _memberSilenceMinutes.Value;

        File.WriteAllText(Paths.ConfigFile, config.ToJsonString());
        File.SetLastWriteTimeUtc(Paths.ConfigFile, DateTime.UtcNow.AddSeconds(1));
    }

    /// <summary>Sets one top-level key in config.json and moves its stamp forward, the way an owner editing the file does.</summary>
    public void Set_ConfigValue(string key, string value)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Paths.ConfigFile)) as JsonObject
            ?? throw new Exception($"config at {Paths.ConfigFile} is not a JSON object");

        root[key] = value;
        File.WriteAllText(Paths.ConfigFile, root.ToJsonString());
        File.SetLastWriteTimeUtc(Paths.ConfigFile, DateTime.UtcNow.AddSeconds(2));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(TempRoot, recursive: true);
        }
        catch
        {
            // A fake still exiting may hold the folder for a moment; the OS temp folder is not precious.
        }
    }

    public static IClaudeInvocation Fake_Invocation()
    {
        var testFolder = Path.GetDirectoryName(typeof(PrintRunnerTestHarness).Assembly.Location) ?? string.Empty;
        var dll = Path.Combine(testFolder, "FakeClaude.dll");

        if (!File.Exists(dll))
            throw new Exception($"FakeClaude.dll not found beside the test assembly at '{dll}' — the project reference to tools/claude-contract/FakeClaude is missing or the fake did not build");

        return ClaudeInvocation_Factory.Create("dotnet", [dll]);
    }

    public IPrintTurnDispatcher Create_Dispatcher(TimeSpan? retryBackoff = null)
    {
        return PrintTurnDispatcher_Factory.Create(Paths, Store, ConfigProvider, Create_Executors(), Log, retryBackoff ?? TimeSpan.FromMilliseconds(200));
    }

    /// <summary>The production ladder, against the fake: print, and stream falling back to it.</summary>
    public IReadOnlyList<ITurnExecutor> Create_Executors()
    {
        return TurnExecutor_Factory.Create_All(Paths, Fake_Invocation(), Log, ConfigProvider);
    }

    /// <summary>An orchestration with one member of the kind, registered bridge-driven — the launcher's path, minus the terminal.</summary>
    public (string OrchId, string MemberId) Register_Member(MemberKinds kind, string orchId = "repo-1", SessionRunners runner = SessionRunners.Print)
    {
        if (Store.Get_Session_OrNull(orchId) == null)
            Store.Create_Orchestration(orchId, "Repo", RepoPath);

        var session = Store.Add_Member(orchId, kind);
        var memberId = session.Members[^1].MemberId;
        var role = SessionRole_Names.From_MemberKind(kind);

        Create_Runner(runner).Start(
            SessionLaunch_Factory.Create(role, orchId, memberId, RepoPath, "haiku", Paths.Get_ImplementerPidFile(orchId, memberId), null));

        return (orchId, memberId);
    }

    /// <summary>The orchestration's SUPERVISOR, registered bridge-driven — woken by the owner channel.</summary>
    public string Register_Supervisor(string orchId = "repo-1", SessionRunners runner = SessionRunners.Stream)
    {
        if (Store.Get_Session_OrNull(orchId) == null)
            Store.Create_Orchestration(orchId, "Repo", RepoPath);

        // The stamp is what makes the orchestration a CREW rather than a basic one, and the launcher
        // writes it for every supervisor whether it spawns a window or registers a state file.
        Store.Set_SupervisorPid(orchId, null);

        Create_Runner(runner).Start(
            SessionLaunch_Factory.Create(SessionRoles.Supervisor, orchId, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID, RepoPath, "haiku", Paths.Get_SupervisorPidFile(orchId), null));

        return SessionLaunch_Factory.SUPERVISOR_MEMBER_ID;
    }

    public void Register_General(SessionRunners runner = SessionRunners.Print)
    {
        GeneralChannel_Initializer.Ensure_Exists(Paths);

        Create_Runner(runner).Start(
            SessionLaunch_Factory.Create(SessionRoles.General, ChannelDiscovery.GENERAL_ORCH_ID, SessionLaunch_Factory.GENERAL_MEMBER_ID, RepoPath, "sonnet", Paths.GeneralPidFile, null));
    }

    ISessionRunner Create_Runner(SessionRunners runner)
    {
        return runner == SessionRunners.Stream ? SessionRunner_Factory.Create_Stream(Paths, Store, Log) : SessionRunner_Factory.Create_Print(Paths, Store, Log);
    }

    /// <summary>
    /// Pushes the supervisor's spawn stamp into the past, so the watchdog's 90 s grace is over. The
    /// store has no setter for it (nothing in the app needs one), and a test that waited it out for
    /// real would take a minute and a half.
    /// </summary>
    public void Age_SupervisorSpawn(string orchId, TimeSpan by)
    {
        var file = Paths.Get_SessionFile(orchId);
        var root = (JsonObject)JsonNode.Parse(File.ReadAllText(file))!;

        root["supervisorSpawnedUtc"] = DateTime.UtcNow.Subtract(by).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        File.WriteAllText(file, root.ToJsonString());
    }

    public void Write_Scenario(string json)
    {
        File.WriteAllText(ScenarioFile, json);
    }

    /// <summary>
    /// RETRIED, because a test may read this WHILE a fake is writing it. The fake takes the log
    /// under an exclusive handle (it has to: ten turns share one file and the counters are computed
    /// from what is really there), so a plain read throws "used by another process" — which is
    /// exactly what happened once in seven runs of a test that polls this file inside its wait
    /// predicate, and reads as a failure of the code under test rather than of the reader.
    /// </summary>
    public IReadOnlyList<JsonObject> Read_Invocations()
    {
        for (var attempt = 0; ; attempt++)
        {
            if (!File.Exists(InvocationLog))
                return [];

            try
            {
                return File.ReadAllLines(InvocationLog).Where(line => line.Length > 0).Select(line => (JsonObject)JsonNode.Parse(line)!).ToList();
            }
            catch (IOException) when (attempt < 100)
            {
                Thread.Sleep(20);
            }
        }
    }

    public static List<string> Args(JsonObject invocation)
    {
        return invocation["args"]!.AsArray().Select(node => node!.GetValue<string>()).ToList();
    }

    public IPrintSessionState Read_State(SessionRoles role, string orchId, string memberId)
    {
        return PrintSessionState_Store.Read_OrNull(PrintSessionState_Store.Get_StateFile(Paths, role, orchId, memberId))
            ?? throw new Exception("state file missing");
    }

    public string? Read_Pack_OrNull(SessionRoles role, string orchId, string memberId)
    {
        var file = AIOrchestratorCoreLib.Running.StatePack.StatePack_Locator.Get_File(Paths, role, orchId, memberId);
        return File.Exists(file) ? File.ReadAllText(file) : null;
    }

    public string Read_Channel(string orchId, string memberId)
    {
        return File.ReadAllText(Paths.Get_ImplementerChannelFile(orchId, memberId));
    }

    /// <summary>Ticks the dispatcher with the real clock until the condition holds or the timeout passes; returns whether it held.</summary>
    public static bool Drive_Until(IPrintTurnDispatcher dispatcher, Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            dispatcher.Tick(DateTime.Now);

            if (condition())
                return true;

            Thread.Sleep(100);
        }

        dispatcher.Tick(DateTime.Now);
        return condition();
    }

    /// <summary>
    /// THE SAME DRIVER ON AN INJECTED CLOCK. <c>Tick</c> takes the moment it is deciding at, so
    /// anything the dispatcher schedules — the coalesce window, the wake-up digest — can be tested by
    /// handing it a later <paramref name="nowLocal"/> instead of by sleeping through it. The polling
    /// is still real time, because what is being waited for is a turn appearing in flight on a
    /// background task; the DECISION under test is made entirely from the stamp passed in.
    /// </summary>
    public static bool Drive_Until_At(IPrintTurnDispatcher dispatcher, DateTime nowLocal, Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            dispatcher.Tick(nowLocal);

            if (condition())
                return true;

            Thread.Sleep(50);
        }

        dispatcher.Tick(nowLocal);
        return condition();
    }

    /// <summary>
    /// SPENDS EVERY NAMED SPOKE'S FIRST-CONTACT EXEMPTION, so what a digest case measures afterwards is
    /// the digest and not the greeting rule.
    ///
    /// <para>
    /// A member's FIRST entry is never held (<c>WakeUp_Policy.Is_Digestable</c>): it is the boot
    /// greeting of a session that exists to be briefed, and holding it cost a whole window of dead time
    /// per <c>add-implementer</c>. Every entry after that is an ordinary report and is held — which is
    /// the state a member is in for all but the first minute of its life, and therefore the state a
    /// case about "a member's report" has to set up. This is that setup: each member says hello, the
    /// owner writes at the same instant, and the owner's message releases the turn whatever the digest
    /// thinks, so every spoke ends with one delivery on its cursor.
    /// </para>
    /// <para>
    /// ONE COPY, HERE, because two test classes need it and the wait helpers beside it are already the
    /// thing this suite was told not to write a fifth of.
    /// </para>
    /// </summary>
    public void Spend_FirstContact(IPrintTurnDispatcher dispatcher, string orchId, DateTime nowLocal, int turnsSoFar, params string[] memberIds)
    {
        // IT THROWS RATHER THAN ASSERTS, like everything else in this file: a harness that fails its
        // own setup is not a case's verdict, and a setup step reported as an assertion failure sends
        // the next reader to the wrong file.
        foreach (var memberId in memberIds)
        {
            if (!ChannelAppender.Append_SessionEntry(
                Paths.Get_ImplementerChannelFile(orchId, memberId),
                MemberKind_Ids.Resolve_Kind(memberId) == MemberKinds.Reviewer ? ChannelAuthors.Reviewer : ChannelAuthors.Implementer,
                $"{memberId} online",
                "reporting for duty",
                DateTime.Now))
                throw new Exception($"could not append '{memberId}' greeting to its spoke");
        }

        if (!ChannelAppender.Append_OwnerEntry(Paths.Get_OwnerChannelFile(orchId), "get started", DateTime.Now))
            throw new Exception($"could not append the owner entry that releases '{orchId}' first-contact traffic");

        if (!Drive_Until_At(dispatcher, nowLocal, () => Read_State(SessionRoles.Supervisor, orchId, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID).ExecutedTurns.Count == turnsSoFar + 1, GENEROUS))
            throw new Exception("the owner's message did not start a turn, so no spoke's first entry was ever handed over and nothing after this is measuring the digest");
    }

    public static readonly TimeSpan GENEROUS = TimeSpan.FromSeconds(60);
}
