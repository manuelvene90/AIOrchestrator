namespace AIOrchestratorCoreLib.Kit;

/// <summary>
/// What un-wiring a legacy hook entry actually did. A bool could not tell "there was nothing of ours
/// in there" from "I could not read the file", and those two need opposite reactions from the caller:
/// the first is the normal case on a clean machine, the second means the legacy entries are still
/// live and nobody has been told.
/// </summary>
public enum UnwireOutcomes
{
    /// <summary>No file, no hooks block, or none of our entries in it. Nothing written.</summary>
    NothingToDo,

    /// <summary>Our entries were removed and the file was rewritten (previous copy backed up).</summary>
    Unwired,

    /// <summary>The file exists and could not be read or parsed. Our entries, if any, are STILL THERE.</summary>
    Unreadable,
}
