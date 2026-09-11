using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Running.RoleRunnerConfig;

namespace AIOrchestratorCoreLib.Running.RunnerConfigs;

/// <summary>
/// The config.json shape of the runner configuration, in one place for the loader and the saver:
/// <code>
/// "runners": {
///   "implementer": { "runner": "print", "resume": "transcript", "permission_mode": null },
///   "supervisor":  { "runner": "stream", "resume": "transcript" },
///   "general":     { "runner": "print", "resume": "fresh" },
///   "sessionMemoryMax": "3G"
/// },
/// "printRunner": { "maxConcurrentTurns": 10, "maxConcurrentTurnsPerOrchestration": 3,
///                  "turnTimeoutMinutes": 30, "coalesceSeconds": 3, "streamSilenceSeconds": 120,
///                  "memberDigestMinutes": 5 }
/// </code>
/// Tolerant on the way in — an absent block, an absent role, an unknown word all read as the
/// default, because a typo in a hand-edited file must not stop the app from starting — and
/// explicit on the way out: every role and every limit is written, so the owner sees the whole
/// surface and its current values without reading this file.
/// </summary>
public static class RunnerConfigs_Json
{
    public const string RUNNERS_KEY = "runners";
    public const string LIMITS_KEY = "printRunner";
    public const string RUNNER_KEY = "runner";
    public const string RESUME_KEY = "resume";
    public const string PERMISSION_MODE_KEY = "permission_mode";
    public const string SETTINGS_KEY = "settings";
    public const string MAX_CONCURRENT_TURNS_KEY = "maxConcurrentTurns";
    public const string MAX_CONCURRENT_TURNS_PER_ORCHESTRATION_KEY = "maxConcurrentTurnsPerOrchestration";
    public const string TURN_TIMEOUT_MINUTES_KEY = "turnTimeoutMinutes";
    public const string COALESCE_SECONDS_KEY = "coalesceSeconds";
    public const string STREAM_SILENCE_SECONDS_KEY = "streamSilenceSeconds";

    /// <summary>
    /// The digest window of <see cref="PendingTraffic.WakeUp_Policy"/>, in minutes — how long a
    /// member's ordinary entry may be held before it buys its supervisor a turn. Under
    /// <see cref="LIMITS_KEY"/> with its siblings because it is a thing the DISPATCHER does with
    /// turns, not a thing a spawn does with a session.
    ///
    /// <para>
    /// READ BY <see cref="Read_MemberDigestWindow_OrDefault"/> AND BY NOTHING GENERIC, which is the
    /// whole difference between a setting and an accident. <c>0</c> means the owner turned the digest
    /// OFF (one entry, one turn — the behaviour before 2026-09-09), so the positive reader that governs
    /// a timeout would have silently given them the 5-minute default back; and the non-negative reader
    /// that replaced it did the same thing to a NEGATIVE value, which is the review finding of
    /// 2026-09-09. Below zero also means off, and above the ceiling is refused out loud.
    /// </para>
    /// </summary>
    public const string MEMBER_DIGEST_MINUTES_KEY = "memberDigestMinutes";

    /// <summary>
    /// The per-session memory ceiling, and it sits in the ROLES block rather than in
    /// <see cref="LIMITS_KEY"/> on purpose: it governs how a session is SPAWNED, which is what
    /// every other key under <c>runners</c> governs, while <c>printRunner</c> holds what the
    /// dispatcher does with turns. A role can never be called this — the role keys are a closed set
    /// (<see cref="SessionRole_Names.ALL"/>) and the reader only ever asks for those — so the two
    /// cannot collide.
    /// </summary>
    public const string SESSION_MEMORY_MAX_KEY = "sessionMemoryMax";

    public static IRunnerConfigs Parse(JsonObject? configRoot)
    {
        var defaults = RunnerConfigs_Factory.Create_Default();

        if (configRoot == null)
            return defaults;

        Dictionary<SessionRoles, IRoleRunnerConfig> roles = [];
        List<string> rejections = [];

        if (configRoot[RUNNERS_KEY] is JsonObject runnersNode)
        {
            foreach (var role in SessionRole_Names.ALL)
            {
                if (runnersNode[SessionRole_Names.Get_ConfigKey(role)] is JsonObject roleNode)
                    roles[role] = Parse_Role(role, roleNode, rejections);
            }
        }

        var limits = configRoot[LIMITS_KEY] as JsonObject;
        var sessionMemoryMax = Read_SessionMemoryMax_OrDefault(configRoot[RUNNERS_KEY] as JsonObject, rejections);

        return RunnerConfigs_Factory.Create(
            roles,
            Read_PositiveInt_OrDefault(limits, MAX_CONCURRENT_TURNS_KEY, defaults.MaxConcurrentTurns),
            Read_PositiveInt_OrDefault(limits, MAX_CONCURRENT_TURNS_PER_ORCHESTRATION_KEY, defaults.MaxConcurrentTurnsPerOrchestration),
            TimeSpan.FromMinutes(Read_PositiveDouble_OrDefault(limits, TURN_TIMEOUT_MINUTES_KEY, defaults.TurnTimeout.TotalMinutes)),
            TimeSpan.FromSeconds(Read_NonNegativeDouble_OrDefault(limits, COALESCE_SECONDS_KEY, defaults.CoalesceWindow.TotalSeconds)),
            TimeSpan.FromSeconds(Read_PositiveDouble_OrDefault(limits, STREAM_SILENCE_SECONDS_KEY, defaults.SilenceLimit.TotalSeconds)),
            rejections,
            sessionMemoryMax,
            Read_MemberDigestWindow_OrDefault(limits, rejections));
    }

    /// <summary>
    /// Sets both blocks on <paramref name="configRoot"/>, replacing whatever was there.
    ///
    /// <para>
    /// WHAT IS WRITTEN IS WHAT THE APP IS USING, not what the operator typed — the header's contract
    /// ("explicit on the way out: every role and every limit is written, so the owner sees the whole
    /// surface and its current values"). So a REFUSED value is overwritten by the effective one at
    /// the next save: <c>memberDigestMinutes: 10</c> becomes <c>5</c>, exactly as an unparseable
    /// <see cref="SESSION_MEMORY_MAX_KEY"/> becomes <c>3G</c> and a negative digest becomes <c>0</c>.
    /// Reviewed and kept on 2026-09-10: a file that shows the values in force is the point of writing
    /// it out at all, and preserving a number the app is not using would make the file a record of
    /// two different configurations, one of which is a lie about behaviour.
    /// </para>
    /// <para>
    /// WHAT MAKES THAT HONEST IS THE REFUSAL LINE, AND IT NAMES BOTH NUMBERS — "is 10, above the
    /// 5-minute ceiling … refused, the digest stays at 5 minutes" — logged once by
    /// <c>PrintTurnDispatcherModel.Report_ConfigRejections_Once</c>. Before that reader existed the
    /// overwrite was silent in both places at once, which was the review's finding rather than this
    /// method's behaviour.
    /// </para>
    /// </summary>
    public static void Write(JsonObject configRoot, IRunnerConfigs configs)
    {
        var runnersNode = new JsonObject();

        foreach (var role in SessionRole_Names.ALL)
        {
            var roleConfig = configs.Get_ForRole(role);

            runnersNode[SessionRole_Names.Get_ConfigKey(role)] = new JsonObject
            {
                [RUNNER_KEY] = SessionRunner_Names.Get_Word(roleConfig.Runner),
                [RESUME_KEY] = ResumeMode_Names.Get_Word(roleConfig.Resume),
                [PERMISSION_MODE_KEY] = roleConfig.PermissionMode,
                [SETTINGS_KEY] = roleConfig.Settings,
            };
        }

        runnersNode[SESSION_MEMORY_MAX_KEY] = configs.SessionMemoryMax;

        configRoot[RUNNERS_KEY] = runnersNode;
        configRoot[LIMITS_KEY] = new JsonObject
        {
            [MAX_CONCURRENT_TURNS_KEY] = configs.MaxConcurrentTurns,
            [MAX_CONCURRENT_TURNS_PER_ORCHESTRATION_KEY] = configs.MaxConcurrentTurnsPerOrchestration,
            [TURN_TIMEOUT_MINUTES_KEY] = configs.TurnTimeout.TotalMinutes,
            [COALESCE_SECONDS_KEY] = configs.CoalesceWindow.TotalSeconds,
            [STREAM_SILENCE_SECONDS_KEY] = configs.SilenceLimit.TotalSeconds,
            [MEMBER_DIGEST_MINUTES_KEY] = configs.MemberDigestWindow.TotalMinutes,
        };
    }

    /// <summary>
    /// One role's block. Tolerant on everything the owner can merely mistype — an unknown word reads
    /// as the default — and REFUSING on the one thing that is not a typo: <c>bg</c> without Remote
    /// Control disabled. A refused role falls back to <c>terminal</c> (today's shape, a window the
    /// owner can see) rather than to the ladder, because the ladder is for a transport that BROKE
    /// and this is a transport that was never allowed to start.
    /// </summary>
    static IRoleRunnerConfig Parse_Role(SessionRoles role, JsonObject roleNode, List<string> rejections)
    {
        var defaults = RoleRunnerConfig_Factory.Create_Default(role);
        var runner = SessionRunner_Names.Parse_OrNull(Read_String_OrNull(roleNode, RUNNER_KEY)) ?? defaults.Runner;
        var resume = ResumeMode_Names.Parse_OrNull(Read_String_OrNull(roleNode, RESUME_KEY)) ?? defaults.Resume;
        var permissionMode = Read_String_OrNull(roleNode, PERMISSION_MODE_KEY);
        var settings = Read_String_OrNull(roleNode, SETTINGS_KEY);

        if (runner != SessionRunners.Bg)
            return RoleRunnerConfig_Factory.Create(runner, resume, permissionMode, settings);

        var (bgSettings, refusal) = BgSettings_Rule.Resolve(settings);

        if (refusal != null)
        {
            rejections.Add($"role '{SessionRole_Names.Get_ConfigKey(role)}' asks for runner '{SessionRunner_Names.BG}' but {refusal} — refused, the role runs in a terminal instead");
            return RoleRunnerConfig_Factory.Create(SessionRunners.Terminal, resume, permissionMode, settings);
        }

        return RoleRunnerConfig_Factory.Create(runner, resume, permissionMode, bgSettings);
    }

    /// <summary>
    /// A ceiling this host cannot read is REFUSED, not silently replaced: an operator who typed
    /// "3 GB" and got the 3G default would never learn the difference, and an operator who typed
    /// "300M" meaning three hundred megabytes must not be quietly given three gigabytes. The word
    /// forms that mean "no ceiling" pass through untouched — turning the cap off is a decision, not
    /// a typo.
    /// </summary>
    static string Read_SessionMemoryMax_OrDefault(JsonObject? runnersNode, List<string> rejections)
    {
        if (runnersNode == null)
            return RunnerConfigs_Factory.DEFAULT_SESSION_MEMORY_MAX;

        var written = Read_String_OrNull(runnersNode, SESSION_MEMORY_MAX_KEY);

        if (written == null)
            return RunnerConfigs_Factory.DEFAULT_SESSION_MEMORY_MAX;

        if (SessionSandbox.MemorySize_Parser.Means_NoLimit(written))
            return written.Trim();

        // NORMALISED, not echoed: what is stored here is handed to systemd verbatim at every spawn,
        // so "3072M" becomes "3G" and anything systemd would refuse never gets that far.
        var normalised = SessionSandbox.MemorySize_Parser.Normalise_OrNull(written);

        if (normalised != null)
            return normalised;

        rejections.Add(
            $"'{RUNNERS_KEY}.{SESSION_MEMORY_MAX_KEY}' is '{written}', which is not a memory size "
            + $"(expected e.g. '3G', '3072M', or 'none') — refused, sessions are capped at "
            + $"{RunnerConfigs_Factory.DEFAULT_SESSION_MEMORY_MAX} instead");

        return RunnerConfigs_Factory.DEFAULT_SESSION_MEMORY_MAX;
    }

    /// <summary>
    /// THE DIGEST WINDOW, WITH ITS TWO ENDS DECIDED SEPARATELY. Both were review findings of
    /// 2026-09-09 and both were the generic reader answering a question it was not asked.
    ///
    /// <para>
    /// BELOW ZERO MEANS OFF, not "have the default back". <c>Read_NonNegativeDouble_OrDefault</c>
    /// collapses "absent", "unreadable" and "negative" into one answer, so an owner who typed
    /// <c>-1</c> got the LONGEST wait in answer to a request for none — and the factory's own
    /// docstring three lines from the key said the opposite ("zero and below both mean one entry, one
    /// turn"). Off and off are the same answer; there is nowhere else a minus sign could honestly
    /// lead.
    /// </para>
    /// <para>
    /// ABOVE THE CEILING IS REFUSED WITH A LINE THE OPERATOR READS, and the default applied — the
    /// shape <see cref="Read_SessionMemoryMax_OrDefault"/> already uses, and for the same reason: a
    /// value silently replaced is a value nobody learns about. The ceiling and the measurement behind
    /// it are <see cref="RunnerConfigs_Factory.MAX_MEMBER_DIGEST_WINDOW"/>; the short version is that
    /// past it the app tells the supervisor it is late on a verdict for a report the app itself is
    /// holding, and spends that quiet spell's single nudge token on the false alarm.
    /// </para>
    /// <para>
    /// NOT A THROW, on either end — AND IT WAS ONE. Review finding, 2026-09-10: the ceiling was
    /// applied by building the <c>TimeSpan</c> first, on a line outside the try that guards the read,
    /// so <c>memberDigestMinutes: 1e11</c> raised <c>OverflowException</c> straight out of
    /// <see cref="Parse"/> — and neither <c>OrchestratorConfig_Loader.Load_OrEmpty</c> nor
    /// <c>IOrchestratorConfigProvider.Get_Current</c> catches. <c>Get_Current</c> runs on the startup
    /// path and on every mirror tick, so one hand-typed number took the WHOLE config read down and
    /// logged one error per tick for ever, with nothing after it running. The comparison is made on
    /// the NUMBER now, in minutes, so the only thing an absurd value can cost is its own setting.
    /// <c>1e309</c> is the same door: JSON has no infinity, so it arrives as
    /// <c>double.PositiveInfinity</c>, and the test is written as "is it inside the ceiling" rather
    /// than "is it outside" so that a non-number would be refused rather than passed through.
    /// </para>
    /// </summary>
    static TimeSpan Read_MemberDigestWindow_OrDefault(JsonObject? limits, List<string> rejections)
    {
        var written = Read_Double_OrNull(limits, MEMBER_DIGEST_MINUTES_KEY);

        if (written == null)
            return RunnerConfigs_Factory.DEFAULT_MEMBER_DIGEST_WINDOW;

        if (written.Value <= 0)
            return TimeSpan.Zero;

        if (written.Value <= RunnerConfigs_Factory.MAX_MEMBER_DIGEST_WINDOW.TotalMinutes)
            return TimeSpan.FromMinutes(written.Value);

        rejections.Add(
            $"'{LIMITS_KEY}.{MEMBER_DIGEST_MINUTES_KEY}' is {written.Value:0.#}, above the "
            + $"{RunnerConfigs_Factory.MAX_MEMBER_DIGEST_WINDOW.TotalMinutes:0.#}-minute ceiling: holding a member's "
            + "report longer than that makes the app nudge the supervisor for a verdict on traffic the app "
            + "is itself withholding, and the false alarm spends the one nudge that quiet spell has — "
            + $"refused, the digest stays at {RunnerConfigs_Factory.DEFAULT_MEMBER_DIGEST_WINDOW.TotalMinutes:0.#} minutes "
            + "(0 or less turns it off)");

        return RunnerConfigs_Factory.DEFAULT_MEMBER_DIGEST_WINDOW;
    }

    static string? Read_String_OrNull(JsonObject node, string key)
    {
        try
        {
            return node[key]?.GetValue<string?>();
        }
        catch
        {
            return null;
        }
    }

    static int Read_PositiveInt_OrDefault(JsonObject? node, string key, int fallback)
    {
        var value = Read_PositiveDouble_OrDefault(node, key, fallback);
        return value >= 1 ? (int)value : fallback;
    }

    static double Read_PositiveDouble_OrDefault(JsonObject? node, string key, double fallback)
    {
        var value = Read_NonNegativeDouble_OrDefault(node, key, fallback);
        return value > 0 ? value : fallback;
    }

    static double Read_NonNegativeDouble_OrDefault(JsonObject? node, string key, double fallback)
    {
        var value = Read_Double_OrNull(node, key);
        return value != null && value.Value >= 0 ? value.Value : fallback;
    }

    /// <summary>
    /// The number as written, or null for absent AND for anything that is not a number — a key whose
    /// value cannot be read is indistinguishable from an absent one for every caller here, and the
    /// swallow is the deliberate one this file's header describes: a typo in a hand-edited file must
    /// not stop the app from starting. It is separate from the range readers above because
    /// <see cref="Read_MemberDigestWindow_OrDefault"/> has to tell "absent" from "negative", and a
    /// reader that collapses the two is what made a negative digest mean five minutes.
    /// </summary>
    static double? Read_Double_OrNull(JsonObject? node, string key)
    {
        try
        {
            return node?[key]?.GetValue<double>();
        }
        catch
        {
            return null;
        }
    }
}
