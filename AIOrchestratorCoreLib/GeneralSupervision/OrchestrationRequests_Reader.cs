using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.GeneralSupervision.AddImplementerRequest;
using AIOrchestratorCoreLib.GeneralSupervision.CloseImplementerRequest;
using AIOrchestratorCoreLib.GeneralSupervision.CloseOrchestrationRequest;
using AIOrchestratorCoreLib.GeneralSupervision.PendingRequests;
using AIOrchestratorCoreLib.GeneralSupervision.SetTelegramMutedRequest;
using AIOrchestratorCoreLib.GeneralSupervision.StartOrchestrationRequest;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.GeneralSupervision;

/// <summary>
/// Reads pending request files from .requests/. Agents (general supervisor, orchestration
/// supervisors) drop these; the app executes them. Malformed files are reported and must be
/// deleted by the caller alongside processed ones, so a bad file can never wedge the loop.
///
/// Supported actions:
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
        List<string> malformedFiles = [];

        if (!Directory.Exists(paths.RequestsFolder))
        {
            return PendingRequests_Factory.Create(
                startRequests, addImplementerRequests, closeImplementerRequests, closeOrchestrationRequests, setTelegramMutedRequests, malformedFiles);
        }

        foreach (var file in Directory.EnumerateFiles(paths.RequestsFolder, "*.json"))
        {
            var parsed = Try_ParseInto(
                file, startRequests, addImplementerRequests, closeImplementerRequests, closeOrchestrationRequests, setTelegramMutedRequests);

            if (!parsed)
                malformedFiles.Add(file);
        }

        return PendingRequests_Factory.Create(
            startRequests, addImplementerRequests, closeImplementerRequests, closeOrchestrationRequests, setTelegramMutedRequests, malformedFiles);
    }

    static bool Try_ParseInto(
        string filePath,
        List<IStartOrchestrationRequest> startRequests,
        List<IAddImplementerRequest> addImplementerRequests,
        List<ICloseImplementerRequest> closeImplementerRequests,
        List<ICloseOrchestrationRequest> closeOrchestrationRequests,
        List<ISetTelegramMutedRequest> setTelegramMutedRequests)
    {
        try
        {
            var text = File.ReadAllText(filePath);

            if (JsonNode.Parse(text) is not JsonObject root)
                return false;

            var action = root["action"]?.GetValue<string>();
            var orchId = root["orchId"]?.GetValue<string>();

            switch (action)
            {
                case START_ORCHESTRATION_ACTION:
                {
                    var repoQuery = root["repo"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(repoQuery))
                        return false;

                    startRequests.Add(StartOrchestrationRequest_Factory.Create(repoQuery, filePath));
                    return true;
                }
                case ADD_IMPLEMENTER_ACTION:
                {
                    if (string.IsNullOrWhiteSpace(orchId))
                        return false;

                    addImplementerRequests.Add(AddImplementerRequest_Factory.Create(orchId, filePath));
                    return true;
                }
                case CLOSE_IMPLEMENTER_ACTION:
                {
                    var memberId = root["memberId"]?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(orchId) || string.IsNullOrWhiteSpace(memberId))
                        return false;

                    closeImplementerRequests.Add(CloseImplementerRequest_Factory.Create(orchId, memberId, filePath));
                    return true;
                }
                case CLOSE_ORCHESTRATION_ACTION:
                {
                    if (string.IsNullOrWhiteSpace(orchId))
                        return false;

                    closeOrchestrationRequests.Add(CloseOrchestrationRequest_Factory.Create(orchId, filePath));
                    return true;
                }
                case SET_TELEGRAM_MUTED_ACTION:
                {
                    var mutedNode = root["muted"];
                    if (mutedNode == null)
                        return false;

                    setTelegramMutedRequests.Add(SetTelegramMutedRequest_Factory.Create(mutedNode.GetValue<bool>(), filePath));
                    return true;
                }
                default:
                {
                    return false;
                }
            }
        }
        catch
        {
            return false;
        }
    }
}
