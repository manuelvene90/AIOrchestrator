namespace AIOrchestratorCoreLib.Configuration.SettingsCatalog;

/// <summary>
/// WHICH FILE a setting's resolved value ultimately lives in: <c>Machine</c> — this box's taste,
/// shared by every orchestration on it (config.json); <c>Orchestration</c> — one orchestration's
/// own choice (its session.json), which can override the machine's for that orchestration alone.
/// The resolver's four layers (plan 02 §6) read in this order for an Orchestration-scoped setting;
/// a Machine-scoped one has no session.json layer to read.
/// </summary>
public enum SettingScopes
{
    Machine,
    Orchestration,
}
