namespace AIOrchestratorCoreLib.GeneralSupervision.StartOrchestrationRequest;

/// <summary>
/// A request dropped by the GENERAL supervisor (as a .json file under .requests/) asking the app
/// to start a new orchestration. The app is the executor; the general supervisor is the brain.
/// The orchestration id is ALLOCATED BY THE APP (repo-slug-n, incremental) — never requested.
/// </summary>
public interface IStartOrchestrationRequest
{
    /// <summary>The repo the general supervisor resolved (it maps the owner's colloquial phrasing first).</summary>
    string RepoQuery { get; }

    /// <summary>The request file, deleted after processing (success or failure) so it never loops.</summary>
    string SourceFilePath { get; }
}
