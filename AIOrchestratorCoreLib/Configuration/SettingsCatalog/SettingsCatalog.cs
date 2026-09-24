using AIOrchestratorCoreLib.Configuration.DefaultsSettings;
using AIOrchestratorCoreLib.Configuration.GuardrailSettings;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingDefinition;
using AIOrchestratorCoreLib.Formatting;
using AIOrchestratorCoreLib.Mirroring;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.RoleRunnerConfig;
using AIOrchestratorCoreLib.Running.RunnerConfigs;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Configuration.SettingsCatalog;

/// <summary>
/// THE ONE LIST OF SETTINGS. The resolver, the two shipped presets, and the three user interfaces
/// this plan and the next ones grow (the WPF window, the web page, the Telegram <c>/settings</c>
/// menu) all read THIS — none of them carries its own idea of what a key is called, what it
/// defaults to, or where its value may live. A key that is wrong here is wrong in every one of
/// them at once, which is why most defaults below are READ from the constant that already governs
/// them rather than retyped: a second copy of 900 or of 720 is exactly the drift CLAUDE.md decision
/// 12 forbids. THE SIX MODEL ROWS IN <see cref="Build_Models"/> ARE THE STATED EXCEPTION, and
/// deliberately the other direction: <see cref="OrchestratorConfig.OrchestratorConfig_Factory"/>'s
/// six <c>DEFAULT_*_MODEL</c> fields are READ FROM THIS CATALOGUE, not the other way round — a model
/// row pointing at one of those constants instead would be a type-initializer cycle. See
/// <see cref="Build_Models"/>'s own doc for why.
///
/// <para>
/// WHAT THIS IS NOT: it is not a reader and not a writer. It does not consult config.json,
/// session.json or the presets — it only says what exists. The precedence chain that turns these
/// definitions plus those files into a VALUE is the resolver's, a later task's.
/// </para>
/// <para>
/// THE BOT TOKEN IS DELIBERATELY ABSENT and must stay so. Every renderer lists this catalogue, and
/// a <c>/settings</c> menu that prints <c>telegramBotToken</c> posts the token into the chat the
/// token controls. It lives in secrets.json, which the loader reads by a separate door.
/// </para>
/// <para>
/// CATEGORY <see cref="SettingCategories.Kit"/> IS EMPTY IN v1, and that is planned rather than an
/// omission: <c>repos[].code</c> and the per-user environment exports land there in plan 05.
/// <c>SettingsCatalogTests.EveryCategory_ExceptKit_HasAtLeastOneEntry</c> names the exemption by
/// hand, so the day plan 05 lands, that test is the thing that notices the exemption is stale.
/// </para>
/// <para>
/// <c>Kind = Int</c> IS THE DELIBERATE SURFACE for five keys that are read as <c>double</c> —
/// <c>dispatchPauseThresholdPercent</c>, <c>turnTimeoutMinutes</c>, <c>coalesceSeconds</c>,
/// <c>streamSilenceSeconds</c> and <c>memberDigestMinutes</c>. Sub-unit precision is intentionally
/// not offered on any of the five; this is one statement of that rather than five silent ones.
/// </para>
/// <para>
/// <c>runners.&lt;role&gt;.permission_mode</c> and <c>runners.&lt;role&gt;.settings</c> ARE
/// DELIBERATE OMISSIONS, not a miss by a later completeness pass: <c>RunnerConfigs_Json</c> reads and
/// writes both, but neither is registered here.
/// </para>
/// </summary>
public static class SettingsCatalog
{
    public const string MODELS_PATH_PREFIX = "models.";
    public const string EFFORT_PATH_PREFIX = "effort.";

    /// <summary>Every definition the app knows, in renderer order: Models, Kernel, Phone, Receipts, Pulse, Owner.</summary>
    public static readonly IReadOnlyList<ISettingDefinition> ALL = Build_All();

    /// <summary>The catalogue path of a role's model — the one spelling Task 6 resolves through.</summary>
    public static string Get_ModelPath(SessionRoles role) => MODELS_PATH_PREFIX + SessionRole_Names.Get_ConfigKey(role);

    /// <summary>The catalogue path of a role's effort level — the one spelling Task 7 resolves through.</summary>
    public static string Get_EffortPath(SessionRoles role) => EFFORT_PATH_PREFIX + SessionRole_Names.Get_ConfigKey(role);

    /// <summary>
    /// The definition for a path, or null. Matches <see cref="ISettingDefinition.Path"/> first and
    /// <see cref="ISettingDefinition.LegacyPath_OrNull"/> second, so the spelling already written in
    /// every config.json on both machines still finds its setting — ordinal, because a path is a
    /// wire spelling and not prose.
    /// </summary>
    public static ISettingDefinition? Find_OrNull(string path)
    {
        foreach (var definition in ALL)
        {
            if (string.Equals(definition.Path, path, StringComparison.Ordinal))
                return definition;
        }

        foreach (var definition in ALL)
        {
            if (definition.LegacyPath_OrNull != null && string.Equals(definition.LegacyPath_OrNull, path, StringComparison.Ordinal))
                return definition;
        }

        return null;
    }

    /// <summary>Every definition in one category, in catalogue order — a renderer's menu section.</summary>
    public static IReadOnlyList<ISettingDefinition> In_Category(SettingCategories category)
    {
        List<ISettingDefinition> matches = [];

        foreach (var definition in ALL)
        {
            if (definition.Category == category)
                matches.Add(definition);
        }

        return matches;
    }

    static IReadOnlyList<ISettingDefinition> Build_All()
    {
        List<ISettingDefinition> all = [];

        all.AddRange(Build_Models());
        all.AddRange(Build_Kernel());
        all.AddRange(Build_Phone());
        all.AddRange(Build_Receipts());
        all.AddRange(Build_Pulse());
        all.AddRange(Build_Owner());

        return all;
    }

    // ---------------------------------------------------------------------------------------
    // MODELS AND EFFORT — twelve keys, one pair per role.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// THE LADDER IS NOT CHANGED BY THIS PLAN, only described by it. An absent
    /// <c>models.reviewer</c> or <c>models.solo</c> falls to <c>models.implementer</c> BEFORE the
    /// shipped default below (<c>OrchestratorConfig_Factory.First_StatedModel</c>), so a machine
    /// whose config.json names only an implementer model still has its reviewer and solo follow it,
    /// exactly as they did before the catalogue existed.
    /// </summary>
    const string MODEL_LADDER_NOTE =
        "An absent reviewer or solo model falls to the implementer's before this shipped default " +
        "(the ladder in OrchestratorConfig_Factory.First_StatedModel), and that ladder is unchanged by the catalogue.";

    /// <summary>
    /// NULL MEANS NO FLAG AT ALL, which is not the same as a level named "default": the launcher
    /// emits <c>--effort</c> only when a level is set, so null leaves the CLI's own default in
    /// charge. The layer ABOVE this one is the per-orchestration <c>/effort</c> dial
    /// (session.json's <c>supervisorEffortOverride</c> / <c>implementerEffortOverride</c>, CLAUDE.md
    /// decision 24) — a solo sits on the implementer slot there, as it does for the model.
    /// </summary>
    const string EFFORT_NULL_NOTE =
        "null means no --effort flag at all — the CLI's own default, not a level called 'default'. " +
        "The layer above is the per-orchestration /effort dial in session.json " +
        "(supervisorEffortOverride / implementerEffortOverride); a solo sits on the implementer slot, as it does for the model.";

    /// <summary>
    /// THE OWNER'S OWN DIRECTIVE, carried here from the constant that used to hold it. Until
    /// 2026-09-12 this sentence was the XML doc of <c>SpawnCommand_Builder.SUPERVISION_EFFORT_LEVEL</c>,
    /// a compiled <c>const "xhigh"</c> applied to the supervisor and the solo whenever no
    /// per-orchestration override was set — CODE, so changing it needed a rebuilt app actually running
    /// (CLAUDE.md decision 23), while the MODEL on the same command line was already read live from
    /// config.json. The value did not change when it moved: the <c>classic</c> preset carries xhigh for
    /// these two roles, and an absent <c>preset</c> key means classic. The SHIPPED default registered
    /// here is null for all six roles, because effort is billed thinking and the owner named two.
    /// </summary>
    const string EFFORT_OWNER_DIRECTIVE_NOTE =
        "Owner directive 2026-09-09: \"XHigh effort in each solo and sup session\" — the two roles that " +
        "talk to the owner and decide. That xhigh lives in the classic preset, not in this shipped " +
        "default, so a machine on quiet carries no flag. Verified against the installed CLI, whose " +
        "--help lists --effort as (low, medium, high, xhigh, max).";

    /// <summary>
    /// ALL SIX ROWS ARE LITERALS, NEVER A READ OF <see cref="OrchestratorConfig_Factory"/>'S OWN
    /// CONSTANTS (moved 2026-09-12, task 6 of this plan). They used to split: supervisor and
    /// implementer were deliberate literals because pointing them at
    /// <c>OrchestratorConfig_Factory.DEFAULT_SUPERVISOR_MODEL</c> (then <c>"claude-fable-5-1"</c>,
    /// owner directive 2026-09-09) while <c>"opus"</c> was the REGISTERED shipped default here would
    /// have been a live mismatch; the other four read the factory's constants directly. Task 6 makes
    /// <see cref="OrchestratorConfig_Factory"/>'s six <c>DEFAULT_*_MODEL</c> fields <c>static
    /// readonly</c>, computed BY READING THIS CATALOGUE (<c>Read_ShippedModel</c>) — so a row here that
    /// still read the factory's constant would be a type-initializer cycle: this class's static
    /// constructor would call into the factory's, which calls back into this class's before it has
    /// finished running, and the reader would observe an unassigned <c>null</c>. Literals here are
    /// what break the cycle; the factory derives FROM the catalogue, never the other way round. Four
    /// tests were red on purpose until this task — <c>PerRoleModelDefaultsTests</c> (three cases) and
    /// <c>OrchestratorConfigLoaderGuardrailsTests.Save_OverACorruptConfigJson_StillSucceeds_AndWritesTheKnownKeys</c>
    /// — and both sides now agree the catalogue is the one source of the shipped default.
    ///
    /// <para>
    /// RULED 2026-09-12 (task-6 fix round 1), SEPARATELY FROM THE CYCLE ABOVE: <c>classic</c> used to
    /// restate these same four values (at the older Fable id) as preset rows, which meant a machine
    /// naming no preset — the common case — never actually got the catalogue's Opus, only Fable. Those
    /// four preset rows are gone from <c>kit/presets/classic.json</c> now; the shipped default declared
    /// here is what a bare config.json actually spawns.
    /// </para>
    /// </summary>
    static IReadOnlyList<ISettingDefinition> Build_Models()
    {
        return
        [
            Model_Definition(SessionRoles.Supervisor, "supervisorModel", "opus", SettingScopes.Orchestration,
                "The model a SUPERVISOR session spawns with — the turn that is the owner's phone line."),
            Model_Definition(SessionRoles.Implementer, "implementerModel", "opus", SettingScopes.Orchestration,
                "The model an IMPLEMENTER session spawns with, and the rung the reviewer and solo fall back to."),
            Model_Definition(SessionRoles.Reviewer, "reviewerModel", "opus", SettingScopes.Machine,
                "The model a REVIEWER session spawns with. A bad review costs more than it saves, so this rung is worth its price."),
            Model_Definition(SessionRoles.Solo, "soloModel", "opus", SettingScopes.Machine,
                "The model a SOLO session spawns with — the session that both talks to the owner and does the work."),
            Model_Definition(SessionRoles.General, "generalSupervisorModel", "sonnet", SettingScopes.Machine,
                "The model the GENERAL supervisor spawns with. Routing, not judging, so it is deliberately cheap."),
            Model_Definition(SessionRoles.Communicator, "communicatorModel", "sonnet", SettingScopes.Machine,
                "The model a COMMUNICATOR session spawns with. Narration, not judging, so it is deliberately cheap."),

            Effort_Definition(SessionRoles.Supervisor, SettingScopes.Orchestration, EFFORT_OWNER_DIRECTIVE_NOTE),
            Effort_Definition(SessionRoles.Implementer, SettingScopes.Orchestration),
            Effort_Definition(SessionRoles.Reviewer, SettingScopes.Machine),
            Effort_Definition(SessionRoles.Solo, SettingScopes.Machine, EFFORT_OWNER_DIRECTIVE_NOTE),
            Effort_Definition(SessionRoles.General, SettingScopes.Machine),
            Effort_Definition(SessionRoles.Communicator, SettingScopes.Machine),
        ];
    }

    static ISettingDefinition Model_Definition(SessionRoles role, string legacyPath, string shippedDefault, SettingScopes scope, string description)
    {
        return SettingDefinition_Factory.Create_String(
            path: Get_ModelPath(role),
            shippedDefault: shippedDefault,
            scope: scope,
            category: SettingCategories.Models,
            label: $"{SessionRole_Names.Get_ConfigKey(role)} model",
            description: $"{description} {MODEL_LADDER_NOTE}",
            restart: RestartKinds.NextSpawn,
            legacyPath: legacyPath,
            validator: SettingValidators.MODEL_WORD);
    }

    /// <summary>
    /// <paramref name="ownerDirectiveNote"/> is appended for the two roles the owner named
    /// (<see cref="EFFORT_OWNER_DIRECTIVE_NOTE"/>) and empty for the other four, so the renderer that
    /// shows a supervisor's or a solo's effort also shows WHY xhigh is what it is — a question every
    /// other role's row genuinely has no answer to.
    /// </summary>
    static ISettingDefinition Effort_Definition(SessionRoles role, SettingScopes scope, string ownerDirectiveNote = "")
    {
        var directive = ownerDirectiveNote.Length == 0 ? string.Empty : $" {ownerDirectiveNote}";

        return SettingDefinition_Factory.Create_Enum(
            path: Get_EffortPath(role),
            values: EffortLevels.ALL,
            shippedDefault: null,
            scope: scope,
            category: SettingCategories.Models,
            label: $"{SessionRole_Names.Get_ConfigKey(role)} effort",
            description: $"The --effort level a {SessionRole_Names.Get_ConfigKey(role)} session spawns with. {EFFORT_NULL_NOTE}{directive}",
            restart: RestartKinds.NextSpawn,
            nullable: true);
    }

    // ---------------------------------------------------------------------------------------
    // KERNEL — how the host runs, what it spawns, and what it refuses.
    // ---------------------------------------------------------------------------------------

    static IReadOnlyList<ISettingDefinition> Build_Kernel()
    {
        List<ISettingDefinition> kernel = [];

        foreach (var role in SessionRole_Names.ALL)
        {
            var key = SessionRole_Names.Get_ConfigKey(role);
            var roleDefault = RoleRunnerConfig_Factory.Create_Default(role);

            kernel.Add(SettingDefinition_Factory.Create_Enum(
                path: $"{RunnerConfigs_Json.RUNNERS_KEY}.{key}.{RunnerConfigs_Json.RUNNER_KEY}",

                // `bg` IS IN THE ENUM AND NOT OFFERED HERE. The fork names it and has not implemented
                // it as a transport (spec §3): a role configured for it is launched in a terminal
                // with a warning. Offering a value that silently becomes another one is worse than
                // not offering it, so the choice list is the three that mean what they say.
                values: [SessionRunner_Names.TERMINAL, SessionRunner_Names.PRINT, SessionRunner_Names.STREAM],
                shippedDefault: SessionRunner_Names.Get_Word(roleDefault.Runner),
                scope: SettingScopes.Machine,
                category: SettingCategories.Kernel,
                label: $"{key} runner",
                description:
                    $"How a {key} session is run. 'terminal' is the shape this app has always had (a window per session, " +
                    "woken by its own watcher); 'print' spawns one `claude -p` per inbound entry and keeps no process between " +
                    "messages; 'stream' keeps one `claude -p --input-format stream-json` process alive and writes on its stdin. " +
                    "'bg' exists in the enum and is NOT offered: it is named by the fork and not implemented as a transport.",
                restart: RestartKinds.NextSpawn));

            kernel.Add(SettingDefinition_Factory.Create_Enum(
                path: $"{RunnerConfigs_Json.RUNNERS_KEY}.{key}.{RunnerConfigs_Json.RESUME_KEY}",
                values: [ResumeMode_Names.TRANSCRIPT, ResumeMode_Names.FRESH],
                shippedDefault: ResumeMode_Names.Get_Word(roleDefault.Resume),
                scope: SettingScopes.Machine,
                category: SettingCategories.Kernel,
                label: $"{key} resume",
                description:
                    $"What a {key} session remembers across its own restarts. 'transcript' continues its own conversation " +
                    "(never --continue, which guesses among the sessions sharing a repo directory); 'fresh' starts empty with " +
                    "only what the session re-reads from disk. The general supervisor ships 'fresh' by owner directive " +
                    "(CLAUDE.md decision 8) — a resumed conversation once re-ran a failed request on boot.",
                restart: RestartKinds.NextSpawn));
        }

        kernel.Add(SettingDefinition_Factory.Create_String(
            path: $"{RunnerConfigs_Json.RUNNERS_KEY}.{RunnerConfigs_Json.SESSION_MEMORY_MAX_KEY}",
            shippedDefault: RunnerConfigs_Factory.DEFAULT_SESSION_MEMORY_MAX,
            scope: SettingScopes.Machine,
            category: SettingCategories.Kernel,
            label: "Session memory ceiling",
            description:
                "The per-session memory ceiling, applied through a Linux cgroup — it does NOTHING on Windows, where the " +
                "spawn has no cgroup to put the session in. The point past which ONE session is doing something nobody " +
                "asked for, not a budget that adds up to the machine.",
            restart: RestartKinds.Host));

        kernel.Add(SettingDefinition_Factory.Create_Int(
            path: $"{RunnerConfigs_Json.LIMITS_KEY}.{RunnerConfigs_Json.MAX_CONCURRENT_TURNS_KEY}",
            shippedDefault: RunnerConfigs_Factory.DEFAULT_MAX_CONCURRENT_TURNS,
            minimum: 1,
            maximum: 100,
            scope: SettingScopes.Machine,
            category: SettingCategories.Kernel,
            label: "Max concurrent turns",
            description: "How many bridge-driven turns may run at once across the whole machine.",
            restart: RestartKinds.Host));

        kernel.Add(SettingDefinition_Factory.Create_Int(
            path: $"{RunnerConfigs_Json.LIMITS_KEY}.{RunnerConfigs_Json.MAX_CONCURRENT_TURNS_PER_ORCHESTRATION_KEY}",
            shippedDefault: RunnerConfigs_Factory.DEFAULT_MAX_CONCURRENT_TURNS_PER_ORCHESTRATION,
            minimum: 1,
            maximum: 50,
            scope: SettingScopes.Machine,
            category: SettingCategories.Kernel,
            label: "Max concurrent turns per orchestration",
            description: "How many bridge-driven turns one orchestration may run at once — so a busy crew cannot starve every other one.",
            restart: RestartKinds.Host));

        kernel.Add(SettingDefinition_Factory.Create_Int(
            path: $"{RunnerConfigs_Json.LIMITS_KEY}.{RunnerConfigs_Json.TURN_TIMEOUT_MINUTES_KEY}",
            shippedDefault: (int)RunnerConfigs_Factory.DEFAULT_TURN_TIMEOUT.TotalMinutes,
            minimum: 1,
            maximum: 600,
            scope: SettingScopes.Machine,
            category: SettingCategories.Kernel,
            label: "Turn timeout (minutes)",
            description: "How long a bridge-driven turn may run before it is abandoned as hung.",
            restart: RestartKinds.Host));

        kernel.Add(SettingDefinition_Factory.Create_Int(
            path: $"{RunnerConfigs_Json.LIMITS_KEY}.{RunnerConfigs_Json.COALESCE_SECONDS_KEY}",
            shippedDefault: (int)RunnerConfigs_Factory.DEFAULT_COALESCE_WINDOW.TotalSeconds,
            minimum: 0,
            maximum: 600,
            scope: SettingScopes.Machine,
            category: SettingCategories.Kernel,
            label: "Coalesce window (seconds)",
            description: "How long entries arriving together are gathered into one turn instead of buying a turn each.",
            restart: RestartKinds.Host));

        kernel.Add(SettingDefinition_Factory.Create_Int(
            path: $"{RunnerConfigs_Json.LIMITS_KEY}.{RunnerConfigs_Json.STREAM_SILENCE_SECONDS_KEY}",
            shippedDefault: (int)RunnerConfigs_Factory.DEFAULT_SILENCE_LIMIT.TotalSeconds,
            minimum: 1,
            maximum: 3600,
            scope: SettingScopes.Machine,
            category: SettingCategories.Kernel,
            label: "Stream silence limit (seconds)",
            description:
                "How long a living stream process may emit nothing before it is treated as hung. A working turn emits " +
                "assistant and hook events throughout, so real silence means real silence.",
            restart: RestartKinds.Host));

        kernel.Add(SettingDefinition_Factory.Create_Int(
            path: $"{RunnerConfigs_Json.LIMITS_KEY}.{RunnerConfigs_Json.MEMBER_DIGEST_MINUTES_KEY}",
            shippedDefault: (int)RunnerConfigs_Factory.DEFAULT_MEMBER_DIGEST_WINDOW.TotalMinutes,
            minimum: 0,
            maximum: (int)RunnerConfigs_Factory.MAX_MEMBER_DIGEST_WINDOW.TotalMinutes,
            scope: SettingScopes.Machine,
            category: SettingCategories.Kernel,
            label: "Member digest window (minutes)",
            description:
                "How long a member's ordinary entry may be held before it buys its supervisor a turn. 0 means wake " +
                "immediately — the behaviour before 2026-09-09. The CEILING is not this catalogue's invention: it is the " +
                "existing refusal's (RunnerConfigs_Json.MEMBER_DIGEST_MINUTES_KEY), because above it the app starts " +
                "complaining that a supervisor is late with a verdict on a report the app is itself holding.",
            restart: RestartKinds.Host));

        kernel.Add(SettingDefinition_Factory.Create_Enum(
            path: "telegramInbound",
            values: [TelegramInbound_Modes.POLL_TEXT, TelegramInbound_Modes.OFF_TEXT],
            shippedDefault: TelegramInbound_Modes.POLL_TEXT,
            scope: SettingScopes.Machine,
            category: SettingCategories.Kernel,
            label: "Telegram inbound",
            description:
                "Whether this host long-polls getUpdates. ONE BOT TOKEN ALLOWS ONE POLLER — a second getUpdates on the " +
                "same token gets HTTP 409 — so a second machine sharing the token sets this to 'off' and mirrors outbound only.",
            restart: RestartKinds.Host));

        kernel.Add(SettingDefinition_Factory.Create_Int(
            path: "telegramSupergroupChatId",
            shippedDefault: null,
            minimum: null,
            maximum: null,
            scope: SettingScopes.Machine,
            category: SettingCategories.Kernel,
            label: "Telegram supergroup chat id",
            description: "The forum supergroup every topic is created in. Absent until the installer or the owner sets it.",
            restart: RestartKinds.None,
            nullable: true));

        kernel.Add(SettingDefinition_Factory.Create_Int(
            path: "telegramOwnerUserId",
            shippedDefault: null,
            minimum: null,
            maximum: null,
            scope: SettingScopes.Machine,
            category: SettingCategories.Kernel,
            label: "Telegram owner user id",
            description: "The one Telegram user whose messages the bridge accepts as the owner's. Absent until set.",
            restart: RestartKinds.None,
            nullable: true));

        kernel.Add(SettingDefinition_Factory.Create_Bool(
            path: "telegramStatusScreenshots",
            shippedDefault: false,
            scope: SettingScopes.Machine,
            category: SettingCategories.Kernel,
            label: "Status screenshots",
            description: "Whether the periodic status carries a picture of the terminal (the 📸 toggle).",
            restart: RestartKinds.None));

        kernel.Add(SettingDefinition_Factory.Create_String(
            path: "voiceTranscribeCommand",
            shippedDefault: "",
            scope: SettingScopes.Machine,
            category: SettingCategories.Kernel,
            label: "Voice transcribe command",
            description: "The command the bridge shells out to for transcribing a voice note. Empty means voice notes are not transcribed.",
            restart: RestartKinds.None));

        kernel.Add(SettingDefinition_Factory.Create_Int(
            path: "orchestrationTokenBudget",
            shippedDefault: null,
            minimum: null,
            maximum: null,
            scope: SettingScopes.Machine,
            category: SettingCategories.Kernel,
            label: "Orchestration token budget",
            description: "A per-orchestration token budget the app warns against. Absent means no budget — the owner has never said.",
            restart: RestartKinds.None,
            nullable: true));

        kernel.Add(SettingDefinition_Factory.Create_StringList(
            path: "highRiskPatterns",
            shippedDefault: GuardrailSettings_Factory.DEFAULT_HIGH_RISK_PATTERNS,
            scope: SettingScopes.Machine,
            category: SettingCategories.Kernel,
            label: "High-risk patterns",
            description:
                "Substrings, matched case-insensitively, that make an agent's question high risk and cost it a typed code. " +
                "AN EMPTY LIST IS NOT ABSENT: it means 'nothing is high risk', which is the owner's to say, while a missing " +
                "key means they never said and gets these back.",
            restart: RestartKinds.Host));

        kernel.Add(SettingDefinition_Factory.Create_Int(
            path: "highRiskCodeExpiryMinutes",
            shippedDefault: GuardrailSettings_Factory.DEFAULT_HIGH_RISK_CODE_EXPIRY_MINUTES,
            minimum: 1,
            maximum: 1440,
            scope: SettingScopes.Machine,
            category: SettingCategories.Kernel,
            label: "High-risk code expiry (minutes)",
            description: "How long a typed high-risk code stays valid. Long enough to fetch the phone from another room; short enough to be a second gesture.",
            restart: RestartKinds.Host));

        // ON BY DEFAULT, OFF IN CLASSIC (owner, 2026-09-23, plan 03 task 15): the shipped default is
        // today's behaviour and the guarded one (ruling R14); classic.json states the owner's way.
        // Read by the engine at the one place a question's risk is decided, per question, so the
        // switch takes effect on the next question with no restart.
        kernel.Add(SettingDefinition_Factory.Create_Bool(
            path: GuardrailSettings_Factory.HIGH_RISK_CONFIRMATION_KEY,
            shippedDefault: GuardrailSettings_Factory.DEFAULT_HIGH_RISK_CONFIRMATION,
            scope: SettingScopes.Machine,
            category: SettingCategories.Kernel,
            label: "High-risk confirmation code",
            description:
                "Whether a tap on a high-risk question — declared RISK: high by the asker, or matching a high-risk pattern — " +
                "also costs a typed 4-digit code. OFF, one tap decides it, but it is still high risk: it never takes a " +
                "default and lapses as a deny. The question contract does not change: every question still carries its " +
                "RISK: line, and the log still records which questions are high risk. A question asked while the code was " +
                "ON keeps its lock after the setting is turned off — the lock is fixed when the question is asked. " +
                "highRiskPatterns: [] only silences the pattern half; this is the switch for both.",
            restart: RestartKinds.None));

        kernel.Add(SettingDefinition_Factory.Create_Int(
            path: "dispatchPauseThresholdPercent",
            shippedDefault: (int)GuardrailSettings_Factory.DEFAULT_DISPATCH_PAUSE_THRESHOLD_PERCENT,
            minimum: 1,
            maximum: 100,
            scope: SettingScopes.Machine,
            category: SettingCategories.Kernel,
            label: "Dispatch pause threshold (%)",
            description: "The usage percentage above which a new turn buys a failure rather than a result, so dispatch pauses instead.",
            restart: RestartKinds.Host));

        kernel.Add(SettingDefinition_Factory.Create_Int(
            path: "buttonExpiryMinutes",
            shippedDefault: GuardrailSettings_Factory.DEFAULT_BUTTON_EXPIRY_MINUTES,
            minimum: 1,
            maximum: 10080,
            scope: SettingScopes.Machine,
            category: SettingCategories.Kernel,
            label: "Button expiry (minutes)",
            description:
                "How long a question's buttons stay tappable. A question asked at the end of a working day must still be " +
                "tappable the next morning, but a keyboard live a week later is furniture, not a decision.",
            restart: RestartKinds.Host));

        kernel.Add(SettingDefinition_Factory.Create_Enum(
            path: $"{DefaultsSettings_Json.DEFAULTS_KEY}.{DefaultsSettings_Json.ORCHESTRATION_MODE_KEY}",
            values: [OrchestrationModes.BASIC, OrchestrationModes.FULL],
            shippedDefault: DefaultsSettings_Factory.DEFAULT_ORCHESTRATION_IS_BASIC ? OrchestrationModes.BASIC : OrchestrationModes.FULL,
            scope: SettingScopes.Machine,
            category: SettingCategories.Kernel,
            label: "Default orchestration mode",
            description:
                "What a new orchestration is unless the owner says otherwise: 'basic' is one solo session, 'full' is a " +
                "supervisor and a crew. Basic is the owner's own choice of 2026-08-13, to stop a crew being spawned for " +
                "work that needs one session.",
            restart: RestartKinds.None));

        kernel.Add(SettingDefinition_Factory.Create_String(
            path: "web.listen",

            // READ BY NOTHING IN THIS PLAN. The HTTP listener that will consume it is plan 04; the key
            // is registered now so the resolver, the presets and the renderers have one spelling of it
            // from the start rather than acquiring one later.
            shippedDefault: "127.0.0.1:7391",
            scope: SettingScopes.Machine,
            category: SettingCategories.Kernel,
            label: "Web listen address",
            description:
                "host:port the settings web page will listen on, or 'off'. READ BY NOTHING YET — the listener is plan 04. " +
                "The loopback default is deliberate: the page has no authentication of its own beyond web.token.",
            restart: RestartKinds.Host,
            validator: SettingValidators.LISTEN_ADDRESS));

        kernel.Add(SettingDefinition_Factory.Create_String(
            path: "web.token",
            shippedDefault: "",
            scope: SettingScopes.Machine,
            category: SettingCategories.Kernel,
            label: "Web token",
            description: "The shared secret the settings web page will require. READ BY NOTHING YET — the listener is plan 04.",
            restart: RestartKinds.Host));

        kernel.Add(SettingDefinition_Factory.Create_Composite(
            path: "repos",
            parserTypeName: "AIOrchestratorCoreLib.Configuration.OrchestratorConfig_Loader",
            scope: SettingScopes.Machine,
            category: SettingCategories.Kernel,
            label: "Repositories",
            description:
                "The repo list, a structure rather than a value — name, path and topic colour per entry. Shown read-only " +
                "here because its own editor already exists; the named parser is the authority on its shape.",
            restart: RestartKinds.Host));

        kernel.Add(SettingDefinition_Factory.Create_Composite(
            path: "planBackend",
            parserTypeName: "AIOrchestratorCoreLib.Planning.PlanBackend.PlanBackend_Loader",
            scope: SettingScopes.Machine,
            category: SettingCategories.Kernel,
            label: "Plan backend",
            description:
                "Where the task ledger lives — a structure with its own kind and per-kind fields. Read-only here; the " +
                "named parser is the authority, and a mistyped kind stays LOUD there rather than quietly becoming a default.",
            restart: RestartKinds.Host));

        kernel.AddRange(Build_SessionState());

        return kernel;
    }

    /// <summary>
    /// PER-ORCHESTRATION STATE THE APP WRITES, listed so a renderer can SHOW an orchestration's
    /// current mode beside the settings that govern it. None of the three is edited through the
    /// catalogue: the owner changes them with /pause, /dnd, /mute and /pc, and the app reconciles
    /// them into session.json. They are Orchestration-scoped because that is literally where they
    /// live, and a renderer that wrote one into config.json would write it where no reader looks.
    /// </summary>
    static IReadOnlyList<ISettingDefinition> Build_SessionState()
    {
        return
        [
            SettingDefinition_Factory.Create_Bool(
                path: "session.paused",
                shippedDefault: false,
                scope: SettingScopes.Orchestration,
                category: SettingCategories.Kernel,
                label: "Paused",
                description:
                    "STATE, NOT A SETTING — the app writes it, /pause toggles it. Asleep, not closed and not finished: " +
                    "outbound traffic is held and replays on unpause, and every waker is gated separately so dormancy is " +
                    "not merely a word. Shown so a renderer can display it; never edited through the catalogue.",
                restart: RestartKinds.None,
                readOnly: true),

            SettingDefinition_Factory.Create_Enum(
                path: "session.telegramMode",
                values: Enum.GetNames<TelegramDeliveryModes>(),
                shippedDefault: nameof(TelegramDeliveryModes.Normal),
                scope: SettingScopes.Orchestration,
                category: SettingCategories.Kernel,
                label: "Telegram delivery mode",
                description:
                    "STATE, NOT A SETTING — the app writes it, /dnd and /mute toggle it. 'Normal' texts as it happens, " +
                    "'Deferred' keeps everything and replays it later (the owner is away), 'Silenced' drops it outright " +
                    "(the owner is reading the same content live in a terminal). The words are the enum's own, which is " +
                    "how session.json spells them. Shown so a renderer can display it; never edited through the catalogue.",
                restart: RestartKinds.None,
                readOnly: true),

            SettingDefinition_Factory.Create_Enum(
                path: "session.ownerPresence",
                values: Enum.GetNames<OwnerPresenceModes>(),
                shippedDefault: nameof(OwnerPresenceModes.Remote),
                scope: SettingScopes.Orchestration,
                category: SettingCategories.Kernel,
                label: "Owner presence",
                description:
                    "STATE, NOT A SETTING — the app writes it, /pc toggles it. WHERE THE OWNER IS, which is not the same " +
                    "as what gets delivered: 'Terminal' means the conversation is happening in the session itself, so " +
                    "nothing is pushed and nothing BLOCKS on a Telegram answer. Shown so a renderer can display it; never " +
                    "edited through the catalogue.",
                restart: RestartKinds.None,
                readOnly: true),
        ];
    }

    // ---------------------------------------------------------------------------------------
    // PHONE — what reaches the owner's phone and how a topic looks. Every row is read by the engine
    // at its point of effect since plan 03 (the last two, topic.modeGlyphs and topic.onClose, in
    // Tasks 7 and 10), so the "read by nothing yet" note these rows carried is gone.
    // ---------------------------------------------------------------------------------------

    static IReadOnlyList<ISettingDefinition> Build_Phone()
    {
        return
        [
            SettingDefinition_Factory.Create_Enum(
                path: "phone.push",
                values: ["filtered", "everything"],
                shippedDefault: "filtered",
                scope: SettingScopes.Machine,
                category: SettingCategories.Phone,
                label: "What reaches the phone",
                description:
                    "'filtered' pushes only what asks, is blocked, carries a picture, or is THE answer (OwnerPush_Policy), " +
                    "and hands the rest over as one message when the turn the owner waited on ends; " +
                    "'everything' mirrors every owner-channel entry. Orchestration topics only — General always gets everything.",
                restart: RestartKinds.None),

            SettingDefinition_Factory.Create_Bool(
                path: "phone.status.periodic",
                shippedDefault: true,
                scope: SettingScopes.Machine,
                category: SettingCategories.Phone,
                label: "Periodic status",
                description:
                    "Whether the app posts master's periodic STATUS (the /status roster plus the current task) into each " +
                    "orchestration topic on its interval — and only when it changed since the last one, so an idle topic " +
                    "stays silent. The away digest is not this: it keeps its own half hour whatever this says.",
                restart: RestartKinds.None),

            SettingDefinition_Factory.Create_Int(
                path: "phone.status.intervalMinutes",
                shippedDefault: 30,
                minimum: 5,
                maximum: 120,
                scope: SettingScopes.Machine,
                category: SettingCategories.Phone,
                label: "Periodic status interval (minutes)",
                description:
                    "Minutes between periodic status messages, when they are on — a grid on the wall clock counted from " +
                    "midnight, shared by every topic. Moves the periodic status only; the away digest stays on 30.",
                restart: RestartKinds.None),

            // THE AWAY DELAY (plan 03 task 18; owner 2026-09-23, ai-orchestrator-29 entry [95]: "the away mode
            // is triggered too soon all the time. That also should be a setting."). The shipped value is
            // today's 15 (ruling R14) and classic states the owner's 60. Read by the engine each time it asks
            // AwayMode_Policy.Should_EnterAway, which is also where 0 is read as "never", not "at once".
            SettingDefinition_Factory.Create_Int(
                path: PhoneSettings.PhoneSettings_Json.AWAY_AFTER_MINUTES_PATH,
                shippedDefault: Bridge.AwayMode_Policy.DEFAULT_AWAY_AFTER_MINUTES,

                // 0 IS LEGAL AND IS THE OFF SWITCH; a day is the ceiling, because a silence longer than
                // that is not a delay any more — it is the switch, and 0 already says so plainly.
                minimum: 0,
                maximum: 1440,
                scope: SettingScopes.Machine,
                category: SettingCategories.Phone,
                label: "Away mode after (minutes)",
                description:
                    "How long you can be silent — no message or tap in ANY topic — while an orchestration is holding " +
                    "its questions for you, before the app decides you are away: it tells you once, parks the " +
                    "questions already asked, tells every session to ask nothing more, and sends a short update per " +
                    "orchestration every 30 minutes until you write again. Any message you send ends it everywhere, " +
                    "and it never starts while you are at a PC (/pc). 0 means away mode never starts by itself.",
                restart: RestartKinds.None),

            SettingDefinition_Factory.Create_Bool(
                path: "phone.appMessagesRing",
                shippedDefault: true,
                scope: SettingScopes.Machine,
                category: SettingCategories.Phone,
                label: "App messages ring",
                description:
                    "Whether your supervisor's (or solo's) narration rings as it arrives. When off, only a question, a " +
                    "BLOCKED ON OWNER, a file, the boot greeting and the answer to what you asked ring; the rest arrives " +
                    "silently. Under phone.push = filtered, narration held for the turn-end summary still rings once, " +
                    "with that summary. Receipts, the busy line and a bare turn-ended line are silent either way.",
                restart: RestartKinds.None),

            SettingDefinition_Factory.Create_Enum(
                path: "phone.replyKeyboard",
                values: ["off", "on"],
                shippedDefault: "off",
                scope: SettingScopes.Machine,
                category: SettingCategories.Phone,
                label: "Reply keyboard",
                description:
                    "The bar of literal slash commands above the input box. Off by default and deliberately not persistent: " +
                    "is_persistent re-shows the bar whenever the phone keyboard hides — which is what the back button does — " +
                    "and disables the icon that collapses it (CLAUDE.md decision 24). 'on' INSTALLS NOTHING YET: the owner " +
                    "answered D5 'off for both presets' (2026-09-14), so plan 03 task 9 wired no carrier and ReplyKeyboard_Markup " +
                    "stays built, tested and deliberately uncalled — wiring it is its own request.",
                restart: RestartKinds.Host),

            // THE AGGREGATION WINDOW AND ITS DISCOUNT (plan 03 task 13, owner 2026-09-23: "it should be a
            // buffer of 6 seconds, giving me the time to press wait if I need"). The shipped values are
            // today's — the fork's 3 s / 2 s (bb91051a, 2026-09-09) — and classic states the owner's 6 s
            // with no discount. The reasoning behind each number lives on the constant it reads, one copy.
            SettingDefinition_Factory.Create_Int(
                path: PhoneSettings.PhoneSettings_Json.AGGREGATION_SECONDS_PATH,
                shippedDefault: Bridge.OwnerDeliveryBuffer.OwnerDeliveryBuffer_Factory.DEFAULT_AGGREGATION_SECONDS,

                // ONE, NOT ZERO: OwnerDeliveryBuffer refuses a window under a second, and a window of zero
                // is a message taken on the tick it lands — out of reach of ⏸ and one turn per message.
                minimum: 1,
                maximum: 60,
                scope: SettingScopes.Machine,
                category: SettingCategories.Phone,
                label: "Message aggregation window (seconds)",
                description:
                    "How long the app holds a message you send before handing it to the session. Longer gives you " +
                    "time to press ⏸ Wait under the receipt — the hold only reaches a message still in the buffer — " +
                    "and lets a burst of typing arrive as ONE turn instead of one each (a turn costs roughly a million " +
                    "input tokens, measured 2026-09-09); shorter makes a lone message reach the session sooner. " +
                    "▶ Send now skips it for one message.",
                restart: RestartKinds.None),

            SettingDefinition_Factory.Create_Int(
                path: PhoneSettings.PhoneSettings_Json.FINISHED_MESSAGE_SECONDS_PATH,
                shippedDefault: Bridge.OwnerDeliveryBuffer.OwnerDeliveryBuffer_Factory.FINISHED_MESSAGE_QUIET_SECONDS,

                // ZERO IS LEGAL here and only here: it is "a finished message leaves on the next tick".
                // Never longer than the window — OwnerDeliveryBufferModel clamps it at the point of use.
                minimum: 0,
                maximum: 60,
                scope: SettingScopes.Machine,
                category: SettingCategories.Phone,
                label: "Finished-message window (seconds)",
                description:
                    "The shorter wait a single message that reads as finished ('ok, go ahead.') serves instead of the " +
                    "aggregation window. A discount only while it is the shorter of the two — a value above the window " +
                    "is served as the window. Equal to the window means no discount: every message stays reachable by " +
                    "⏸ Wait for the whole window, which is the owner's classic choice (2026-09-23).",
                restart: RestartKinds.None),

            SettingDefinition_Factory.Create_Int(
                path: "phone.foldLongEntriesAbove",
                shippedDefault: OwnerMessage_Folder.DEFAULT_FOLD_THRESHOLD,

                // 0, NOT 1: 0 is the owner's OFF SWITCH (OwnerMessage_Folder — the delivery that predates
                // the fold), pinned by TelegramProseSettingsJsonTests and LongEntriesFoldOnThePhoneTests.
                // A floor of 1 was harmless while nothing resolved this row; the day
                // TelegramProseSettings_Json began resolving it (2026-09-14) it would have turned a
                // hand-edited 0 into 900.
                minimum: 0,
                maximum: 10000,
                scope: SettingScopes.Machine,
                category: SettingCategories.Phone,
                label: "Fold long entries above (characters)",
                description:
                    "Characters above which an entry is folded on the phone rather than sent whole; 0 turns folding off. " +
                    "Re-homed from 'telegram.foldLongEntriesAbove', which still resolves as an alias.",
                restart: RestartKinds.None,
                legacyPath: $"{TelegramProseSettings.TelegramProseSettings_Json.TELEGRAM_KEY}.{TelegramProseSettings.TelegramProseSettings_Json.FOLD_LONG_ENTRIES_ABOVE_KEY}"),

            SettingDefinition_Factory.Create_Int(
                path: "phone.attachEntriesAbove",
                shippedDefault: OwnerDocument_Builder.DEFAULT_ATTACH_ABOVE_CHUNKS,

                // 0, NOT 1, for the reason phone.foldLongEntriesAbove gives: 0 is the attachment's off switch.
                minimum: 0,
                maximum: 100,
                scope: SettingScopes.Machine,
                category: SettingCategories.Phone,
                label: "Attach entries above (chunks)",
                description:
                    "How many message-sized chunks an entry may span before it is sent as an attached document instead; " +
                    "0 turns the attachment off. Re-homed from 'telegram.attachEntriesAbove', which still resolves as an alias.",
                restart: RestartKinds.None,
                legacyPath: $"{TelegramProseSettings.TelegramProseSettings_Json.TELEGRAM_KEY}.{TelegramProseSettings.TelegramProseSettings_Json.ATTACH_ENTRIES_ABOVE_KEY}"),

            // DELETE IS THE SHIPPED DEFAULT — the owner's answer D2 (2026-09-14): "delete", with close-but-keep
            // "staying available as an option" (ruling R4 implements it). Classic states nothing and follows;
            // quiet no longer restates it. Read at the point of effect by the engine's Execute_Close.
            SettingDefinition_Factory.Create_Enum(
                path: "topic.onClose",
                values: ["delete", "close"],
                shippedDefault: "delete",
                scope: SettingScopes.Machine,
                category: SettingCategories.Phone,
                label: "On closing an orchestration",
                description:
                    "Whether closing an orchestration deletes its Telegram topic or closes it. 'delete' (the default) " +
                    "removes the topic and its messages — for a phone that will hold thousands of finished topics — and " +
                    "keeps trying: a delete that fails is retried, remembered, and paid off at the next start. 'close' " +
                    "keeps the topic in the list with its history, marked 🏁, as an audit trail on the phone; it is one " +
                    "closeForumTopic, not retried, because a failed close only leaves the topic open. The folder on disk " +
                    "is kept either way.",
                restart: RestartKinds.None),

            SettingDefinition_Factory.Create_Enum(
                path: "topic.modeGlyphs",
                values: ["name", "pulseHeader"],
                shippedDefault: "pulseHeader",
                scope: SettingScopes.Machine,
                category: SettingCategories.Phone,
                label: "Where mode glyphs are drawn",
                description:
                    "Whether the delivery-mode glyphs (🌙 🔕 ✈ 🤐 💻) live on the topic NAME or in PULSE's header. " +
                    "THE FORK'S REASON FOR MOVING THEM OFF THE NAME: each rename is an editForumTopic call and EACH " +
                    "RENAME WRITES A SERVICE MESSAGE, so an app-wide mode change wrote a line into every one of the " +
                    "owner's threads to tell them something they had just done themselves. In PULSE's header the same " +
                    "fact costs one silent edit of a message that was being edited anyway (spec §7.4). Under 'name' that " +
                    "cost is back: an app-wide mode change (/dnd_all, /mute_all, away) renames every open topic in one " +
                    "sweep. A name is only renamed when what it should say has changed, never on a tick. The state " +
                    "glyphs (🏁 💤 ✅ 🧪 ⏸) stay on the name either way, and the five are never drawn in both places.",
                restart: RestartKinds.None),

            // THE PER-REPO TOPIC COLOUR (plan 03 task 14; owner 2026-09-23 08:10: "since we merged his forks
            // the topic icon gets colored without any context of why, red, blue, green, seemingly random").
            // The shipped value is today's — the fork's brief F1 (dfb33688, 2026-09-10) — and classic states
            // the owner's false (ruling R14). Read at the point of effect by RepoTopicColour_Resolver.
            SettingDefinition_Factory.Create_Bool(
                path: PhoneSettings.PhoneSettings_Json.TOPIC_REPO_COLOURS_PATH,
                shippedDefault: true,
                scope: SettingScopes.Machine,
                category: SettingCategories.Phone,
                label: "Colour topics by repository",
                description:
                    "Whether each repository's topics are created with a colour of their own: the first repository " +
                    "takes blue, the next yellow, then violet, green, rose and red, and the colour is written onto the " +
                    "repository in config.json so reordering the list never changes it. Off creates every topic in " +
                    "Telegram's default. TELEGRAM ONLY TAKES A COLOUR WHEN A TOPIC IS CREATED (editForumTopic has no " +
                    "icon_color), so a change reaches topics created afterwards — existing topics keep the colour they " +
                    "have. Turning it off erases no assignment: on again, each repository gets its old colour back.",
                restart: RestartKinds.None),
        ];
    }

    // ---------------------------------------------------------------------------------------
    // RECEIPTS — how the app acknowledges the owner.
    // ---------------------------------------------------------------------------------------

    static IReadOnlyList<ISettingDefinition> Build_Receipts()
    {
        return
        [
            SettingDefinition_Factory.Create_Enum(
                path: "phone.receipts",
                values: ["ticks", "reactions"],
                shippedDefault: "ticks",
                scope: SettingScopes.Machine,
                category: SettingCategories.Receipts,
                label: "Receipt style",
                description:
                    "How the app says it has your message: 'ticks' posts a silent ✓ under it and edits that line to ✓✓ " +
                    "when a session is handed it; 'reactions' puts 👀 then 👌 on the owner's own message instead, and no " +
                    "reaction is attempted under 'ticks'. A reaction Telegram refuses still gets the ✓, whichever is set.",
                restart: RestartKinds.None),

            SettingDefinition_Factory.Create_Bool(
                path: "pulse.holdToggle",
                shippedDefault: true,
                scope: SettingScopes.Machine,
                category: SettingCategories.Receipts,
                label: "Hold toggle on the pulse bar",
                description:
                    "WHERE the ⏸/▶ hold toggle is drawn: true puts it on the PULSE bar, false on the receipt. NEVER BOTH — " +
                    "one toggle in two places is CLAUDE.md decision 12's drift, and this key is the single fact that says " +
                    "which place. False with phone.receipts = 'reactions' has no receipt message to carry it, so the toggle " +
                    "falls back to the PULSE bar and the log says so once.",
                restart: RestartKinds.None),
        ];
    }

    // ---------------------------------------------------------------------------------------
    // PULSE — the status line and the bars hanging off it.
    // ---------------------------------------------------------------------------------------

    static IReadOnlyList<ISettingDefinition> Build_Pulse()
    {
        return
        [
            SettingDefinition_Factory.Create_StringList(
                path: "pulse.fields",

                // THE SEVEN OF SPEC §6.4, and `modelEffort` is deliberately legal but not shipped: it
                // is a field an owner may add, not one every owner wants on every line.
                shippedDefault:
                [
                    PulseField_Names.WAITING_ON_YOU,
                    PulseField_Names.SUPERVISOR,
                    PulseField_Names.MEMBERS,
                    PulseField_Names.CLOSED_COUNT,
                    PulseField_Names.LAST_EVENT,
                    PulseField_Names.MERGED,
                    PulseField_Names.UPDATED,
                ],
                scope: SettingScopes.Machine,
                category: SettingCategories.Pulse,
                label: "Pulse fields",
                description:
                    "Which fields the pulse line carries, in this order, under its header. 'modelEffort' is a legal field and " +
                    "is not shipped on the line — an owner who wants it adds it, and it rides the supervisor and member rows " +
                    "rather than drawing a line of its own. 'progress' is the task count alone, '1/12 (8%)' — the reading " +
                    "'merged' carries without its label, and with the 'unchanged for' clause when pulse.unchangedFor is on " +
                    "('1/12 (8%) · unchanged 25 min') — and it is the one field with a place " +
                    "outside this order: listed FIRST, it is drawn above the header, so it is the first line of the message and " +
                    "of a notification preview; listed anywhere else, it sits in its place like any other field. " +
                    "Omitting 'updated' removes the heartbeat, which is what tells the " +
                    "owner a quiet orchestration from a dead app — a frozen status line looks exactly like a correct one.",
                restart: RestartKinds.None,
                validator: SettingValidators.PULSE_FIELDS),

            SettingDefinition_Factory.Create_Int(
                path: "pulse.stepMinutes",
                shippedDefault: UnchangedFor_Formatter.STEP_MINUTES,
                minimum: 1,
                maximum: 60,
                scope: SettingScopes.Machine,
                category: SettingCategories.Pulse,
                label: "Pulse step (minutes)",
                description:
                    "The granularity the pulse's clocks step in — each member's time on task, the 'unchanged for' reading " +
                    "and the 'updated' heartbeat, all three by the same step — and therefore how often the pulse message " +
                    "is EDITED at all. THE FLOOR IS NOT LOWER BY DEFAULT because of the 429 evidence of 2026-09-10: an " +
                    "edit per minute across every open topic is a rate-limit, and a status line that is rate-limited tells " +
                    "the owner nothing at all.",
                restart: RestartKinds.None),

            // THE "UNCHANGED FOR" CLAUSE (plan 03 task 19; owner 2026-09-24, ai-orchestrator-29 entry [100]: "my
            // brother removed the indication ... of how long the progress and completion percentage have stayed
            // identical in minutes. It's useful to get an idea if the session is working or not."). Shipped ON,
            // today's behaviour for the shipped list, where `merged` carries it (ruling R14); quiet states false
            // (ruling R26). The wording and the ten-minute silence are UnchangedFor_Formatter's, one copy.
            SettingDefinition_Factory.Create_Bool(
                path: PulseSettings.PulseSettings_Json.UNCHANGED_FOR_PATH,
                shippedDefault: true,
                scope: SettingScopes.Machine,
                category: SettingCategories.Pulse,
                label: "Say how long progress has stood still",
                description:
                    "Whether the pulse's progress reading says how long the task count and completion percentage have " +
                    "stayed identical — 'unchanged 25 min', shown only once they have stood still for 10 minutes and " +
                    "stepped by pulse.stepMinutes. A useful hint of whether the session is working. It rides whichever " +
                    "of the 'progress' and 'merged' fields is drawn; off, neither says it.",
                restart: RestartKinds.None),

            SettingDefinition_Factory.Create_StringList(
                path: "pulse.buttons",
                shippedDefault: TopicCommandButtons.Commands,
                scope: SettingScopes.Machine,
                category: SettingCategories.Pulse,
                label: "Orchestration topic buttons",
                description:
                    "The verbs on an orchestration topic's button bar, in display order. Any command of the '/' menu can be a " +
                    "button, and a tap runs it exactly as if you had typed it in that topic. An element carrying a target other " +
                    "than 'tail sup' (e.g. 'tail 1') is left off the bar and named once in the log — it stays a typed command.",
                restart: RestartKinds.None,
                validator: SettingValidators.BOT_COMMANDS),

            SettingDefinition_Factory.Create_StringList(
                path: "general.buttons",
                shippedDefault: TopicCommandButtons.GeneralCommands,
                scope: SettingScopes.Machine,
                category: SettingCategories.Pulse,
                label: "General topic buttons",
                description:
                    "The verbs on the GENERAL topic's button bar, in display order. All cross-cutting on purpose: General " +
                    "has no session of its own, so a /merge or a /close there would have nothing to act on. An empty list " +
                    "is no bar at all.",
                restart: RestartKinds.None,
                validator: SettingValidators.BOT_COMMANDS),
        ];
    }

    // ---------------------------------------------------------------------------------------
    // OWNER — who the app is talking to.
    // ---------------------------------------------------------------------------------------

    static IReadOnlyList<ISettingDefinition> Build_Owner()
    {
        return
        [
            SettingDefinition_Factory.Create_String(
                path: "owner.name",
                shippedDefault: "",
                scope: SettingScopes.Machine,
                category: SettingCategories.Owner,
                label: "Owner name",
                description:
                    "What the sessions call the owner. Empty means they do not use a name at all, which is the shipped " +
                    "behaviour. Nothing reads this key until plan 05 exports it to a session's environment.",
                restart: RestartKinds.NextSpawn),

            SettingDefinition_Factory.Create_Enum(
                path: "owner.language",
                values: ["auto", "en", "it"],
                shippedDefault: "auto",
                scope: SettingScopes.Machine,
                category: SettingCategories.Owner,
                label: "Owner language",
                description:
                    "THE APP-SIDE TRANSLATION LAYER IS GONE (spec §7.8, owner 2026-09-12) and this key replaces it. " +
                    "'auto' is the fork's rule, and this description is the only machine-readable home that rule has: " +
                    "WRITE TO THE OWNER IN THE LANGUAGE THEY USED; EVERYTHING ON DISK OR ADDRESSED TO ANOTHER AGENT " +
                    "STAYS ENGLISH — channel entries, commit messages, PLAN.md, code and comments included. 'en' and " +
                    "'it' pin the owner-facing half instead of following them. Nothing reads this key until plan 05 " +
                    "exports it as AIORCH_OWNER_LANGUAGE.",
                restart: RestartKinds.NextSpawn),
        ];
    }
}
