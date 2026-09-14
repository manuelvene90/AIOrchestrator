using AIOrchestratorCoreLib.Limits;
using AIOrchestratorCoreLib.Usage;

namespace AIOrchestratorCoreLib.Status.SessionModelReading;

/// <summary>
/// Reads one session's model and effort out of the status-line probe file it writes beside itself
/// (.usage.json).
///
/// BOTH FIELDS ARE PARSED IN EXACTLY ONE PLACE EACH — <see cref="RateLimits_Reader.Read_ModelName_OrNull"/>
/// and <see cref="RateLimits_Reader.Read_EffortLevel_OrNull"/> — and this factory only decides
/// whether there is a reading at all. The model reader already fed /limits' "models" tail; a second
/// `display_name` parser here is how the pulse and /limits would start naming different models for
/// one session.
/// </summary>
public static class SessionModelReading_Factory
{
    /// <summary>
    /// A reading from figures already in hand. The file-reading overload below is how the app gets
    /// one; this exists so a caller that already knows the values — a test, or any future source
    /// that is not a probe file — does not have to write a file to express one, and so that
    /// `new SessionModelReadingModel` stays inside this factory where the pattern requires it.
    ///
    /// A BLANK MODEL IS REFUSED, BY NAME: a reading without a model has no subject, and letting one
    /// through would give every consumer an empty string to check for on top of the null it already
    /// checks — two spellings of "unknown". A blank EFFORT is merely unknown, and is stored as null
    /// for the same one-spelling reason.
    /// </summary>
    public static ISessionModelReading Create(string modelDisplayName, string? effortLevel)
    {
        if (string.IsNullOrWhiteSpace(modelDisplayName))
            throw new ArgumentException($"modelDisplayName must name a model, got '{modelDisplayName}'", nameof(modelDisplayName));

        return new SessionModelReadingModel(modelDisplayName, Normalise_Effort_OrNull(effortLevel));
    }

    /// <summary>
    /// The reading, or null when there is none: no probe file yet, an unreadable or half-written
    /// one, or a payload with no model in it. An effort with no model behind it is NOT a reading —
    /// "xhigh" of nothing in particular would put a dial on the pulse with no session to attach it
    /// to. Null means UNKNOWN and every surface drops the field for it.
    /// </summary>
    public static ISessionModelReading? Create_OrNull(string usageFilePath)
    {
        try
        {
            if (!File.Exists(usageFilePath))
                return null;

            var rawJson = UsageTotals_Reader.Read_Text_Safe(usageFilePath);

            if (string.IsNullOrEmpty(rawJson))
                return null;

            var modelDisplayName = RateLimits_Reader.Read_ModelName_OrNull(rawJson);

            if (string.IsNullOrWhiteSpace(modelDisplayName))
                return null;

            return Create(modelDisplayName, RateLimits_Reader.Read_EffortLevel_OrNull(rawJson));
        }
        catch
        {
            return null;
        }
    }

    static string? Normalise_Effort_OrNull(string? effortLevel)
    {
        if (string.IsNullOrWhiteSpace(effortLevel))
            return null;

        return effortLevel;
    }
}
