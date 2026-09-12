using AIOrchestratorCoreLib.Configuration.DefaultsSettings;
using AIOrchestratorCoreLib.Configuration.GuardrailSettings;
using AIOrchestratorCoreLib.Configuration.RepoEntry;
using AIOrchestratorCoreLib.Configuration.TelegramProseSettings;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.RunnerConfigs;
using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

namespace AIOrchestratorCoreLib.Configuration.OrchestratorConfig;

public static class OrchestratorConfig_Factory
{
    /// <summary>
    /// THE SHIPPED DEFAULT IS NOW A CATALOGUE ENTRY, not a literal here (spec §6.4, owner §11.4:
    /// "Opus for every role except general and communicator"). Moved 2026-09-12: the same number had to
    /// be stateable by a preset, editable by three renderers and readable by the resolver, and a `const`
    /// in this file is none of those.
    ///
    /// <para>
    /// RULED 2026-09-12 (task-6 fix round 1): <c>classic</c> used to name all four judging roles
    /// (<c>models.supervisor/implementer/reviewer/solo</c>) at the older Fable id, which meant the
    /// owner's own request here — Opus as the SHIPPED default — was satisfied on paper only, since
    /// every machine that names no preset (the common case) got the preset's Fable instead. Those four
    /// rows are gone from <c>kit/presets/classic.json</c> now, so a config.json that states nothing at
    /// all really does spawn the catalogue's Opus; only <c>effort.supervisor</c>/<c>effort.solo</c>
    /// (the owner's working preferences, not a model) remain preset-stated.
    /// </para>
    /// <para>
    /// STILL NAMED HERE because six call sites and four tests read these by name, and because this file
    /// is where the LADDER lives: an absent reviewerModel or soloModel falls to implementerModel before
    /// any default applies, and that is unchanged.
    /// </para>
    /// </summary>
    public static readonly string DEFAULT_SUPERVISOR_MODEL = Read_ShippedModel(SessionRoles.Supervisor);
    public static readonly string DEFAULT_IMPLEMENTER_MODEL = Read_ShippedModel(SessionRoles.Implementer);
    public static readonly string DEFAULT_REVIEWER_MODEL = Read_ShippedModel(SessionRoles.Reviewer);
    public static readonly string DEFAULT_SOLO_MODEL = Read_ShippedModel(SessionRoles.Solo);
    public static readonly string DEFAULT_GENERAL_SUPERVISOR_MODEL = Read_ShippedModel(SessionRoles.General);
    public static readonly string DEFAULT_COMMUNICATOR_MODEL = Read_ShippedModel(SessionRoles.Communicator);

    /// <summary>
    /// READS THE CATALOGUE'S OWN LITERAL, NEVER ONE OF THIS CLASS'S SIX CONSTANTS ABOVE — and every
    /// one of <c>SettingsCatalog.Build_Models</c>'s six model rows MUST likewise pass a plain string
    /// literal as its own <c>shippedDefault</c>, never <c>OrchestratorConfig_Factory.DEFAULT_*_MODEL</c>.
    /// The six fields above are `static readonly`, assigned by running THIS method at this class's own
    /// static-constructor time, and this method's first call reaches into <c>SettingsCatalog</c>. If
    /// any catalogue row read one of this class's six constants back, that would be a TYPE-INITIALIZER
    /// CYCLE: this class's static constructor would trigger <c>SettingsCatalog</c>'s, which would call
    /// back into this class's constants before they finish being assigned — C#'s reentrant
    /// type-initialization rule does not deadlock or throw for that, it silently hands back the
    /// constant's default value (<c>null</c> for a string) at that point in the recursion, so the
    /// catalogue row would build with a null shipped default instead of erroring at compile time.
    /// Found and fixed 2026-09-12 while wiring this method in for the first time: the catalogue was
    /// still pointing four of its six model rows at these very constants.
    /// </summary>
    static string Read_ShippedModel(SessionRoles role)
    {
        var definition = Catalog.Find_OrNull(Catalog.Get_ModelPath(role))
            ?? throw new Exception($"No catalogue entry for {Catalog.Get_ModelPath(role)} — a role without a registered model default cannot spawn");

        return definition.Default_OrNull!.GetValue<string>();
    }

    /// <summary>Opt-in: a screenshot raises a real window, so an absent key must read as OFF.</summary>
    public const bool DEFAULT_TELEGRAM_STATUS_SCREENSHOTS = false;

    public static IOrchestratorConfig Create(
        IReadOnlyList<IRepoEntry> repos,
        string? supervisorModel,
        string? implementerModel,

        // AFTER THE IMPLEMENTER AND BEFORE THE GENERAL, which is SessionRole_Names.ALL's own order —
        // and inserted positionally rather than appended, deliberately: every existing call site
        // fails to compile instead of silently binding a string to the wrong role. Measured while
        // writing this: all six of them do.
        string? reviewerModel,
        string? soloModel,
        string? generalSupervisorModel,
        string? communicatorModel,
        long? telegramSupergroupChatId,
        long? telegramOwnerUserId,
        string? telegramBotToken,
        bool? telegramStatusScreenshots,
        string? voiceTranscribeCommand,
        long? orchestrationTokenBudget,

        // OPTIONAL, AND ONLY THESE FOUR. Every other parameter is required because every caller
        // knows its value; these keys are hand-edited in config.json and no window has a field for
        // any of them, so the Settings window builds a config without them — and the loader, which is
        // the only reader that can have them, passes them explicitly. Save() never serialises any of
        // the four, so a config built without them cannot erase them from disk.
        PlanBackendSettings? planBackend = null,
        IGuardrailSettings? guardrails = null,
        IDefaultsSettings? defaults = null,
        ITelegramProseSettings? telegramProse = null,

        // A FIFTH OF THE SAME KIND, and it obeys the same three rules: hand-edited in config.json,
        // no window field, never serialised by Save — so a config rebuilt without it cannot erase
        // it from disk. Null reads as `poll`, which is what every host did before the key existed.
        Telegram.TelegramInboundModes? telegramInbound = null)
    {
        return Create(
            repos, supervisorModel, implementerModel, reviewerModel, soloModel, generalSupervisorModel, communicatorModel,
            telegramSupergroupChatId, telegramOwnerUserId, telegramBotToken,
            telegramStatusScreenshots, voiceTranscribeCommand, orchestrationTokenBudget,
            RunnerConfigs_Factory.Create_Default(), planBackend, guardrails, defaults, telegramProse, telegramInbound);
    }

    /// <summary>
    /// The full shape. Callers that rebuild a config from parts (the Settings window) MUST pass the
    /// runners through from the config they started from — the overload above defaults them, and a
    /// save through it would silently reset a hand-edited <c>runners</c> block to terminal.
    /// </summary>
    public static IOrchestratorConfig Create(
        IReadOnlyList<IRepoEntry> repos,
        string? supervisorModel,
        string? implementerModel,

        // AFTER THE IMPLEMENTER AND BEFORE THE GENERAL, which is SessionRole_Names.ALL's own order —
        // and inserted positionally rather than appended, deliberately: every existing call site
        // fails to compile instead of silently binding a string to the wrong role. Measured while
        // writing this: all six of them do.
        string? reviewerModel,
        string? soloModel,
        string? generalSupervisorModel,
        string? communicatorModel,
        long? telegramSupergroupChatId,
        long? telegramOwnerUserId,
        string? telegramBotToken,
        bool? telegramStatusScreenshots,
        string? voiceTranscribeCommand,
        long? orchestrationTokenBudget,
        IRunnerConfigs runners,
        PlanBackendSettings? planBackend = null,
        IGuardrailSettings? guardrails = null,
        IDefaultsSettings? defaults = null,
        ITelegramProseSettings? telegramProse = null,
        Telegram.TelegramInboundModes? telegramInbound = null)
    {
        return new OrchestratorConfigModel(
            repos,
            First_StatedModel([supervisorModel], DEFAULT_SUPERVISOR_MODEL),
            First_StatedModel([implementerModel], DEFAULT_IMPLEMENTER_MODEL),

            // THE LADDER IS THE COMPATIBILITY PROMISE, and it is resolved HERE so that
            // IOrchestratorConfig.ReviewerModel is the EFFECTIVE answer and no reader has to
            // remember the fallback. An absent reviewerModel means "what the reviewer got until
            // now", which was implementerModel — including the case that matters, an owner who had
            // set implementerModel by hand and never heard of this key. Only when neither is set
            // does the shipped default apply, and it is opus, which is what the reviewer already ran.
            // Through First_StatedModel like every other rung: an empty or whitespace reviewerModel
            // must be absent too, or it slips past implementerModel and the reviewer spawns with no
            // --model flag at all (proven 2026-09-10, see WithAnEmptyReviewerModel_... below).
            First_StatedModel([reviewerModel, implementerModel], DEFAULT_REVIEWER_MODEL),
            First_StatedModel([soloModel, implementerModel], DEFAULT_SOLO_MODEL),
            First_StatedModel([generalSupervisorModel], DEFAULT_GENERAL_SUPERVISOR_MODEL),
            First_StatedModel([communicatorModel], DEFAULT_COMMUNICATOR_MODEL),
            telegramSupergroupChatId,
            telegramOwnerUserId,
            telegramBotToken,
            telegramStatusScreenshots ?? DEFAULT_TELEGRAM_STATUS_SCREENSHOTS,
            voiceTranscribeCommand,
            orchestrationTokenBudget,
            runners,
            planBackend,

            // DEFAULTED, NEVER NULL — and this is where it differs from planBackend above, whose null
            // IS its meaning. Every caller that predates this parameter, including the app's own
            // settings window, keeps compiling and keeps getting the guarded behaviour, which is the
            // only direction an optional guard is allowed to default in.
            guardrails ?? GuardrailSettings_Factory.Create_Default(),

            // SAME RULE AS THE GUARDRAILS ABOVE, and for the same reason: the block decides what an
            // orchestration started without an explicit shape becomes, so a caller that predates it
            // must get the owner's shipped answer rather than a null nobody downstream can read.
            defaults ?? DefaultsSettings_Factory.Create_Default(),

            // AND AGAIN THE SAME RULE. The block decides only the SHAPE of a long entry on the phone,
            // never whether it is delivered, so every caller that predates it gets the shipped
            // shaping rather than a null the mirror would have to test for at the send site.
            telegramProse ?? TelegramProseSettings_Factory.Create_Default(),

            // POLLING IS THE DEFAULT, and the direction is chosen rather than inherited: a host that
            // silently stops polling is a phone that silently stops working, and it cannot report
            // the reason — it is not polling, so it never sees the 409 that would explain it.
            telegramInbound ?? Telegram.TelegramInboundModes.Poll);
    }

    /// <summary>
    /// THE LADDER'S ONE COALESCE — the first rung the owner actually stated, or the shipped default.
    /// Every per-role model in this file resolves through it, so "what counts as the owner having
    /// said nothing" has a single answer for all six roles rather than one per call site (CLAUDE.md
    /// decision 12's rule about formatters, applied to a ladder).
    ///
    /// <para>
    /// AN EMPTY OR WHITESPACE VALUE IS ABSENT, and it takes a method to say so because <c>??</c>
    /// cannot. Proven 2026-09-10: <c>{"implementerModel":"sonnet","reviewerModel":""}</c> handed the
    /// reviewer the empty string, which <c>??</c> passed straight through — and both spawn-command
    /// builders add <c>--model</c> only when the value is not whitespace, so the reviewer was
    /// launched with NO model flag at all: the CLI's own default, neither the ladder's answer nor
    /// this file's, and no line anywhere saying so. An empty key is a cleared field or a leftover,
    /// never a model. The whitespace is trimmed for the same reason a blank value is dropped: what
    /// survives here goes on a command line.
    /// </para>
    /// <para>
    /// THE WORD ITSELF IS NOT VALIDATED, and that is a decision rather than an omission (2026-09-10).
    /// An allowlist here would be a TYPO guard, not a security one — it authenticates nothing about
    /// who wrote the file, so a session that can edit config.json defeats it simply by writing a
    /// word that IS on the list. What it would cost is real: <c>claude --model</c> takes more words
    /// than any list compiled into this build, so a new alias would need a rebuild, and until then
    /// the app would silently substitute its own default for a model the owner deliberately named —
    /// a worse failure than the CLI refusing the word out loud, because it looks healthy.
    /// <c>fable</c> is refused on the REQUEST path
    /// (<c>OrchestrationRequests_Reader.FORBIDDEN_MEMBER_MODEL</c>) precisely because an agent writes
    /// that file; config.json is the owner's own, and that refusal's own message names the owner as
    /// the one who selects fable. Guarding two of the six keys and not the other four would also
    /// give one config file two answers about what a valid model word is.
    /// </para>
    /// </summary>
    static string First_StatedModel(IReadOnlyList<string?> ladder, string shippedDefault)
    {
        foreach (var rung in ladder)
        {
            if (!string.IsNullOrWhiteSpace(rung))
                return rung.Trim();
        }

        return shippedDefault;
    }

    public static IOrchestratorConfig Create_Empty()
    {
        return Create([], null, null, null, null, null, null, null, null, null, null, null, null);
    }

    /// <summary>
    /// The same config with only the status-screenshot flag changed — the /screenshots command
    /// flips it live from the phone, and it must not have to restate every other field just to move
    /// one bool.
    /// </summary>
    public static IOrchestratorConfig Create_WithStatusScreenshots(IOrchestratorConfig source, bool telegramStatusScreenshots)
    {
        return Create(
            source.Repos,
            source.SupervisorModel,
            source.ImplementerModel,
            source.ReviewerModel,
            source.SoloModel,
            source.GeneralSupervisorModel,
            source.CommunicatorModel,
            source.TelegramSupergroupChatId,
            source.TelegramOwnerUserId,
            source.TelegramBotToken,
            telegramStatusScreenshots,
            source.VoiceTranscribeCommand,
            source.OrchestrationTokenBudget,
            source.Runners,
            source.PlanBackend,
            source.Guardrails,
            source.Defaults,
            source.TelegramProse,
            source.TelegramInbound);
    }
}
