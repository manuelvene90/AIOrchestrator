using System.Diagnostics;
using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.Spawning.SpawnCommand;
using AIOrchestratorCoreLib.Spawning.SessionSpawner;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Running;
using AIOrchestratorCoreLib.Time.Clock;
using Xunit;
using Xunit.Abstractions;
using AIOrchestratorCoreLib.Tests.TestSupport;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE ROUND V.7 OF THE VPS INSTALL COULD NOT HAVE WITHOUT A HUMAN, run here end to end with nobody
/// touching an orchestration channel: the owner says one thing to the concierge, and everything after
/// that is the app.
///
/// <para>
/// On 2026-09-06 the same round needed a hand twice — the task never left the request (there was no
/// field for it) and the stream-run supervisor never booted (no entry ever arrived, because the topic
/// the owner would have typed into is made by the greeting the supervisor had not written). Both are
/// fixed; this is the proof that they are fixed TOGETHER, which is the only way either of them is
/// worth anything. Two engines' worth of unit tests can both be green and the round still stop at the
/// seam between them.
/// </para>
/// <para>
/// WHAT IS REAL HERE: the <c>claude</c> CLI on <c>--model haiku</c>, the request-file protocol, the
/// launcher, the boot turn, the dispatcher, the channel files and a git repo that is really committed
/// to. WHAT IS NOT: the Telegram transport is a fake, so "the topic was created" is asserted as the
/// call the app made and the id it stored, not as an HTTP round trip — that half was proved on the
/// VPS, against the real API, and does not need a bot token here to be believed.
/// </para>
/// <para>
/// Skipped unless <c>AIORCH_LIVE=1</c>: it costs tokens and minutes. The skip says so, because a
/// silent one lets a CLI upgrade pass the suite without ever being measured.
/// </para>
/// </summary>
public class FirstRoundFromTheGeneralLiveSmokeTests(ITestOutputHelper output)
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const string REPO_NAME = "Repo";

    /// <summary>How MirrorText_Formatter labels a supervisor entry on the owner's phone.</summary>
    const string MIRROR_SUPERVISOR_PREFIX = "Sup:";

    /// <summary>Quote-free on purpose: the concierge has to put this inside a JSON string with printf.</summary>
    const string OWNER_ASK = "Work on Repo: create a file called HELLO.md containing a one-line greeting, and commit it. Use a full crew.";

    readonly ITestOutputHelper _output = output;

    sealed class LiveFactAttribute : FactAttribute
    {
        public LiveFactAttribute()
        {
            if (Environment.GetEnvironmentVariable(StreamLiveSmokeTests.ENABLE_ENV) != "1")
                Skip = $"Live smoke — set {StreamLiveSmokeTests.ENABLE_ENV}=1 to run a whole first round from one owner message, on --model haiku";
        }
    }

    [LiveFact]
    public async Task OneOwnerMessageToTheConcierge_BuysACrewThatBootsItself_AndDoesTheWork()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-stage-1d-live-{Guid.NewGuid():N}");
        var repo = Path.Combine(tempRoot, "repo");

        Directory.CreateDirectory(repo);

        var paths = SupervisionPaths_Factory.Create(tempRoot);
        Directory.CreateDirectory(paths.RequestsFolder);
        Directory.CreateDirectory(paths.GeneralFolder);

        Write_Config(paths, repo);
        Write_ProbeRepo(repo);
        Write_ProbeGeneralCommand(paths);
        Git(repo, "init -q");
        Git(repo, "config user.email probe@example.com");
        Git(repo, "config user.name Probe");

        var store = OrchestrationSessionStore_Factory.Create(paths);
        var configProvider = OrchestratorConfigProvider_Factory.Create(paths);
        var log = new RecordingLog_Fake();
        var launcher = OrchestrationLauncher_Factory.Create(paths, configProvider, store, new NoTerminal_Fake(), log);
        var telegram = new CapturingTelegram_Fake();

        var engine = BridgeEngine_Factory.Create_WithDecisionState(
            paths, configProvider, store, launcher, log, telegram,
            EngineStateStore_Factory.Create_File(paths, log),
            Clock_Factory.Create_System(),
            BridgeTestTiming.Fast());

        // The app does this at startup; the test does it explicitly so a watchdog timing difference
        // cannot be mistaken for the thing under test.
        launcher.Spawn_GeneralSupervisor();

        // AFTER the spawn, because Ensure_Exists seeds this file and will not overwrite it — and the
        // seed it writes is louder than any role command. MEASURED, first attempt at this round: the
        // seed tells a first-run concierge to "say so in your greeting and ask the owner where to
        // learn the repo landscape", and haiku did exactly that, asked "what would you like to do?",
        // and filed no request. That is the file working as designed on a machine with no memory yet;
        // this replaces it with the memory a machine that has been used for five minutes would hold.
        Write_ProbeGeneralMemory(paths);

        using var cancellation = new CancellationTokenSource();
        var loop = engine.Run_Async(cancellation.Token);
        var wholeRound = Stopwatch.StartNew();

        // Filled as the round learns them, so the evidence written in the finally is complete for a
        // FAILED round too — which is the run whose evidence is actually worth having.
        string? seenOrchId = null;
        string? seenImplementerId = null;

        try
        {
            // ── THE ONLY THING A HUMAN DOES IN THIS ENTIRE TEST ──────────────────────────────
            Assert.True(ChannelAppender.Append_OwnerEntry(paths.GeneralChannelFile, OWNER_ASK, DateTime.Now));

            var toTheRequest = await Wait_Async(() => store.Load_All().Count > 0, TimeSpan.FromMinutes(4), "the concierge never started an orchestration", log, telegram);
            var orchId = store.Load_All()[0].OrchId;
            seenOrchId = orchId;

            Say($"owner message -> orchestration '{orchId}' created: {toTheRequest.TotalSeconds:F1} s");

            // THE TASK TRAVELLED. Nothing in this test wrote it; the app did, from the request.
            var toTheTask = await Wait_Async(
                () => Owner_Entries(paths, orchId).Any(entry => entry.Author == ChannelAuthors.Owner),
                TimeSpan.FromSeconds(30), "the task never reached the new orchestration's owner channel", log, telegram);

            Say($"  …and its task was in its channel {toTheTask.TotalSeconds:F1} s later");

            // THE BOOT TURN. Nobody has said anything to this supervisor; it greets because the bridge
            // ran it once, and the greeting is what the push policy recognises as a session coming up.
            var toTheGreeting = await Wait_Async(
                () => Owner_Entries(paths, orchId).Any(entry => entry.Author == ChannelAuthors.Supervisor),
                TimeSpan.FromMinutes(4), "the supervisor never greeted — THE DEFECT: it was waiting for an entry that could not arrive", log, telegram);

            var greeting = Owner_Entries(paths, orchId).First(entry => entry.Author == ChannelAuthors.Supervisor);

            Say($"orchestration created -> supervisor's first entry: {toTheGreeting.TotalSeconds:F1} s — \"{greeting.Subject}\"");

            // THE SUBJECT IS NOT ASSERTED, and the first version of this test asserting it was wrong in
            // a way worth keeping written down. It demanded an "online" greeting, and haiku answered
            // "Task delegated" — which is CORRECT: the task rode the same turn, so the supervisor's
            // first entry is an answer and not an announcement. Is_OnlineGreeting decides whether the
            // owner's phone BUZZES for a session coming up; the topic is created by
            // Resolve_ThreadId_OrNull_Async before the push filter is consulted at all, so what breaks
            // the deadlock is that there is an entry, not what it says. The greeting shape belongs to
            // the OTHER first turn — the one with nothing to answer — and is asserted there, on the
            // stub, in BootTurnTests.
            var toTheTopic = await Wait_Async(
                () => store.Get_Session_OrNull(orchId)?.TelegramTopicId != null,
                TimeSpan.FromMinutes(2),
                "no Telegram topic was ever created — THE DEFECT, from the other side: the orchestration exists and the owner still has nowhere to type",
                log, telegram);

            Say($"supervisor's first entry -> topic created: {toTheTopic.TotalSeconds:F1} s (fake transport)");

            // AND IT REACHED THE PHONE. The owner asked the CONCIERGE, so the flag that makes a
            // supervisor's answer push instead of being filtered as narration is raised by the app when
            // it files the task — nothing else on this route would raise it. Without that, everything
            // above still happens and the owner is told none of it.
            // MATCHED ON THE AUTHOR MARKER, not on the subject, and the first version of this line was
            // wrong in a way worth keeping: ChannelAppender truncates a long subject in the header with
            // an ellipsis, while the mirror sends the entry's own full text — so comparing the parsed
            // subject against what was sent compares a shortened string with a complete one, and a
            // supervisor whose greeting names its repo directory (which the role command REQUIRES) has
            // a subject long enough for that to bite. What is being asserted is "a supervisor entry
            // reached the owner", and the mirror's own prefix says exactly that.
            var toThePush = await Wait_Async(
                () => telegram.Has_Sent_Containing(MIRROR_SUPERVISOR_PREFIX),
                TimeSpan.FromMinutes(2),
                "no supervisor entry was ever pushed to the owner — they asked for this work and the answer stayed in the channel",
                log, telegram);

            Say($"topic -> owner notified: {toThePush.TotalSeconds:F1} s");

            // ── the crew does the work ──────────────────────────────────────────────────────
            var implementerId = store.Get_Session_OrNull(orchId)!.Members.First(member => member.MemberId.StartsWith("imp-", StringComparison.Ordinal)).MemberId;
            seenImplementerId = implementerId;

            var toTheBrief = await Wait_Async(
                () => Spoke_Entries(paths, orchId, implementerId).Any(entry => entry.Author == ChannelAuthors.Supervisor),
                TimeSpan.FromMinutes(4), $"the supervisor never briefed {implementerId}", log, telegram);

            Say($"greeting -> {implementerId} briefed: {toTheBrief.TotalSeconds:F1} s");

            var toTheReport = await Wait_Async(
                () => Spoke_Entries(paths, orchId, implementerId).Any(entry => entry.Author == ChannelAuthors.Implementer),
                TimeSpan.FromMinutes(5), $"{implementerId} never reported", log, telegram);

            Say($"brief -> {implementerId} reported: {toTheReport.TotalSeconds:F1} s");

            // THE WAKE-UP: the supervisor takes a turn on the spoke entry, with the owner silent since
            // their one message. This is stage 1c's property, re-proved inside the whole round.
            var ownerEntriesBefore = Owner_Entries(paths, orchId).Count(entry => entry.Author == ChannelAuthors.Supervisor);

            var toTheWake = await Wait_Async(
                () => Owner_Entries(paths, orchId).Count(entry => entry.Author == ChannelAuthors.Supervisor) > ownerEntriesBefore,
                TimeSpan.FromMinutes(4), "the supervisor was never woken by the spoke, so the owner was never told", log, telegram);

            Say($"report -> supervisor came back to the owner: {toTheWake.TotalSeconds:F1} s");

            // THE WORK ITSELF, not the traffic about it.
            var hello = Path.Combine(repo, "HELLO.md");
            Assert.True(File.Exists(hello), $"HELLO.md was never written.{Environment.NewLine}{Dump(paths, orchId, implementerId, log, telegram)}");
            Assert.Contains("HELLO.md", Git(repo, "log --name-only --oneline"), StringComparison.Ordinal);

            wholeRound.Stop();

            Report_Cost(paths, orchId, implementerId, wholeRound.Elapsed);
        }
        finally
        {
            // BEFORE the cancel and before the delete, and OUTSIDE the success path: a live round that
            // failed is the one whose evidence nobody can reconstruct, and xUnit shows a passing test's
            // output nowhere at all.
            if (seenOrchId != null)
                Save_Evidence(paths, seenOrchId, seenImplementerId ?? "imp-1", log, telegram);

            await cancellation.CancelAsync();

            try
            {
                await loop;
            }
            catch (OperationCanceledException)
            {
                // The only way these loops end.
            }

            Say(Environment.NewLine + "--- engine log ---" + Environment.NewLine + log.Dump());

            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // A session still exiting may hold the folder for a moment; the temp folder is not precious.
            }
        }
    }

    /// <summary>
    /// The round, kept. Everything else here lives in a temp folder this test deletes, and xUnit shows
    /// a passing test's output nowhere — so a green run would leave no evidence at all, which is the
    /// one thing a live measurement must not do. Same place and the same reason as stage 1c's.
    /// </summary>
    void Save_Evidence(ISupervisionPaths paths, string orchId, string implementerId, RecordingLog_Fake log, CapturingTelegram_Fake telegram)
    {
        var folder = Path.Combine(Path.GetTempPath(), "aiorchestrator-stage-1d", "live");
        Directory.CreateDirectory(folder);

        File.WriteAllText(
            Path.Combine(folder, "REPORT.txt"),
            $"AIOrchestrator stage 1d — one owner message to the concierge, {DateTime.Now:yyyy-MM-dd HH:mm}{Environment.NewLine}{Environment.NewLine}"
            + string.Join(Environment.NewLine, _measures) + Environment.NewLine + Environment.NewLine
            + Dump(paths, orchId, implementerId, log, telegram));
    }

    readonly List<string> _measures = [];

    void Report_Cost(ISupervisionPaths paths, string orchId, string implementerId, TimeSpan wholeRound)
    {
        var total = 0.0;

        foreach (var (role, memberId) in new[]
        {
            (SessionRoles.General, SessionLaunch_Factory.GENERAL_MEMBER_ID),
            (SessionRoles.Supervisor, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID),
            (SessionRoles.Implementer, implementerId),
        })
        {
            var stateFile = PrintSessionState_Store.Get_StateFile(paths, role, role == SessionRoles.General ? ChannelDiscovery.GENERAL_ORCH_ID : orchId, memberId);
            var state = PrintSessionState_Store.Read_OrNull(stateFile);

            if (state == null)
                continue;

            foreach (var turn in state.ExecutedTurns)
            {
                total += turn.CostUsd ?? 0;
                Say($"  {turn.RequestId}: {turn.Outcome}, {(turn.CostUsd?.ToString("F4") ?? "unknown")} USD, entries [{turn.FirstEntryIndex}]–[{turn.LastEntryIndex}]");
            }
        }

        Say($"WHOLE ROUND: {wholeRound.TotalSeconds:F1} s, {total:F4} USD");
    }

    void Say(string line)
    {
        _measures.Add(line);
        _output.WriteLine(line);
    }

    // ----- the world the round runs in -----

    static void Write_Config(ISupervisionPaths paths, string repo)
    {
        File.WriteAllText(
            paths.ConfigFile,
            $$"""
            {
              "repos": [{"name": "{{REPO_NAME}}", "path": {{System.Text.Json.JsonSerializer.Serialize(repo)}}}],
              "telegramSupergroupChatId": {{SUPERGROUP_CHAT_ID}},
              "telegramOwnerUserId": {{OWNER_USER_ID}},
              "generalSupervisorModel": "haiku",
              "supervisorModel": "haiku",
              "implementerModel": "haiku",
              "runners": {
                "general": {"runner": "print", "resume": "fresh"},
                "supervisor": {"runner": "stream", "resume": "transcript"},
                "implementer": {"runner": "print", "resume": "transcript"},
                "reviewer": {"runner": "print", "resume": "transcript"}
              },
              "printRunner": {"coalesceSeconds": 1, "turnTimeoutMinutes": 4}
            }
            """);

        File.WriteAllText(paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");
    }

    /// <summary>
    /// The concierge's protocol, cut to the two actions this round needs. Project-scoped, in its own
    /// permanent working directory, so nothing is installed into the machine's real ~/.claude.
    /// </summary>
    static void Write_ProbeGeneralCommand(ISupervisionPaths paths)
    {
        var commands = Path.Combine(paths.GeneralFolder, ".claude", "commands");
        Directory.CreateDirectory(commands);

        File.WriteAllText(Path.Combine(commands, "general-supervisor.md"),
            "---\ndescription: probe general supervisor\n---\n" +
            "You are the GENERAL SUPERVISOR, run by a bridge: no watcher, no Monitor.\n\n" +
            "FIRST, one Bash call — the Read tool does not expand variables:\n" +
            "`echo \"ROOT=$AIORCH_SUPERVISION_ROOT\"; cat \"$AIORCH_SUPERVISION_ROOT/general/channel.md\"`\n\n" +
            "Read that channel as a LOG. Act on the owner's unanswered trailing message, IN THIS TURN. Never answer\n" +
            "with a greeting or a question about what they would like — they have already said it.\n\n" +
            "When the owner asks for work on a repo, START AN ORCHESTRATION. Write the request file FIRST, with one\n" +
            "Bash call, BEFORE you say anything:\n\n" +
            "printf '%s' '{\"action\":\"start-orchestration\",\"repo\":\"" + REPO_NAME + "\",\"mode\":\"full\",\"task\":\"THE OWNER WORDS\"}' > \"$AIORCH_SUPERVISION_ROOT/.requests/start.json\"\n\n" +
            "Replace THE OWNER WORDS with the owner's request, verbatim, with no double quotes in it. The `task` field is\n" +
            "how their words reach the new orchestration: the APP writes it into that orchestration's owner channel. You\n" +
            "never touch another orchestration's channel yourself.\n\n" +
            "Your FINAL MESSAGE IS your own channel entry: first line the subject, a blank line, then the body. Never\n" +
            "append to a channel file with Bash.\n");
    }

    /// <summary>
    /// The concierge's LONG-TERM MEMORY, which on a real machine the owner has taught it and which on
    /// a fresh one is a seed that says "ask me where to learn the landscape". It loads automatically
    /// into every general session and outranks a role command in practice, so a probe that leaves the
    /// seed in place is testing the seed.
    /// </summary>
    static void Write_ProbeGeneralMemory(ISupervisionPaths paths)
    {
        File.WriteAllText(
            Path.Combine(paths.GeneralFolder, "CLAUDE.md"),
            "# General Supervisor — persistent knowledge\n\n" +
            "THE REPO MAP IS COMPLETE. There is exactly one configured repo and you already know it:\n" +
            $"- `{REPO_NAME}` — the owner's only project. Anything they ask for goes there.\n\n" +
            "You have nothing left to learn before acting. Do NOT greet, do NOT ask the owner where to\n" +
            "learn the landscape, do NOT ask what they would like to do: ACT on their message in the turn\n" +
            "it arrives in, exactly as your role command says.\n");
    }

    /// <summary>The crew's protocol — the 1c probe, plus a greeting the push policy can recognise and a commit.</summary>
    static void Write_ProbeRepo(string repo)
    {
        var commands = Path.Combine(repo, ".claude", "commands");
        Directory.CreateDirectory(commands);

        File.WriteAllText(Path.Combine(commands, "supervisor.md"),
            "---\ndescription: probe supervisor\n---\n" +
            "You are the SUPERVISOR of orchestration $ARGUMENTS. You are run by a bridge: no watcher, no Monitor.\n\n" +
            "You are woken by EVERY channel you listen to — the owner's, and each member's spoke. The prompt names the\n" +
            "channel each message came from. Your FINAL MESSAGE IS your channel entries: address each part with a line\n" +
            "reading `TO: <channel>` on its own, then the subject, a blank line, and the body. Text before the first\n" +
            "`TO:` goes to the owner.\n\n" +
            "YOUR FIRST ENTRY TO THE OWNER IS A GREETING, and its first line must begin exactly `supervisor online — `\n" +
            "followed by your working directory. If a task came with it, put the greeting in a `TO: owner` block AND\n" +
            "brief imp-1 in a `TO: imp-1` block in the SAME message — first line of each block is its subject.\n\n" +
            "When imp-1 reports, tell the owner in a `TO: owner` block that it is done.\n\n" +
            "NEVER use Bash to append to a channel file — the bridge writes every entry from your final message, and a\n" +
            "session that also writes one produces it twice. A question ends the turn exactly as an answer does.\n");

        File.WriteAllText(Path.Combine(commands, "implementer.md"),
            "---\ndescription: probe implementer\n---\n" +
            "You are IMPLEMENTER $ARGUMENTS, run by a bridge: no watcher, no Monitor.\n\n" +
            "FIRST, resolve your environment with ONE Bash call and read your channel — the root and the mode travel as\n" +
            "environment variables, which the Read tool does not expand:\n" +
            "`cat \"${AIORCH_SUPERVISION_ROOT}/${AIORCH_ID}/${AIORCH_MEMBER}/channel.md\"`\n\n" +
            "Then do exactly what the newest entry from the supervisor says, with the Bash tool, in the working directory\n" +
            "you were started in, and COMMIT your work with git.\n\n" +
            "Your FINAL MESSAGE IS your channel entry: first line the subject, then a blank line, then the body. Nothing\n" +
            "else. DO NOT append to your channel yourself — the bridge writes your entry from that message.\n");
    }

    // ----- reading the world -----

    static IReadOnlyList<IChannelEntry> Owner_Entries(ISupervisionPaths paths, string orchId)
    {
        var file = paths.Get_OwnerChannelFile(orchId);

        return File.Exists(file) ? ChannelEntry_Parser.Parse_All(File.ReadAllText(file)) : [];
    }

    static IReadOnlyList<IChannelEntry> Spoke_Entries(ISupervisionPaths paths, string orchId, string memberId)
    {
        var file = paths.Get_ImplementerChannelFile(orchId, memberId);

        return File.Exists(file) ? ChannelEntry_Parser.Parse_All(File.ReadAllText(file)) : [];
    }

    static string Dump(ISupervisionPaths paths, string orchId, string memberId, RecordingLog_Fake log, CapturingTelegram_Fake telegram)
    {
        return $"--- owner channel ---{Environment.NewLine}{string.Join(Environment.NewLine, Owner_Entries(paths, orchId).Select(entry => entry.RawText))}"
            + $"{Environment.NewLine}--- {memberId} ---{Environment.NewLine}{string.Join(Environment.NewLine, Spoke_Entries(paths, orchId, memberId).Select(entry => entry.RawText))}"
            + $"{Environment.NewLine}--- sent to telegram ---{Environment.NewLine}{telegram.Dump_Sent()}"
            + $"{Environment.NewLine}--- engine log ---{Environment.NewLine}{log.Dump()}";
    }

    /// <summary>
    /// Waits on the world rather than on a tick: the engine owns its own loops here, so the test's job
    /// is to look, and to fail with everything a reader needs rather than with a bare timeout.
    /// </summary>
    static async Task<TimeSpan> Wait_Async(Func<bool> condition, TimeSpan budget, string whatDidNotHappen, RecordingLog_Fake log, CapturingTelegram_Fake telegram)
    {
        var stopwatch = Stopwatch.StartNew();
        var deadline = DateTime.UtcNow + budget;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return stopwatch.Elapsed;

            await Task.Delay(200);
        }

        Assert.Fail($"{whatDidNotHappen} (waited {budget.TotalSeconds:F0} s)"
            + $"{Environment.NewLine}--- sent to telegram ---{Environment.NewLine}{telegram.Dump_Sent()}"
            + $"{Environment.NewLine}--- engine log ---{Environment.NewLine}{log.Dump()}");

        return stopwatch.Elapsed;
    }

    static string Git(string repo, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = repo,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return output;
    }
}

/// <summary>
/// A spawner that refuses. Every role in this round is bridge-driven, so a terminal spawn would mean
/// the runner fell back — and a window opening on the tester's desktop is a far worse way to find that
/// out than a failure that names it.
/// </summary>
internal sealed class NoTerminal_Fake : ISessionSpawner
{
    public int? Spawn(ISpawnCommand command)
    {
        throw new Exception($"A terminal spawn was attempted for '{command.WorkingDirectory}' — every role in this round is configured bridge-driven, so this means a runner fell back rather than registering.");
    }
}
