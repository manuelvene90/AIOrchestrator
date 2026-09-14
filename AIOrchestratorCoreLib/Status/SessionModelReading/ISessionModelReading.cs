namespace AIOrchestratorCoreLib.Status.SessionModelReading;

/// <summary>
/// What model, and at what effort, ONE session is actually running — as that session's own
/// status-line probe last reported it. Claude Code hands the probe a `model` object and (since
/// 2.1.266, on models with the dial) an `effort` object on every render; the probe dumps both
/// verbatim into the session's .usage.json, so this costs a file read and no new plumbing.
///
/// THE MODEL IS NON-NULLABLE BY CONSTRUCTION and the effort is not, and the asymmetry is the payload's
/// own: a session always has a model, and the factory returns null for the whole reading rather than
/// an object with no subject. The effort block is genuinely absent on an older Claude Code and on a
/// model without a dial, so null there means UNKNOWN — never a default the owner could mistake for a
/// setting they chose.
/// </summary>
public interface ISessionModelReading
{
    /// <summary>The name Claude Code shows for the model — "Fable 5.1", "Opus 5" — never the id.</summary>
    string ModelDisplayName { get; }

    /// <summary>
    /// The effort dial as reported: low / medium / high / xhigh / max. Null when the payload carries
    /// no `effort` block, and when it carries a blank one — the factory normalises blank to null so
    /// a consumer has exactly one spelling of "unknown" to check.
    /// </summary>
    string? EffortLevel { get; }
}
