using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration.DefaultsSettings;
using AIOrchestratorCoreLib.Configuration.GuardrailSettings;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.Configuration.RepoEntry;
using AIOrchestratorCoreLib.Configuration.TelegramProseSettings;
using AIOrchestratorCoreLib.Running.RunnerConfigs;
using AIOrchestratorCoreLib.Storage;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Configuration;

/// <summary>
/// Loads and saves the orchestrator configuration. Non-secret settings live in config.json;
/// the bot token lives in secrets.json so the config file can be shared/backed up freely.
/// </summary>
public static class OrchestratorConfig_Loader
{
    public static IOrchestratorConfig Load_OrEmpty(ISupervisionPaths paths)
    {
        var configRoot = Read_JsonObject_OrNull(paths.ConfigFile);
        var secretsRoot = Read_JsonObject_OrNull(paths.SecretsFile);

        if (configRoot == null && secretsRoot == null)
            return OrchestratorConfig_Factory.Create_Empty();

        var repos = Parse_Repos(configRoot);

        return OrchestratorConfig_Factory.Create(
            repos,
            Get_String_OrNull(configRoot, "supervisorModel"),
            Get_String_OrNull(configRoot, "implementerModel"),

            // ABSENT MEANS "WHAT THE ROLE GOT UNTIL NOW", and the factory is where that ladder lives
            // (reviewer/solo → implementer → the shipped default). Passed as the raw key, null and
            // all, precisely so the factory can tell "the owner never said" from "the owner said
            // this" — reading them here with a fallback would hide the first case from the only
            // place that can act on it.
            Get_String_OrNull(configRoot, REVIEWER_MODEL_KEY),
            Get_String_OrNull(configRoot, SOLO_MODEL_KEY),
            Get_String_OrNull(configRoot, "generalSupervisorModel"),
            Get_String_OrNull(configRoot, "communicatorModel"),
            Get_Long_OrNull(configRoot, "telegramSupergroupChatId"),
            Get_Long_OrNull(configRoot, "telegramOwnerUserId"),
            Get_String_OrNull(secretsRoot, "telegramBotToken"),
            Get_Bool_OrNull(configRoot, "telegramStatusScreenshots"),
            Get_String_OrNull(configRoot, "voiceTranscribeCommand"),
            Get_Long_OrNull(configRoot, "orchestrationTokenBudget"),
            RunnerConfigs_Json.Parse(configRoot),
            Parse_PlanBackend_OrNull(configRoot),
            Parse_Guardrails(configRoot),
            DefaultsSettings_Json.Parse(configRoot),
            TelegramProseSettings_Json.Parse(configRoot),

            // `telegramInbound`: "poll" (default) or "off". Hand-edited, never written back by Save
            // below — the same contract as the four blocks above it.
            Telegram.TelegramInbound_Modes.Parse_OrPoll(Get_String_OrNull(configRoot, "telegramInbound")));
    }

    /// <summary>
    /// A MISSING key and an EMPTY value are deliberately different here: no "highRiskPatterns" key
    /// means the owner never said, and gets the default list; an explicitly empty array means they
    /// said "nothing is high risk", which is theirs to say. Collapsing the two would make the guard
    /// impossible to turn off, or impossible to keep.
    /// </summary>
    static IGuardrailSettings Parse_Guardrails(JsonObject? configRoot)
    {
        return GuardrailSettings_Factory.Create(
            Get_StringList_OrNull(configRoot, GUARDRAIL_HIGH_RISK_PATTERNS),
            Get_Int_OrNull(configRoot, GUARDRAIL_HIGH_RISK_CODE_EXPIRY_MINUTES),
            Get_Double_OrNull(configRoot, GUARDRAIL_DISPATCH_PAUSE_THRESHOLD_PERCENT),
            Get_Int_OrNull(configRoot, GUARDRAIL_BUTTON_EXPIRY_MINUTES));
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
    /// </summary>
    public static void Save(IOrchestratorConfig config, ISupervisionPaths paths)
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

        var configRoot = Read_JsonObject_ForEditing(paths.ConfigFile);

        configRoot["repos"] = reposArray;
        configRoot["supervisorModel"] = config.SupervisorModel;
        configRoot["implementerModel"] = config.ImplementerModel;
        configRoot["generalSupervisorModel"] = config.GeneralSupervisorModel;
        configRoot["communicatorModel"] = config.CommunicatorModel;
        configRoot["telegramSupergroupChatId"] = config.TelegramSupergroupChatId;
        configRoot["telegramOwnerUserId"] = config.TelegramOwnerUserId;
        configRoot["telegramStatusScreenshots"] = config.TelegramStatusScreenshots;
        configRoot["voiceTranscribeCommand"] = config.VoiceTranscribeCommand;
        configRoot["orchestrationTokenBudget"] = config.OrchestrationTokenBudget;

        RunnerConfigs_Json.Write(configRoot, config.Runners);

        Atomic_FileWriter.Write_AllText(paths.ConfigFile, configRoot.ToJsonString(JsonWriting.INDENTED));

        // reviewerModel AND soloModel ARE READ AND NEVER WRITTEN, and they belong to the paragraph
        // below rather than beside their four siblings above. The four have a Settings field, so the
        // value in the file is the owner's own; these two have none, and their default is one that is
        // MEANT TO MOVE — an absent reviewerModel tracks implementerModel by design (owner
        // 2026-09-09: implementer sonnet eventually, reviewer opus). Writing this build's answer
        // would materialise it as if the owner had chosen it and cut that ladder for good, on the
        // first button press, on every box that had never heard of the keys. A hand-edited value is
        // safe either way: Save() merges, so keys it does not write survive untouched.
        //
        // planBackend, THE GUARDRAIL KEYS, defaults AND telegram ARE DELIBERATELY ABSENT from the writes above,
        // for the same reason from two directions. planBackend is hand-edited, no window builds one,
        // and IOrchestratorConfig.PlanBackend is null in every config the app constructs itself —
        // writing it would erase the owner's own key on the next save. The guardrail keys and the
        // defaults block have no UI and no command that changes them, so the only thing a save could
        // do is materialise this build's defaults into the file as if the owner had chosen them,
        // freezing a default that is meant to move when the app is updated. The telegram block —
        // foldLongEntriesAbove, attachEntriesAbove — is the newest member of that same set. All four
        // are read; none is owned.

        var secretsRoot = Read_JsonObject_ForEditing(paths.SecretsFile);

        secretsRoot["telegramBotToken"] = config.TelegramBotToken;

        Atomic_FileWriter.Write_AllText(paths.SecretsFile, secretsRoot.ToJsonString(JsonWriting.INDENTED));
    }

    /// <summary>
    /// The tree a save edits: the file's own object when it can be read, an empty one when it
    /// cannot. A file that will not parse has no unknown keys worth preserving — they are already
    /// unreachable — and refusing to save over it would strand the owner with a corrupt config and
    /// no way to fix it from the app.
    /// </summary>
    static JsonObject Read_JsonObject_ForEditing(string filePath)
    {
        try
        {
            return Read_JsonObject_OrNull(filePath) ?? [];
        }
        catch
        {
            // Broad by intent: malformed, truncated, or not an object at all are one situation here.
            return [];
        }
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
