using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.GeneralSupervision.AddImplementerRequest;
using AIOrchestratorCoreLib.GeneralSupervision.CloseImplementerRequest;
using AIOrchestratorCoreLib.GeneralSupervision.CloseOrchestrationRequest;
using AIOrchestratorCoreLib.GeneralSupervision.MalformedRequest;
using AIOrchestratorCoreLib.GeneralSupervision.PendingRequests;
using AIOrchestratorCoreLib.GeneralSupervision.PromoteOrchestrationRequest;
using AIOrchestratorCoreLib.GeneralSupervision.SetModelRequest;
using AIOrchestratorCoreLib.GeneralSupervision.SetOrchestrationNameRequest;
using AIOrchestratorCoreLib.GeneralSupervision.SetTelegramMutedRequest;
using AIOrchestratorCoreLib.GeneralSupervision.StartOrchestrationRequest;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.GeneralSupervision;

/// <summary>
/// Reads pending request files from .requests/. Agents (general supervisor, orchestration
/// supervisors) drop these; the app executes them. Malformed files are reported WITH A REASON
/// (agents hand-write them — the log must say what was wrong) and must be deleted by the caller
/// alongside processed ones, so a bad file can never wedge the loop.
///
/// Supported actions (retries REUSE the same action string — never invent variants):
///   {"action":"start-orchestration","repo":"...","mode":"full|basic","task":"..."}
///                                       (general supervisor; id auto-allocated. mode is optional:
///                                        absent means "the request did not say", and config.json's
///                                        defaults.orchestrationMode decides — shipped as basic, one
///                                        solo session. task is the owner's own words, which the APP
///                                        writes into the new orchestration's owner channel as its
///                                        first FROM owner entry.)
///   {"action":"add-implementer","orchId":"..."}                       (orchestration supervisor)
///   {"action":"add-reviewer","orchId":"..."}                          (orchestration supervisor; read-only member)
///   {"action":"close-implementer","orchId":"...","memberId":"imp-n"}  (orchestration supervisor; also closes rev-n)
///   {"action":"close-orchestration","orchId":"...","requester":"...","reason":"..."}
///                                       (general supervisor OR that orchestration's supervisor —
///                                        HELD until the owner confirms with a tap, whoever asked,
///                                        so 'requester' is what they are shown, not a gate. The
///                                        owner's own closes do not come through here at all.)
///   {"action":"promote-orchestration","orchId":"...","reason":"..."}
///                                       (a SOLO session, asking for its basic orchestration to
///                                        become a full crew — HELD until the owner taps, and
///                                        refused unless the solo has filed a HANDOVER entry)
///   {"action":"set-telegram-muted","muted":true|false}                (any supervisor — DND mode)
///   {"action":"set-orchestration-name","orchId":"...","name":"..."}   (orchestration supervisor; 2-4 words)
///   {"action":"set-model","orchId":"...","role":"supervisor|implementer","model":"..."}  (per-orchestration override)
/// </summary>
public static class OrchestrationRequests_Reader
{
    public const string START_ORCHESTRATION_ACTION = "start-orchestration";
    public const string ADD_IMPLEMENTER_ACTION = "add-implementer";
    public const string ADD_REVIEWER_ACTION = "add-reviewer";
    public const string CLOSE_IMPLEMENTER_ACTION = "close-implementer";
    public const string PROMOTE_ORCHESTRATION_ACTION = "promote-orchestration";
    public const string CLOSE_ORCHESTRATION_ACTION = "close-orchestration";
    public const string SET_TELEGRAM_MUTED_ACTION = "set-telegram-muted";
    public const string SET_ORCHESTRATION_NAME_ACTION = "set-orchestration-name";
    public const string SET_MODEL_ACTION = "set-model";

    /// <summary>
    /// Every autonomous action costs the owner tokens, so it must justify itself: the app relays
    /// the reason to them. Rejecting is deliberate — a silent spawn left the owner in the dark.
    /// </summary>
    /// <summary>Only the owner may choose this one, and only through set-model — never a session on its own.</summary>
    public const string FORBIDDEN_MEMBER_MODEL = "fable";

    public const string FORBIDDEN_MEMBER_MODEL_MESSAGE = "model 'fable' is not yours to choose — the owner selects it, through set-model; pick sonnet or opus for the task and drop a new file";

    public const string MISSING_REASON_MESSAGE = "missing 'reason' — every autonomous action must state WHY in one short line (it is relayed to the owner)";

    /// <summary>
    /// A full crew: supervisor plus imp-1. Since 2026-08-13 it has to be asked for by name unless
    /// config.json says otherwise — it is the expensive shape, and the one the owner wants justified
    /// rather than defaulted into. The word itself now lives in <see cref="OrchestrationModes"/>,
    /// because the configuration layer names the same two shapes and two spellings of "full" is how
    /// the two come to disagree.
    /// </summary>
    public const string FULL_MODE = OrchestrationModes.FULL;

    /// <summary>One solo session, no supervisor. What a start request buys unless it says otherwise.</summary>
    public const string BASIC_MODE = OrchestrationModes.BASIC;

    public const string MISSING_REQUESTER_MESSAGE = "missing 'requester' — closing an orchestration is irreversible, so the audit trail and the owner's confirmation must both be able to name WHO asked (e.g. \"supervisor of crm-2\")";

    public static IPendingRequests Read_Pending(ISupervisionPaths paths)
    {
        List<IStartOrchestrationRequest> startRequests = [];
        List<IAddImplementerRequest> addImplementerRequests = [];
        List<ICloseImplementerRequest> closeImplementerRequests = [];
        List<ICloseOrchestrationRequest> closeOrchestrationRequests = [];
        List<ISetTelegramMutedRequest> setTelegramMutedRequests = [];
        List<ISetOrchestrationNameRequest> setOrchestrationNameRequests = [];
        List<IPromoteOrchestrationRequest> promoteOrchestrationRequests = [];
        List<ISetModelRequest> setModelRequests = [];
        List<IMalformedRequest> malformedRequests = [];

        if (Directory.Exists(paths.RequestsFolder))
        {
            foreach (var file in Directory.EnumerateFiles(paths.RequestsFolder, "*.json"))
            {
                var rejectionReason = Try_ParseInto_OrReason(
                    file, startRequests, addImplementerRequests, closeImplementerRequests, closeOrchestrationRequests, setTelegramMutedRequests, setOrchestrationNameRequests, promoteOrchestrationRequests, setModelRequests);

                if (rejectionReason != null)
                    malformedRequests.Add(MalformedRequest_Factory.Create(file, rejectionReason, Peek_OrchId_OrNull(file)));
            }
        }

        return PendingRequests_Factory.Create(
            startRequests, addImplementerRequests, closeImplementerRequests, closeOrchestrationRequests, setTelegramMutedRequests, setOrchestrationNameRequests, promoteOrchestrationRequests, setModelRequests, malformedRequests);
    }

    /// <summary>
    /// Re-reads ONE close-orchestration request by path, for files parked outside the scanned
    /// folder while they await the owner's confirmation. It goes through the same parse as every
    /// other request — including the 'requester' requirement — so a parked file can never be
    /// honoured on terms the scanner would have rejected.
    /// </summary>
    public static ICloseOrchestrationRequest? Read_CloseOrchestrationRequest_OrNull(string filePath)
    {
        List<IStartOrchestrationRequest> startRequests = [];
        List<IAddImplementerRequest> addImplementerRequests = [];
        List<ICloseImplementerRequest> closeImplementerRequests = [];
        List<ICloseOrchestrationRequest> closeOrchestrationRequests = [];
        List<ISetTelegramMutedRequest> setTelegramMutedRequests = [];
        List<ISetOrchestrationNameRequest> setOrchestrationNameRequests = [];
        List<IPromoteOrchestrationRequest> promoteOrchestrationRequests = [];
        List<ISetModelRequest> setModelRequests = [];

        var rejection = Try_ParseInto_OrReason(
            filePath,
            startRequests,
            addImplementerRequests,
            closeImplementerRequests,
            closeOrchestrationRequests,
            setTelegramMutedRequests,
            setOrchestrationNameRequests,
            promoteOrchestrationRequests,
            setModelRequests);

        if (rejection != null)
            return null;

        return closeOrchestrationRequests.Count == 1 ? closeOrchestrationRequests[0] : null;
    }

    /// <summary>
    /// The same re-read for a parked close-IMPLEMENTER request. Identical contract to the
    /// orchestration one above: the same strict parse, so a parked file can never be honoured on
    /// terms the scanner would have rejected — including the 'reason' every autonomous action owes
    /// the owner.
    /// </summary>
    public static ICloseImplementerRequest? Read_CloseImplementerRequest_OrNull(string filePath)
    {
        List<IStartOrchestrationRequest> startRequests = [];
        List<IAddImplementerRequest> addImplementerRequests = [];
        List<ICloseImplementerRequest> closeImplementerRequests = [];
        List<ICloseOrchestrationRequest> closeOrchestrationRequests = [];
        List<ISetTelegramMutedRequest> setTelegramMutedRequests = [];
        List<ISetOrchestrationNameRequest> setOrchestrationNameRequests = [];
        List<IPromoteOrchestrationRequest> promoteOrchestrationRequests = [];
        List<ISetModelRequest> setModelRequests = [];

        var rejection = Try_ParseInto_OrReason(
            filePath,
            startRequests,
            addImplementerRequests,
            closeImplementerRequests,
            closeOrchestrationRequests,
            setTelegramMutedRequests,
            setOrchestrationNameRequests,
            promoteOrchestrationRequests,
            setModelRequests);

        if (rejection != null)
            return null;

        return closeImplementerRequests.Count == 1 ? closeImplementerRequests[0] : null;
    }

    /// <summary>
    /// Re-reads ONE parked promote-orchestration request, through the SAME strict parse as everything
    /// else — including the mandatory reason. A parked file can never be honoured on terms the scanner
    /// would have refused, and the reason is what the owner is shown when they are asked to spend.
    /// </summary>
    public static IPromoteOrchestrationRequest? Read_PromoteOrchestrationRequest_OrNull(string filePath)
    {
        List<IStartOrchestrationRequest> startRequests = [];
        List<IAddImplementerRequest> addImplementerRequests = [];
        List<ICloseImplementerRequest> closeImplementerRequests = [];
        List<ICloseOrchestrationRequest> closeOrchestrationRequests = [];
        List<ISetTelegramMutedRequest> setTelegramMutedRequests = [];
        List<ISetOrchestrationNameRequest> setOrchestrationNameRequests = [];
        List<IPromoteOrchestrationRequest> promoteOrchestrationRequests = [];
        List<ISetModelRequest> setModelRequests = [];

        var rejection = Try_ParseInto_OrReason(
            filePath,
            startRequests,
            addImplementerRequests,
            closeImplementerRequests,
            closeOrchestrationRequests,
            setTelegramMutedRequests,
            setOrchestrationNameRequests,
            promoteOrchestrationRequests,
            setModelRequests);

        if (rejection != null)
            return null;

        return promoteOrchestrationRequests.Count == 1 ? promoteOrchestrationRequests[0] : null;
    }

    /// <summary>
    /// Best-effort orch id from a file the strict parse has REJECTED, so a bad request can still be
    /// reported to the session that wrote it instead of vanishing silently.
    /// </summary>
    public static string? Peek_OrchId_OrNull(string filePath)
    {
        try
        {
            return (JsonNode.Parse(File.ReadAllText(filePath)) as JsonObject)?["orchId"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Returns null on success, otherwise the rejection reason.</summary>
    static string? Try_ParseInto_OrReason(
        string filePath,
        List<IStartOrchestrationRequest> startRequests,
        List<IAddImplementerRequest> addImplementerRequests,
        List<ICloseImplementerRequest> closeImplementerRequests,
        List<ICloseOrchestrationRequest> closeOrchestrationRequests,
        List<ISetTelegramMutedRequest> setTelegramMutedRequests,
        List<ISetOrchestrationNameRequest> setOrchestrationNameRequests,
        List<IPromoteOrchestrationRequest> promoteOrchestrationRequests,
        List<ISetModelRequest> setModelRequests)
    {
        JsonObject root;
        try
        {
            var text = File.ReadAllText(filePath);

            if (JsonNode.Parse(text) is not JsonObject parsedObject)
                return "content is not a JSON object";

            root = parsedObject;
        }
        catch (Exception ex)
        {
            return $"unreadable or invalid JSON ({ex.Message})";
        }

        try
        {
            var action = root["action"]?.GetValue<string>();
            var orchId = root["orchId"]?.GetValue<string>();

            switch (action)
            {
                case START_ORCHESTRATION_ACTION:
                {
                    var repoQuery = root["repo"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(repoQuery))
                        return "missing 'repo'";

                    // ABSENT MEANS "THE REQUEST DID NOT SAY", and it is no longer this reader's place
                    // to decide what that buys. It used to be: absent collapsed to basic here, per the
                    // owner's directive of 2026-08-13 ("as a cost-saving measure"). That rule is still
                    // the shipped default, but it now lives in config.json where the owner who set it
                    // can change it — so the null has to survive this far, or an unstated shape and an
                    // explicitly basic one become indistinguishable and the setting can never mean
                    // anything.
                    //
                    // A value we do not recognise is still REJECTED rather than defaulted: a typo must
                    // never decide the shape silently, and that is true in both directions.
                    var mode = root["mode"]?.GetValue<string>();
                    var isBasic = OrchestrationModes.Is_Basic_OrNull(mode);

                    if (mode != null && isBasic == null)
                        return $"mode must be {OrchestrationModes.Describe_Accepted()}, got '{mode}'";

                    // THE TASK TRAVELS WITH THE REQUEST. Optional, because a request written before this
                    // key existed must still start an orchestration; but its absence is the defect the
                    // VPS round found, not a shape anybody wants — a crew that boots with nothing to do
                    // leaves the owner typing the job a second time, into a topic that does not exist yet.
                    startRequests.Add(StartOrchestrationRequest_Factory.Create(repoQuery, isBasic, root["task"]?.GetValue<string>(), filePath));
                    return null;
                }
                case ADD_IMPLEMENTER_ACTION:
                case ADD_REVIEWER_ACTION:
                {
                    if (string.IsNullOrWhiteSpace(orchId))
                        return "missing 'orchId'";

                    var reason = root["reason"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(reason))
                        return MISSING_REASON_MESSAGE;

                    var kind = action == ADD_REVIEWER_ACTION ? MemberKinds.Reviewer : MemberKinds.Implementer;

                    // Optional: the requester's per-task model. Fable is refused HERE, not merely
                    // forbidden in the manual (decision 21 — the app enforces what it can): a
                    // session may not put the owner on the most expensive model on its own judgement.
                    var memberModel = root["model"]?.GetValue<string>()?.Trim();

                    if (memberModel != null && memberModel.Length == 0)
                        memberModel = null;

                    if (memberModel != null && string.Equals(memberModel, FORBIDDEN_MEMBER_MODEL, StringComparison.OrdinalIgnoreCase))
                        return FORBIDDEN_MEMBER_MODEL_MESSAGE;

                    addImplementerRequests.Add(AddImplementerRequest_Factory.Create(orchId, kind, reason.Trim(), filePath, memberModel));
                    return null;
                }
                case PROMOTE_ORCHESTRATION_ACTION:
                {
                    if (string.IsNullOrWhiteSpace(orchId))
                        return "missing 'orchId'";

                    var reason = root["reason"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(reason))
                        return MISSING_REASON_MESSAGE;

                    // NOTHING ABOUT THE ORCHESTRATION IS CHECKED HERE, and that is deliberate. This
                    // reader knows JSON; whether the orchestration is basic, and whether its solo has
                    // filed the handover entry, are facts about the world that the executor reads and
                    // refuses on WITH ITS OWN REASON. A parser that answers "unanalysable" to a
                    // perfectly analysable line — because something it cannot see is not true yet —
                    // wears the safe posture without having it.
                    promoteOrchestrationRequests.Add(PromoteOrchestrationRequest_Factory.Create(orchId, reason.Trim(), filePath));
                    return null;
                }
                case CLOSE_IMPLEMENTER_ACTION:
                {
                    var memberId = root["memberId"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(orchId) || string.IsNullOrWhiteSpace(memberId))
                        return "missing 'orchId' or 'memberId'";

                    var reason = root["reason"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(reason))
                        return MISSING_REASON_MESSAGE;

                    closeImplementerRequests.Add(CloseImplementerRequest_Factory.Create(orchId, memberId, reason.Trim(), filePath));
                    return null;
                }
                case CLOSE_ORCHESTRATION_ACTION:
                {
                    if (string.IsNullOrWhiteSpace(orchId))
                        return "missing 'orchId'";

                    // The owner's own UI close button carries no reason — only AGENT-initiated
                    // closes must justify themselves, so this one defaults instead of rejecting.
                    var reason = root["reason"]?.GetValue<string>();

                    // The requester does NOT default. On 2026-08-11 an orchestration closed and no
                    // artifact on disk could say who asked, because the request file is deleted on
                    // execution and its schema carried no attribution at all. A default would
                    // re-create that same hole under a new name.
                    var requester = root["requester"]?.GetValue<string>();

                    if (string.IsNullOrWhiteSpace(requester))
                        return MISSING_REQUESTER_MESSAGE;

                    closeOrchestrationRequests.Add(CloseOrchestrationRequest_Factory.Create(
                        orchId,
                        string.IsNullOrWhiteSpace(reason) ? "work concluded" : reason.Trim(),
                        requester.Trim(),
                        filePath));

                    return null;
                }
                case SET_TELEGRAM_MUTED_ACTION:
                {
                    var mutedNode = root["muted"];
                    if (mutedNode == null)
                        return "missing 'muted'";

                    setTelegramMutedRequests.Add(SetTelegramMutedRequest_Factory.Create(mutedNode.GetValue<bool>(), filePath));
                    return null;
                }
                case SET_ORCHESTRATION_NAME_ACTION:
                {
                    var nameValue = root["name"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(orchId) || string.IsNullOrWhiteSpace(nameValue))
                        return "missing 'orchId' or 'name'";

                    setOrchestrationNameRequests.Add(SetOrchestrationNameRequest_Factory.Create(orchId, nameValue, filePath));
                    return null;
                }
                case SET_MODEL_ACTION:
                {
                    var roleValue = root["role"]?.GetValue<string>();
                    var modelValue = root["model"]?.GetValue<string>();

                    if (string.IsNullOrWhiteSpace(orchId) || string.IsNullOrWhiteSpace(roleValue) || string.IsNullOrWhiteSpace(modelValue))
                        return "missing 'orchId', 'role' or 'model'";

                    var normalizedRole = roleValue.Trim().ToLowerInvariant();
                    if (normalizedRole != SetModelRequest_Factory.SUPERVISOR_ROLE && normalizedRole != SetModelRequest_Factory.IMPLEMENTER_ROLE)
                        return $"role must be '{SetModelRequest_Factory.SUPERVISOR_ROLE}' or '{SetModelRequest_Factory.IMPLEMENTER_ROLE}', got '{roleValue}'";

                    var modelReason = root["reason"]?.GetValue<string>();

                    setModelRequests.Add(SetModelRequest_Factory.Create(
                        orchId, normalizedRole, modelValue, string.IsNullOrWhiteSpace(modelReason) ? "owner's request" : modelReason.Trim(), filePath));

                    return null;
                }
                default:
                {
                    // PROMOTE_ORCHESTRATION_ACTION WAS MISSING FROM THIS LIST while the case above
                    // handled it perfectly. So a session that mistyped the action string was handed a
                    // list of "known" actions that did not contain the one it wanted — actively
                    // teaching it the feature does not exist, in the same message that told it its
                    // request had failed. The list must be the switch, not a subset of it.
                    var known = string.Join(", ", new[]
                    {
                        START_ORCHESTRATION_ACTION, ADD_IMPLEMENTER_ACTION, ADD_REVIEWER_ACTION,
                        CLOSE_IMPLEMENTER_ACTION, PROMOTE_ORCHESTRATION_ACTION, CLOSE_ORCHESTRATION_ACTION,
                        SET_TELEGRAM_MUTED_ACTION, SET_ORCHESTRATION_NAME_ACTION, SET_MODEL_ACTION,
                    });

                    return $"unknown action '{action}' (known: {known}; retries must reuse the SAME action)";
                }
            }
        }
        catch (Exception ex)
        {
            return $"field has wrong type ({ex.Message})";
        }
    }
}
