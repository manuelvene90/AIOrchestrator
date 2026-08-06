using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.GeneralSupervision.AddImplementerRequest;
using AIOrchestratorCoreLib.GeneralSupervision.CloseImplementerRequest;
using AIOrchestratorCoreLib.GeneralSupervision.CloseOrchestrationRequest;
using AIOrchestratorCoreLib.GeneralSupervision.MalformedRequest;
using AIOrchestratorCoreLib.GeneralSupervision.PendingRequests;
using AIOrchestratorCoreLib.GeneralSupervision.SetTelegramMutedRequest;
using AIOrchestratorCoreLib.GeneralSupervision.StartOrchestrationRequest;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.GeneralSupervision;

/// <summary>
/// Reads pending request files from .requests/. Agents (general supervisor, orchestration
/// supervisors) drop these; the app executes them. Malformed files are reported WITH A REASON
/// (agents hand-write them — the log must say what was wrong) and must be deleted by the caller
/// alongside processed ones, so a bad file can never wedge the loop.
///
/// Supported actions (retries REUSE the same action string — never invent variants):
///   {"action":"start-orchestration","repo":"..."}                     (general supervisor; id auto-allocated)
///   {"action":"add-implementer","orchId":"..."}                       (orchestration supervisor)
///   {"action":"close-implementer","orchId":"...","memberId":"imp-n"}  (orchestration supervisor)
///   {"action":"close-orchestration","orchId":"..."}                   (general supervisor)
///   {"action":"set-telegram-muted","muted":true|false}                (any supervisor — DND mode)
/// </summary>
public static class OrchestrationRequests_Reader
{
    public const string START_ORCHESTRATION_ACTION = "start-orchestration";
    public const string ADD_IMPLEMENTER_ACTION = "add-implementer";
    public const string CLOSE_IMPLEMENTER_ACTION = "close-implementer";
    public const string CLOSE_ORCHESTRATION_ACTION = "close-orchestration";
    public const string SET_TELEGRAM_MUTED_ACTION = "set-telegram-muted";

    public static IPendingRequests Read_Pending(ISupervisionPaths paths)
    {
        List<IStartOrchestrationRequest> startRequests = [];
        List<IAddImplementerRequest> addImplementerRequests = [];
        List<ICloseImplementerRequest> closeImplementerRequests = [];
        List<ICloseOrchestrationRequest> closeOrchestrationRequests = [];
        List<ISetTelegramMutedRequest> setTelegramMutedRequests = [];
        List<IMalformedRequest> malformedRequests = [];

        if (Directory.Exists(paths.RequestsFolder))
        {
            foreach (var file in Directory.EnumerateFiles(paths.RequestsFolder, "*.json"))
            {
                var rejectionReason = Try_ParseInto_OrReason(
                    file, startRequests, addImplementerRequests, closeImplementerRequests, closeOrchestrationRequests, setTelegramMutedRequests);

                if (rejectionReason != null)
                    malformedRequests.Add(MalformedRequest_Factory.Create(file, rejectionReason));
            }
        }

        return PendingRequests_Factory.Create(
            startRequests, addImplementerRequests, closeImplementerRequests, closeOrchestrationRequests, setTelegramMutedRequests, malformedRequests);
    }

    /// <summary>Returns null on success, otherwise the rejection reason.</summary>
    static string? Try_ParseInto_OrReason(
        string filePath,
        List<IStartOrchestrationRequest> startRequests,
        List<IAddImplementerRequest> addImplementerRequests,
        List<ICloseImplementerRequest> closeImplementerRequests,
        List<ICloseOrchestrationRequest> closeOrchestrationRequests,
        List<ISetTelegramMutedRequest> setTelegramMutedRequests)
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

                    startRequests.Add(StartOrchestrationRequest_Factory.Create(repoQuery, filePath));
                    return null;
                }
                case ADD_IMPLEMENTER_ACTION:
                {
                    if (string.IsNullOrWhiteSpace(orchId))
                        return "missing 'orchId'";

                    addImplementerRequests.Add(AddImplementerRequest_Factory.Create(orchId, filePath));
                    return null;
                }
                case CLOSE_IMPLEMENTER_ACTION:
                {
                    var memberId = root["memberId"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(orchId) || string.IsNullOrWhiteSpace(memberId))
                        return "missing 'orchId' or 'memberId'";

                    closeImplementerRequests.Add(CloseImplementerRequest_Factory.Create(orchId, memberId, filePath));
                    return null;
                }
                case CLOSE_ORCHESTRATION_ACTION:
                {
                    if (string.IsNullOrWhiteSpace(orchId))
                        return "missing 'orchId'";

                    closeOrchestrationRequests.Add(CloseOrchestrationRequest_Factory.Create(orchId, filePath));
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
                default:
                {
                    var known = string.Join(", ", new[]
                    {
                        START_ORCHESTRATION_ACTION, ADD_IMPLEMENTER_ACTION, CLOSE_IMPLEMENTER_ACTION,
                        CLOSE_ORCHESTRATION_ACTION, SET_TELEGRAM_MUTED_ACTION,
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
