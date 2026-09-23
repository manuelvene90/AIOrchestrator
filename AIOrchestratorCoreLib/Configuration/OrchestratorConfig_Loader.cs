using System.Text.Json;
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.DefaultsSettings;
using AIOrchestratorCoreLib.Configuration.EffortSettings;
using AIOrchestratorCoreLib.Configuration.EndeavourSettings;
using AIOrchestratorCoreLib.Configuration.GuardrailSettings;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.Configuration.PhoneSettings;
using AIOrchestratorCoreLib.Configuration.PulseSettings;
using AIOrchestratorCoreLib.Configuration.RepoEntry;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Configuration.SettingsWriting;
using AIOrchestratorCoreLib.Configuration.TelegramProseSettings;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.RunnerConfigs;
using AIOrchestratorCoreLib.Storage;
using AIOrchestratorCoreLib.SupervisionPaths;
using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Configuration;

/// <summary>
/// Loads and saves the orchestrator configuration. Non-secret settings live in config.json;
/// the bot token lives in secrets.json so the config file can be shared/backed up freely.
/// </summary>
public static class OrchestratorConfig_Loader
{
    /// <summary>
    /// Orch id used for the three app-global entries this loader can produce: a mistyped preset (<see cref="Resolve_Preset_OrClassic"/>),
    /// an unreadable config file handed to the tolerant read (<see cref="Read_JsonObject_ForEditing"/>), and a write refused by
    /// <see cref="Read_TreeForEditing"/>.
    /// </summary>
    const string GLOBAL_ORCH_ID = "";

    public static IOrchestratorConfig Load_OrEmpty(ISupervisionPaths paths)
    {
        return Load_OrEmpty(paths, log: null);
    }

    /// <summary>
    /// Reads config.json and secrets.json, or the shipped defaults when either is missing. Passing
    /// <paramref name="log"/> lets a mistyped <c>preset</c> word or path be reported instead of merely
    /// swallowed — see <see cref="Resolve_Preset_OrClassic"/>.
    ///
    /// <para>
    /// AN ABSENT config.json AND AN EMPTY ONE RESOLVE IDENTICALLY (ruling 2026-09-12, task-6 fix
    /// round 2). This method used to return <see cref="OrchestratorConfig_Factory.Create_Empty"/>
    /// early when both files were missing, bypassing the preset rung below entirely — so a machine
    /// with no config.json at all got no preset, while one containing <c>{}</c> got classic. Nothing
    /// in either shipped preset states a model today, so the difference was invisible; but <c>classic</c>
    /// carries the owner's effort preference (<c>effort.supervisor</c>/<c>effort.solo</c>), and a
    /// later task wires effort through this same rung. "Absent" and "empty" are the same statement —
    /// the owner has said nothing — and must resolve the same way, so the early return is gone: every
    /// downstream reader below (<see cref="Parse_Repos"/>, <see cref="RunnerConfigs_Json.Parse"/>,
    /// <see cref="Parse_Guardrails"/>, <see cref="DefaultsSettings_Json.Parse"/>,
    /// <see cref="TelegramProseSettings_Json.Parse"/>, every <c>Get_*_OrNull</c> helper here) already
    /// tolerates a null <c>configRoot</c> and answers with the exact same shipped default
    /// <c>Create_Empty</c> supplied, so this is a pure simplification, not a behaviour change, for
    /// every setting except the preset-stated ones.
    /// </para>
    /// </summary>
    public static IOrchestratorConfig Load_OrEmpty(ISupervisionPaths paths, IOrchestrationLog? log)
    {
        var configRoot = Read_JsonObject_OrNull(paths.ConfigFile);
        var secretsRoot = Read_JsonObject_OrNull(paths.SecretsFile);

        var repos = Parse_Repos(configRoot);

        // THE PRESET RUNG, between the shipped default and this file (spec §6.2). It is applied HERE and
        // only to the six model keys, because the factory's ladder (reviewer/solo → implementer → shipped)
        // must see "the owner said nothing" as null — a preset value read one layer lower would arrive as
        // a stated value and shorten the ladder. Nothing is written back: Save() merges, so a preset value
        // stays a preset value and the renderers can still show its origin (spec §6.2, the reviewerModel
        // rule generalised). Resolved through the safety net below rather than
        // Presets_Loader.Resolve_ForConfig directly — this path cannot fail.
        var (preset, _) = Resolve_Preset_OrClassic(configRoot, log);

        return OrchestratorConfig_Factory.Create(
            repos,
            Read_Model_OrNull(configRoot, preset, SessionRoles.Supervisor),
            Read_Model_OrNull(configRoot, preset, SessionRoles.Implementer),

            // ABSENT MEANS "WHAT THE ROLE GOT UNTIL NOW", and the factory is where that ladder lives
            // (reviewer/solo → implementer → the shipped default). Passed as the raw key, null and
            // all, precisely so the factory can tell "the owner never said" from "the owner said
            // this" — reading them here with a fallback would hide the first case from the only
            // place that can act on it.
            Read_Model_OrNull(configRoot, preset, SessionRoles.Reviewer),
            Read_Model_OrNull(configRoot, preset, SessionRoles.Solo),
            Read_Model_OrNull(configRoot, preset, SessionRoles.General),
            Read_Model_OrNull(configRoot, preset, SessionRoles.Communicator),
            Get_Long_OrNull(configRoot, "telegramSupergroupChatId"),
            Get_Long_OrNull(configRoot, "telegramOwnerUserId"),
            Get_String_OrNull(secretsRoot, "telegramBotToken"),
            Get_Bool_OrNull(configRoot, "telegramStatusScreenshots"),
            Get_String_OrNull(configRoot, "voiceTranscribeCommand"),
            Get_Long_OrNull(configRoot, "orchestrationTokenBudget"),
            RunnerConfigs_Json.Parse(configRoot),
            Parse_PlanBackend_OrNull(configRoot),
            Parse_Guardrails(configRoot, preset),
            DefaultsSettings_Json.Parse(configRoot),

            // ON THE PRESET RUNG SINCE 2026-09-14 (plan 03 task 1): the block now resolves its two keys
            // through the catalogue, which is what makes the re-homed `phone.*` spelling work beside
            // the old `telegram.*` one. Neither shipped preset states either key today; the tree is
            // passed so a preset that does is honoured rather than silently skipped.
            TelegramProseSettings_Json.Parse(configRoot, preset),

            // `telegramInbound`: "poll" (default) or "off". Hand-edited, never written back by Save
            // below — the same contract as the four blocks above it.
            Telegram.TelegramInbound_Modes.Parse_OrPoll(Get_String_OrNull(configRoot, "telegramInbound")),

            // THE `effort` BLOCK RIDES THE SAME PRESET RUNG THE MODELS DO (2026-09-12, plan 02 task 7),
            // and it is passed the same `preset` tree for the same reason: `classic` is where the
            // owner's xhigh for supervisor and solo lives now, so a machine that states nothing must
            // still resolve through it. Unlike the models it needs no "absence" dance — there is no
            // compat ladder between effort roles, so the resolved answer IS the answer, null included.
            EffortSettings_Json.Parse(configRoot, preset),

            // THE `phone`/`topic` AND `pulse` BLOCKS, on the SAME `preset` tree (2026-09-14, plan 03
            // task 1). These rows are exactly where the two presets disagree — Manu's phone and
            // Nathan's — so a block parsed without the tree would give every machine the catalogue's
            // answer and neither owner's. No absence dance here either: the resolved value IS the value.
            PhoneSettings_Json.Parse(configRoot, preset),
            PulseSettings_Json.Parse(configRoot, preset),

            // THE `endeavour` BLOCK (2026-09-23, sibling solos, O2), on the same preset rung. Neither
            // shipped preset states it — the catalogue's 3 is the answer — but a preset that does is
            // honoured rather than silently skipped. Never written back by Save below.
            EndeavourSettings_Json.Parse(configRoot, preset));
    }

    /// <summary>
    /// <c>Presets_Loader.Resolve_ForConfig</c> throws for an unknown preset word and for a preset
    /// file that cannot be read or does not parse — correct for a caller that asked for a named
    /// preset explicitly (ruled 2026-09-12, task-6 fix round 2 — the class doc's own reasoning still
    /// applies there). It is wrong for THIS path: config LOADING cannot fail, because
    /// <see cref="Load_OrEmpty(ISupervisionPaths)"/> runs on the app's startup path and
    /// <c>IOrchestratorConfigProvider.Get_Current()</c> calls it again on every tick with no
    /// try/catch above it. A single transposed letter in a hand-edited <c>"preset": "quite"</c>
    /// would otherwise take the whole app's config loading down — the same failure class
    /// <c>AMistypedModelValue_DoesNotTakeDownTheProviderOnTheStartupPath</c> exists to forbid for a
    /// mistyped model, reached through a different key. So a typo here costs exactly what an ABSENT
    /// <c>preset</c> key already costs — classic — never the load itself. Never silent, and never
    /// the owner's phone: one warning line names the bad word or path (the exception message already
    /// does), the way <see cref="AIOrchestratorCoreLib.Bridge.BridgeState_Store.Load_OrEmpty(ISupervisionPaths, IOrchestrationLog?)"/>
    /// reports a predicate this codebase could not honour — never Telegram, an alert the owner cannot
    /// act on does not belong there (CLAUDE.md decision 15).
    ///
    /// <para>
    /// INTERNAL, AND IT HANDS BACK THE NAME TOO (plan 04 Task 1, ruling P16, 2026-09-23). The settings
    /// renderers label every origin "from preset &lt;name&gt;", so their reader needs the name this rung
    /// actually resolved to — which is <see cref="Presets_Loader.CLASSIC"/> after a fallback, not the word
    /// the owner typed. It calls THIS method rather than carrying its own try/catch: a second copy of "a
    /// mistyped preset word means classic" would be one rule with two descriptions (CLAUDE.md decision 12),
    /// and the day one of them changed the phone would label an origin the loader never used.
    /// </para>
    /// </summary>
    internal static (JsonObject Tree, string Name) Resolve_Preset_OrClassic(JsonObject? configRoot, IOrchestrationLog? log)
    {
        try
        {
            return Presets_Loader.Resolve_ForConfig(configRoot);
        }
        catch (Exception ex)
        {
            log?.Log_Warning(GLOBAL_ORCH_ID, $"config.json's '{Presets_Loader.PRESET_KEY}' could not be resolved ({ex.Message}) — the classic preset was used instead.");
            return (Presets_Loader.Load_Embedded(Presets_Loader.CLASSIC), Presets_Loader.CLASSIC);
        }
    }

    /// <summary>
    /// The model this file or the preset states for a role, or null when neither does — which is what
    /// the factory's ladder needs to hear. Blank is null for the reason
    /// <see cref="OrchestratorConfig_Factory"/> gives: a cleared field is the owner saying nothing, and
    /// an empty string reaching a spawn emits no --model flag at all (proven 2026-09-10).
    ///
    /// <para>
    /// REVIEWER AND SOLO NEVER ACCEPT A PRESET ANSWER FOR THEMSELVES. The compat ladder in
    /// <see cref="OrchestratorConfig_Factory"/> says an absent reviewer or solo model falls to the
    /// IMPLEMENTER's, before any default. No shipped preset states <c>models.reviewer</c> or
    /// <c>models.solo</c> as of the 2026-09-12 ruling (task-6 fix round 1) that removed all four model
    /// rows from <c>classic</c> — but this rule is KEPT rather than deleted (re-confirmed fix round 2),
    /// because it now defends a HAND-EDITED preset file specifically (<c>Presets_Loader.Load_FromDisk</c>),
    /// which is legal input an owner can still write and could name either key. If one did, it would
    /// otherwise pre-empt the ladder for a config.json that only ever set <c>implementerModel</c>
    /// (proven by <c>AFileWrittenBeforeTheseKeysExisted_KeepsGivingTheReviewerAndSoloTheImplementerModel</c>
    /// and three siblings, back when <c>classic</c> still carried these two rows itself and exercised
    /// the same code path). So a Preset-origin answer for these two roles reads as absence here too,
    /// exactly like the shipped default — the factory's ladder, fed the implementer's own
    /// stated-or-preset answer as its second rung, is what actually answers for them.
    /// </para>
    /// <para>
    /// A SECOND REFINEMENT USED TO LIVE HERE TOO (withholding the preset tree entirely whenever the
    /// config key was present, valid or not) and is GONE as of fix round 2: it existed only because
    /// <c>classic</c> used to restate a model for every judging role, so a present-but-invalid config
    /// value would otherwise have picked up the preset's answer instead of the catalogue's. With no
    /// shipped preset stating any model any more, that condition cannot occur — verified by removing
    /// the refinement and confirming no test in <c>PerRoleModelDefaultsTests</c> turned red. It also
    /// duplicated a presence check <see cref="Settings_Resolver"/> already makes internally; deleting
    /// it removes that second copy rather than re-homing it, since nothing calls it any more.
    /// </para>
    /// </summary>
    static string? Read_Model_OrNull(JsonObject? configRoot, JsonObject? presetTree, SessionRoles role)
    {
        var definition = Catalog.Find_OrNull(Catalog.Get_ModelPath(role))!;
        var (value, origin) = Settings_Resolver.Resolve(definition, presetTree, configRoot, session: null);

        if (origin == SettingOrigins.ShippedDefault)
            return null;

        if (origin == SettingOrigins.Preset && (role == SessionRoles.Reviewer || role == SessionRoles.Solo))
            return null;

        return value?.GetValue<string>();
    }

    /// <summary>
    /// A MISSING key and an EMPTY value are deliberately different here: no "highRiskPatterns" key
    /// means the owner never said, and gets the default list; an explicitly empty array means they
    /// said "nothing is high risk", which is theirs to say. Collapsing the two would make the guard
    /// impossible to turn off, or impossible to keep.
    ///
    /// <para>
    /// <c>highRiskConfirmation</c> IS THE ONE KEY HERE ON THE PRESET RUNG (plan 03 task 15), because it
    /// is the one a preset states: classic turns the code off by the owner's request of 2026-09-23. The
    /// four older keys are still read from this file alone — no preset names them, and moving them is
    /// not this change. Resolved through <see cref="Settings_Resolver"/> like the phone rows, so a
    /// non-boolean costs the key its preset value, never the load.
    /// </para>
    /// </summary>
    static IGuardrailSettings Parse_Guardrails(JsonObject? configRoot, JsonObject? presetTree)
    {
        var confirmationDefinition = Catalog.Find_OrNull(GuardrailSettings_Factory.HIGH_RISK_CONFIRMATION_KEY)
            ?? throw new Exception($"No catalogue entry for {GuardrailSettings_Factory.HIGH_RISK_CONFIRMATION_KEY} — the loader names a row the settings catalogue does not register");

        return GuardrailSettings_Factory.Create(
            Get_StringList_OrNull(configRoot, GUARDRAIL_HIGH_RISK_PATTERNS),
            Get_Int_OrNull(configRoot, GUARDRAIL_HIGH_RISK_CODE_EXPIRY_MINUTES),
            Get_Double_OrNull(configRoot, GUARDRAIL_DISPATCH_PAUSE_THRESHOLD_PERCENT),
            Get_Int_OrNull(configRoot, GUARDRAIL_BUTTON_EXPIRY_MINUTES),
            Settings_Resolver.Resolve_Bool(confirmationDefinition, presetTree, configRoot, session: null));
    }

    /// <summary>
    /// Saves the settings this app OWNS, onto whatever the file already contained.
    ///
    /// <para>
    /// IT MERGES RATHER THAN REPLACES, and that is a fix rather than a refinement: this method used to
    /// build a fresh object and write it, so every key it did not know about was deleted the first
    /// time anything saved — the Settings window, the repo list, the /screenshots toggle. A hand-edited
    /// key (planBackend is the first, and will not be the last) survived exactly until the owner next
    /// pressed a button. Unknown keys are now carried through untouched. Agents edit config.json at
    /// runtime, which is exactly why <see cref="ConfigRepos_Reorderer"/> was already written to
    /// operate on the raw tree.
    /// </para>
    /// <para>
    /// TWO BRANCHES FOUND THIS INDEPENDENTLY, which is worth recording: `stage/5-plan-backend` and
    /// `stage/3-durable-bridge` each hit it and each fixed it, by different mechanisms. This is the
    /// stage-3 one, kept because it is strictly the more complete of the two — the other read the
    /// existing file with a bare `Read_JsonObject_OrNull(...) ?? []`, which THROWS on a config.json
    /// that will not parse, and wrote with a plain `File.WriteAllText`.
    /// </para>
    /// <para>
    /// ATOMIC, for the reason <see cref="Atomic_FileWriter"/> exists: a truncate-then-write that is
    /// interrupted leaves a zero-length config.json, and a zero-length config.json is an app with no
    /// repos, no chat id and no owner id — Telegram-blind, with the real settings gone rather than
    /// merely unsaved.
    /// </para>
    /// <para>
    /// UNDER <see cref="Settings_Writer.CONFIG_WRITE_LOCK"/> (plan 04 D10, 2026-09-23), because config.json
    /// now has SEVEN writers in this process and a read-edit-write that interleaves with another loses one
    /// of the two edits to the second rename: this method (the Settings window, and the /screenshots toggle
    /// through <c>BridgeEngineModel.Set_StatusScreenshots</c>), <see cref="Save_BotToken"/> (secrets.json
    /// only), <see cref="Settings_Writer"/>'s <c>Apply</c>, <c>Apply_Many</c> and <c>Reset</c> — all five
    /// take the lock — and <see cref="ConfigRepos_Reorderer"/> and <see cref="ConfigRepoColor_Writer"/>,
    /// which do NOT (PARKED: both operate on the raw tree already, and neither was asked of this plan).
    /// THE LOCK IS IN-PROCESS ONLY: the WPF app and the daemon are two processes, an agent hand-editing the
    /// file is a third, and a lock in one restrains nothing in the others.
    /// </para>
    /// <para>
    /// A FILE IT COULD NOT OPEN IS NEVER REWRITTEN (plan 04 Task 2c, ruling P35, 2026-09-23). This is master's
    /// code, older than plan 04, and it read both files through the lenient <see cref="Read_JsonObject_ForEditing"/>
    /// — a plain <c>File.ReadAllText</c> underneath, not even the retrying reader — so a sharing violation (the
    /// daemon's own save, an editor, an antivirus scan) read as EMPTY and config.json was written holding only the
    /// keys above: <c>planBackend</c>, the <c>effort</c>/<c>phone</c>/<c>pulse</c>/<c>web</c> blocks, the model keys
    /// and the guardrails gone on one press of the window's Save or one <c>/screens</c> from the phone. The reason
    /// that read gave for its leniency — "a file that will not parse has no unknown keys worth preserving" — is
    /// about a file that will not PARSE, never one that could not be OPENED. So both reads now go through
    /// <see cref="Read_TreeForEditing"/>: ABSENT starts empty as before; UNREADABLE throws an <see cref="IOException"/>
    /// naming the file and nothing is written; a CORRUPT config.json keeps the documented replacement (the window's
    /// Save is the owner's way back in from a config the loader cannot read). Two exceptions to that replacement,
    /// ruled P36 and both refused like UNREADABLE: a config.json that parses but states a key TWICE (it threw with
    /// nothing written before Task 2c, and keeps that outcome — its other keys are intact), and ANY unparsable
    /// secrets.json (it holds only what a human put there, so a rewrite is never the smaller loss). BOTH FILES ARE READ BEFORE EITHER IS WRITTEN: the
    /// secrets read used to follow the config write, so a held secrets.json was a half save — config.json rewritten,
    /// the token not — behind an exception that said the save had failed. The contract for a caller does not change:
    /// this method is <c>void</c> and a failed write already threw.
    /// </para>
    /// </summary>
    public static void Save(IOrchestratorConfig config, ISupervisionPaths paths)
    {
        lock (Settings_Writer.CONFIG_WRITE_LOCK)
            Save_UnderWriteLock(config, paths);
    }

    static void Save_UnderWriteLock(IOrchestratorConfig config, ISupervisionPaths paths)
    {
        Directory.CreateDirectory(paths.Root);

        var reposArray = new JsonArray();
        foreach (var repo in config.Repos)
        {
            var repoObject = new JsonObject
            {
                ["name"] = repo.Name,
                ["path"] = repo.Path,
            };

            // WRITTEN ONLY WHEN IT EXISTS, so a config.json belonging to an owner who has never
            // started a topic stays exactly as clean as it was (brief F1).
            if (repo.TopicColor != null)
                repoObject["topicColor"] = repo.TopicColor.Value;

            reposArray.Add(repoObject);
        }

        var configRoot = Read_TreeForEditing_OrThrow(paths.ConfigFile, corruptReadsAsEmpty: true);
        var secretsRoot = Read_TreeForEditing_OrThrow(paths.SecretsFile, corruptReadsAsEmpty: false);

        configRoot["repos"] = reposArray;
        configRoot["telegramSupergroupChatId"] = config.TelegramSupergroupChatId;
        configRoot["telegramOwnerUserId"] = config.TelegramOwnerUserId;
        configRoot["telegramStatusScreenshots"] = config.TelegramStatusScreenshots;
        configRoot["voiceTranscribeCommand"] = config.VoiceTranscribeCommand;
        configRoot["orchestrationTokenBudget"] = config.OrchestrationTokenBudget;

        RunnerConfigs_Json.Write(configRoot, config.Runners);

        Atomic_FileWriter.Write_AllText(paths.ConfigFile, configRoot.ToJsonString(JsonWriting.INDENTED));

        // NO MODEL KEY IS WRITTEN HERE — ALL SIX ARE READ AND NEVER WRITTEN (plan 04 D1, owner,
        // 2026-09-14; until then only reviewerModel and soloModel were). A model's default is one that
        // is MEANT TO MOVE: an absent reviewerModel tracks implementerModel by design (owner 2026-09-09:
        // implementer sonnet eventually, reviewer opus), a preset states models for a whole machine,
        // and the catalogue's shipped default changes when the app is updated. Writing whatever a model
        // had RESOLVED to would materialise it as if the owner had chosen it, on the first button press,
        // on every box — which is exactly how the owner's live config.json came to pin a stale model and
        // defeat the Opus default they had asked for (CLAUDE.md, entry 216): the four supervisor,
        // implementer, general and communicator lines here wrote a preset's value, and before plan 02 a
        // shipped default, back as the owner's own. A model is now written when the owner edits THAT
        // row, through Settings_Writer, and at no other time. A hand-edited value is safe either way:
        // Save() merges, so keys it does not write survive untouched.
        //
        // planBackend, THE GUARDRAIL KEYS, defaults, telegram, effort, phone/topic AND pulse ARE DELIBERATELY ABSENT from the writes above,
        // for the same reason from two directions. planBackend is hand-edited, no window builds one,
        // and IOrchestratorConfig.PlanBackend is null in every config the app constructs itself —
        // writing it would erase the owner's own key on the next save. The guardrail keys and the
        // defaults block have no UI and no command that changes them, so the only thing a save could
        // do is materialise this build's defaults into the file as if the owner had chosen them,
        // freezing a default that is meant to move when the app is updated. The telegram block —
        // foldLongEntriesAbove, attachEntriesAbove — belongs to that same set, and so does the
        // `effort` block (EffortSettings_Json.EFFORT_KEY): its shipped answer comes
        // from the catalogue and its working answer from a PRESET, so writing this build's resolution
        // back would pin the owner to whichever preset was in force the day they last pressed a
        // button. EffortSettings_Json has no Write method at all, so that cannot happen by accident.
        // The `phone`/`topic` and `pulse` blocks (2026-09-14) are the newest members, for the same
        // preset reason and more sharply — they are the rows the two presets disagree about — and
        // PhoneSettings_Json and PulseSettings_Json have no Write either. The `endeavour` block
        // (2026-09-23) joins them for the effort reason: EndeavourSettings_Json has no Write.
        // All eight are read; none is owned.

        secretsRoot["telegramBotToken"] = config.TelegramBotToken;

        Atomic_FileWriter.Write_AllText(paths.SecretsFile, secretsRoot.ToJsonString(JsonWriting.INDENTED));
    }

    /// <summary>
    /// THE BOT TOKEN'S OWN DOOR: secrets.json, and nothing else (plan 04 ruling P11). The window's Connection
    /// tab needs to save the token, and the full <see cref="Save"/> would also rewrite <c>runners.*</c>, <c>limits.*</c> and five
    /// Kernel scalars at whatever they RESOLVED to — turning their origin into "set here" and undoing a Reset
    /// the owner made a minute earlier from the phone (spec §6.2: a save never materialises a preset value).
    /// The token is deliberately absent from the catalogue (a <c>/settings</c> menu that printed it would post
    /// it into the chat it controls), so <see cref="Settings_Writer"/> cannot write it; this writes the token
    /// and nothing beside it. Every other key of secrets.json is carried through; a null token is written as
    /// JSON null, which reads back as no token — the same thing <see cref="Save"/> writes for an empty box.
    ///
    /// <para>
    /// IT NEVER REWRITES A secrets.json IT COULD NOT READ (plan 04 Task 2c, ruling P35). It read through the lenient
    /// <see cref="Read_JsonObject_ForEditing"/>, so a held or unparsable secrets.json became <c>{telegramBotToken}</c>
    /// alone. Both states now throw an <see cref="IOException"/> naming the file, and nothing is written:
    /// secrets.json holds only what a human put there, so a rewrite is never the smaller loss — the rule
    /// <see cref="Save"/> follows for this file too (P36).
    /// </para>
    /// <para>
    /// RETURNS THE TOKEN AS WRITTEN (trimmed, or null for none), so the window's box can show what secrets.json
    /// now holds rather than what was pasted — the tidy is this method's, and the box must not re-derive it.
    /// </para>
    /// </summary>
    public static string? Save_BotToken(ISupervisionPaths paths, string? token)
    {
        var written = Normalise_BotToken_OrNull(token);

        lock (Settings_Writer.CONFIG_WRITE_LOCK)
        {
            var secretsRoot = Read_TreeForEditing_OrThrow(paths.SecretsFile, corruptReadsAsEmpty: false);

            secretsRoot["telegramBotToken"] = written;

            Atomic_FileWriter.Write_AllText(paths.SecretsFile, secretsRoot.ToJsonString(JsonWriting.INDENTED));
        }

        return written;
    }

    /// <summary>
    /// A TOKEN AS A BOX HANDS IT OVER, TIDIED HERE AND NOT IN THE WINDOW (plan 04 Task 9): a pasted token carries a
    /// stray space more often than not, and a token with one is a token Telegram refuses at the next start with no
    /// sign of why. Blank is "no token" (JSON null), exactly what the window's old Save wrote for an empty box. The
    /// WPF project may hold no logic, so this rule lives with the one door that writes the token.
    /// </summary>
    static string? Normalise_BotToken_OrNull(string? token)
    {
        return string.IsNullOrWhiteSpace(token) ? null : token.Trim();
    }

    /// <summary>
    /// The tree a screen DRAWS: the file's own object when it can be read, an empty one when it cannot —
    /// every failure is "empty". For READ-ONLY callers only; a writer reads through
    /// <see cref="Read_TreeForEditing"/>.
    ///
    /// <para>
    /// THE ONE TOLERANT READ OF config.json, AND INTERNAL SINCE 2026-09-23 (plan 04 Task 1, ruling P16).
    /// The settings renderers' reader (<c>SettingsPresentation.SettingsSnapshot_Reader</c>) reads through
    /// it: an HTTP GET and a Telegram menu must draw over a hand-edit with a trailing comma, and several
    /// readers each deciding what "unreadable" means is decision 12's drift.
    /// <see cref="Load_OrEmpty(ISupervisionPaths, IOrchestrationLog?)"/> still reads through
    /// <see cref="Read_JsonObject_OrNull"/> directly and is NOT made tolerant here — that would change what
    /// the app's startup path does, which no task has asked to change.
    /// </para>
    /// <para>
    /// NO WRITER READS THROUGH IT ANY MORE. <c>Settings_Writer</c> left it in plan 04 Task 2b (ruling P33), and
    /// <see cref="Save"/> and <see cref="Save_BotToken"/> in Task 2c (ruling P35): "every failure is empty" made
    /// a sharing violation into an empty tree, and each of them then replaced the file with its own keys
    /// alone. A reader that draws may fall back to empty; a writer that would replace the file may not.
    /// </para>
    /// <para>
    /// SWALLOWED, NEVER SILENT, when a caller hands in <paramref name="log"/>: one warning line names the
    /// file and the parser's own reason, because every setting on screen is about to read as its preset or
    /// shipped default and the owner cannot see why otherwise (decision 21's corollary). Not Telegram —
    /// decision 15.
    /// </para>
    /// </summary>
    internal static JsonObject Read_JsonObject_ForEditing(string filePath, IOrchestrationLog? log = null)
    {
        try
        {
            return Read_JsonObject_OrNull(filePath) ?? [];
        }
        catch (Exception ex)
        {
            // Broad by intent: malformed, truncated, or unreadable are one situation here.
            log?.Log_Warning(GLOBAL_ORCH_ID, Describe_UnreadableFile(filePath, ex.Message));
            return [];
        }
    }

    /// <summary>The warning line for a config file that could not be read — named once, for every caller of the tolerant read.</summary>
    static string Describe_UnreadableFile(string filePath, string reason)
    {
        return $"'{filePath}' could not be read ({reason}) — it was treated as empty, so every setting falls to its preset or "
            + "shipped default. Nothing writes over it while it cannot be opened, or while a key in it is stated twice; if it "
            + "opens but does not parse, a settings edit is still refused, and the Settings window's Save and the /screens "
            + "toggle replace it.";
    }

    /// <summary>Names no caller: <c>Settings_Writer</c>, <see cref="Save"/> and <see cref="Save_BotToken"/> all refuse through it.</summary>
    const string WRITE_REFUSED_PREFIX = "A write was refused: ";

    /// <summary>A duplicated key does not parse for a writer — see <see cref="Parse_Tree"/>.</summary>
    static readonly JsonDocumentOptions EDIT_PARSE_OPTIONS = new() { AllowDuplicateProperties = false };

    /// <summary>
    /// THE ONE READ A WRITER EDITS — config.json for <c>Settings_Writer</c> and <see cref="Save"/>, secrets.json for
    /// <see cref="Save"/> and <see cref="Save_BotToken"/> — and it answers exactly one of two things: the tree to edit,
    /// or the reason the file refuses. Never an empty tree standing in for a file that is there. Moved here from
    /// <c>Settings_Writer</c> (where plan 04 Task 2b wrote it, ruling P33) by Task 2c (ruling P35), so that three
    /// writers share one classifier rather than each deciding what "unreadable" means (decision 12).
    /// <list type="number">
    /// <item><b>Absent</b> (no file, or no folder yet) → an empty tree: there is nothing on disk to lose, so a
    /// first write creates the file holding the written keys.</item>
    /// <item><b>Present, could not be read</b> → refused, always. Read through <see cref="Tolerant_FileReader"/>,
    /// which already outlasts the atomic writer's rename (<c>e72cc84</c>); a holder that outlasts IT — another
    /// process's save, an editor, an antivirus scan, a folder where the file should be — is still holding it, and
    /// an empty tree written now would replace the owner's file the moment the holder lets go.</item>
    /// <item><b>Present, read, CORRUPT</b> — not JSON, a top level that is not an object, or EMPTY (also what an
    /// editor that truncates before it writes looks like for a moment) → refused, unless the caller passes
    /// <paramref name="corruptReadsAsEmpty"/>. Only <see cref="Save"/> does, and only for config.json, keeping its
    /// documented replacement of a corrupt config (P35, narrowed by P36); that replacement is silent, as it always
    /// was, because Save has no log. secrets.json is never replaced this way by either of its writers (P36): it holds
    /// only what a human put there.</item>
    /// <item><b>Present, parses, a key stated twice</b> → refused, ALWAYS, whatever the caller passes (P36). It is
    /// two answers to one question — a rewrite would pick one for the owner — and every other key in the file is
    /// intact, so it is not the "corrupt, nothing left to preserve" case. Before Task 2c, Save's lenient read handed
    /// such a file back as a tree that threw <see cref="ArgumentException"/> on Save's first write, so nothing was
    /// written; that outcome is kept, as a refusal with a reason. Told apart by <see cref="Parse_Tree"/>, inside the
    /// one classifier, rather than by a second one.</item>
    /// </list>
    /// One warning line per refusal when <paramref name="log"/> is given (decision 15: the log, not Telegram; the
    /// caller answers the owner where they acted). The prefix names no caller, because three different doors use it.
    /// </summary>
    internal static (JsonObject? Tree_OrNull, string? Refusal_OrNull) Read_TreeForEditing(
        string filePath, IOrchestrationLog? log, bool corruptReadsAsEmpty)
    {
        string text;

        try
        {
            text = Tolerant_FileReader.Read_AllText(filePath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return ([], null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, Log_WriteRefusal(log, Describe_UnreadableForEditing(filePath, ex.Message)));
        }

        var (tree, problem, isDuplicateKey) = Parse_Tree(text);

        if (tree != null)
            return (tree, null);

        if (corruptReadsAsEmpty && !isDuplicateKey)
            return ([], null);

        return (null, Log_WriteRefusal(log, Describe_UnparsableForEditing(filePath, problem!)));
    }

    /// <summary>
    /// <see cref="Read_TreeForEditing"/> for the two void doors, which have no outcome to return: a refusal is an
    /// <see cref="IOException"/> carrying the refusal's own words. An IOException for BOTH states, the unparsable one
    /// included, so a caller's one catch for a failed save — <see cref="Atomic_FileWriter"/> already throws
    /// IOException or UnauthorizedAccessException — covers a refused read as well. Nothing has been written when it
    /// throws: both doors read everything before they write anything.
    /// </summary>
    static JsonObject Read_TreeForEditing_OrThrow(string filePath, bool corruptReadsAsEmpty)
    {
        var (tree, refusal) = Read_TreeForEditing(filePath, log: null, corruptReadsAsEmpty);

        return tree ?? throw new IOException(refusal);
    }

    /// <summary>
    /// The file's text as the object a writer may edit, or why it is not one. Strict where the loader is lenient on
    /// exactly one point — a duplicated key is refused at the parse rather than surfacing later as a throw from a
    /// lazily built <see cref="JsonObject"/>, the choice <c>SettingsRequest_Handler</c> makes for a PUT body for the
    /// same reason.
    ///
    /// <para>
    /// <c>IsDuplicateKey</c> says which refusal it was (P36). The strict parse reports a duplicate as an ordinary
    /// <see cref="JsonException"/>, so the text is parsed once more with duplicates allowed: an OBJECT that then
    /// parses is a file whose only fault is a key stated twice. Asked only on the refusal path, so a readable file
    /// is parsed once.
    /// </para>
    /// </summary>
    static (JsonObject? Tree_OrNull, string? Problem_OrNull, bool IsDuplicateKey) Parse_Tree(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (null, "the file is empty", false);

        try
        {
            return JsonNode.Parse(text, nodeOptions: null, EDIT_PARSE_OPTIONS) is JsonObject tree
                ? (tree, null, false)
                : (null, "its top level is not a JSON object", false);
        }
        catch (JsonException ex)
        {
            return Is_ObjectWithDuplicateKey(text)
                ? (null, $"a key is stated twice — {ex.Message}", true)
                : (null, ex.Message, false);
        }
    }

    /// <summary>True when the text parses as an object once duplicate keys are allowed — see <see cref="Parse_Tree"/>.</summary>
    static bool Is_ObjectWithDuplicateKey(string text)
    {
        try
        {
            using var lenient = JsonDocument.Parse(text);

            return lenient.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    static string Log_WriteRefusal(IOrchestrationLog? log, string refusal)
    {
        log?.Log_Warning(GLOBAL_ORCH_ID, WRITE_REFUSED_PREFIX + refusal);

        return refusal;
    }

    /// <summary>
    /// "In use" is the likely cause and is said as one — the reason in brackets (a sharing violation, an access
    /// denial, a folder where the file should be) is what tells them apart.
    /// </summary>
    static string Describe_UnreadableForEditing(string filePath, string reason)
    {
        return $"'{filePath}' could not be read ({reason}) — another program is probably using it. Nothing was written and "
            + "the file is exactly as it was; try again in a moment.";
    }

    static string Describe_UnparsableForEditing(string filePath, string reason)
    {
        return $"'{filePath}' does not parse as a JSON object ({reason}). Nothing was written and the file is exactly as it "
            + "was — rewriting a file this app cannot read would erase every key in it. Fix it by hand first.";
    }

    static JsonObject? Read_JsonObject_OrNull(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var text = File.ReadAllText(filePath);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return JsonNode.Parse(text) as JsonObject;
    }

    static IReadOnlyList<IRepoEntry> Parse_Repos(JsonObject? configRoot)
    {
        List<IRepoEntry> repos = [];

        if (configRoot == null)
            return repos;

        if (configRoot["repos"] is not JsonArray reposArray)
            return repos;

        foreach (var node in reposArray)
        {
            if (node is not JsonObject repoObject)
                continue;

            var name = Get_String_OrNull(repoObject, "name");
            var path = Get_String_OrNull(repoObject, "path");

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path))
                continue;

            repos.Add(RepoEntry_Factory.Create(name, path, Get_Int_OrNull(repoObject, "topicColor")));
        }

        return repos;
    }

    /// <summary>
    /// The <c>planBackend</c> object, or null when the key is absent — which is every config.json
    /// written before this existed and every one whose owner never opted in.
    ///
    /// <para>
    /// A KIND THAT IS PRESENT BUT NOT A STRING IS CARRIED THROUGH, NOT DROPPED, and that is the whole
    /// reason this method reads the node itself instead of only <see cref="Get_String_OrNull"/>.
    /// <c>PlanBackend_Loader</c> has a dated contract of its own — an unrecognised kind is a FAILED
    /// load with a warning, because <c>"externa1"</c> once read as the default and produced no error
    /// at all — and making the string reader tolerant on 2026-09-10 reopened exactly that hole through
    /// a different door: <c>{"kind": 42}</c> became null, null became "the owner never configured a
    /// backend", and the loader never got a settings object to complain about. So the raw JSON text
    /// stands in for the word: the loader then says <c>planBackend.kind '42' is not recognised</c>,
    /// which is the loud failure that component promises. Found by the re-review of this very fix.
    /// </para>
    /// </summary>
    static PlanBackendSettings? Parse_PlanBackend_OrNull(JsonObject? configRoot)
    {
        if (configRoot?["planBackend"] is not JsonObject backendRoot)
            return null;

        var kindNode = backendRoot["kind"];

        if (kindNode == null)
            return null;

        var kind = Get_String_OrNull(backendRoot, "kind") ?? kindNode.ToJsonString();

        if (string.IsNullOrWhiteSpace(kind))
            return null;

        return new PlanBackendSettings(
            kind,
            Get_String_OrNull(backendRoot, "assembly"),
            Get_String_OrNull(backendRoot, "type"));
    }

    /// <summary>
    /// TOLERATES A VALUE OF THE WRONG TYPE, for the reason <see cref="Get_Long_OrNull"/> below states
    /// and this reader was missing: <c>JsonNode.GetValue&lt;string?&gt;()</c> THROWS for a JSON number
    /// or boolean, and nothing here caught it.
    ///
    /// <para>
    /// Proven 2026-09-10: <c>{"reviewerModel": 5}</c> threw <c>InvalidOperationException</c> out of
    /// <see cref="Load_OrEmpty"/>, and <c>{"reviewerModel": true}</c> threw it out of
    /// <c>IOrchestratorConfigProvider.Get_Current()</c> — which is on the app's startup path AND on
    /// every tick, with no try/catch above it. One unquoted hand-edit in config.json was an app that
    /// would not start. A typo must cost the DEFAULT for that one setting, never the app.
    /// </para>
    /// <para>
    /// THIRTEEN SETTINGS BECOME TOLERANT AT ONCE, since this helper is shared: the six model keys
    /// (<c>supervisorModel</c>, <c>implementerModel</c>, <c>reviewerModel</c>, <c>soloModel</c>,
    /// <c>generalSupervisorModel</c>, <c>communicatorModel</c>), <c>telegramBotToken</c>,
    /// <c>voiceTranscribeCommand</c>, a repo entry's <c>name</c> and <c>path</c> (a mistyped one is
    /// skipped by <see cref="Parse_Repos"/>'s own blank check, as it already was for a blank string),
    /// and <c>planBackend</c>'s <c>assembly</c>/<c>type</c>. Its <c>kind</c> is the ONE exception and
    /// <see cref="Parse_PlanBackend_OrNull"/> makes it: a mistyped kind must stay LOUD, because that
    /// component's own contract says an unrecognised kind is a failed load rather than a silent
    /// downgrade to PLAN.md. Each of the other twelve
    /// moves from "takes the whole load down" to "takes its own default", and nothing else changes:
    /// a correctly typed value reads exactly as before.
    /// </para>
    /// </summary>
    static string? Get_String_OrNull(JsonObject? root, string key)
    {
        if (root == null)
            return null;

        var node = root[key];
        if (node == null)
            return null;

        try
        {
            return node.GetValue<string?>();
        }
        catch
        {
            // Broad by intent, like the numeric and boolean readers: every way a value fails to be a
            // string — a number, a boolean, an object, an array — is the same situation here.
            return null;
        }
    }

    /// <summary>
    /// TOLERATES A VALUE OF THE WRONG TYPE, which it did not until this stage made that reachable.
    /// <c>JsonNode.GetValue&lt;long&gt;()</c> THROWS for a JSON string, and nothing here caught it —
    /// so a single typo in config.json (<c>"buttonExpiryMinutes": "720"</c>, quotes and all) took
    /// the whole load down, and the load is on the app's startup path. A typo must cost the DEFAULT
    /// for that one setting, never the app.
    /// </summary>
    static long? Get_Long_OrNull(JsonObject? root, string key)
    {
        if (root == null)
            return null;

        var node = root[key];
        if (node == null)
            return null;

        try
        {
            return node.GetValue<long>();
        }
        catch
        {
            // Broad by intent: every way a value fails to be a number is the same situation here.
            return null;
        }
    }

    /// <summary>
    /// The per-role model keys this loader reads but never writes — named once, because a key spelled
    /// in two places is a key that gets read under one spelling and saved under the other.
    /// </summary>
    public const string REVIEWER_MODEL_KEY = "reviewerModel";
    public const string SOLO_MODEL_KEY = "soloModel";

    /// <summary>The config keys behind <see cref="IGuardrailSettings"/>, named once.</summary>
    const string GUARDRAIL_HIGH_RISK_PATTERNS = "highRiskPatterns";
    const string GUARDRAIL_HIGH_RISK_CODE_EXPIRY_MINUTES = "highRiskCodeExpiryMinutes";
    const string GUARDRAIL_DISPATCH_PAUSE_THRESHOLD_PERCENT = "dispatchPauseThresholdPercent";
    const string GUARDRAIL_BUTTON_EXPIRY_MINUTES = "buttonExpiryMinutes";

    /// <summary>Null when the key is absent; an empty list when it is present and empty.</summary>
    static IReadOnlyList<string>? Get_StringList_OrNull(JsonObject? root, string key)
    {
        if (root?[key] is not JsonArray array)
            return null;

        List<string> values = [];

        foreach (var node in array)
        {
            // PER ELEMENT, and unguarded this was the same defect the numeric readers below were
            // just fixed for — one non-string entry took the whole load down, and the load is on the
            // app's startup path. `"highRiskPatterns": ["push", "deploy", 3]` is a plausible
            // hand-edit; it must cost that entry, not the app.
            string? value;

            try
            {
                value = node?.GetValue<string>();
            }
            catch
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(value))
                values.Add(value);
        }

        return values;
    }

    static int? Get_Int_OrNull(JsonObject? root, string key)
    {
        var value = Get_Long_OrNull(root, key);

        if (value == null || value.Value > int.MaxValue || value.Value < int.MinValue)
            return null;

        return (int)value.Value;
    }

    static double? Get_Double_OrNull(JsonObject? root, string key)
    {
        var node = root?[key];

        if (node == null)
            return null;

        try
        {
            return node.GetValue<double>();
        }
        catch
        {
            // A non-numeric value is a typo, and the factory's default is the only safe reading.
            return null;
        }
    }

    /// <summary>
    /// Guarded for the reason the numeric readers are: a value of the wrong type is a typo in one
    /// setting, and a typo must never be able to stop the app from starting.
    /// </summary>
    static bool? Get_Bool_OrNull(JsonObject? root, string key)
    {
        if (root == null)
            return null;

        var node = root[key];
        if (node == null)
            return null;

        try
        {
            return node.GetValue<bool>();
        }
        catch
        {
            return null;
        }
    }
}
