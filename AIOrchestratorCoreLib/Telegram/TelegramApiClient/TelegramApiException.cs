namespace AIOrchestratorCoreLib.Telegram.TelegramApiClient;

/// <summary>
/// Telegram answered and the answer was not a success — CARRYING THE STATUS CODE, which is the whole
/// reason this type exists.
///
/// THE CODE WAS NEVER UNAVAILABLE; IT WAS DISCARDED. Every one of these throw sites already had
/// `response.StatusCode` in scope and formatted it into a message string, so the one fact a caller needs
/// to decide what to do next was destroyed at the moment it was known. Callers then had a plain
/// <see cref="Exception"/> and could only ask "did something fail", which is not the question any of
/// them actually has.
///
/// WHAT THAT COST, MEASURED RATHER THAN ARGUED — three findings from three reviewers, each deferred as
/// "a shared-client change for one call site's benefit":
///
///   rev-6 F5   the busy-supervisor narration cleared its message ids on a 429 or a 5xx, because it
///              could not tell them from "the message is gone" — the decision-14 waterfall
///   rev-9 F2   the stated limit read as inherent when it was one typed exception away
///   rev-10 F1  a single 429 recorded a topic name as applied FOR EVER: the app edits topic names every
///              tick so a rate limit is ordinary, and the dictionary that suppresses the retry has no
///              Remove and no Clear anywhere in the solution. The topic then keeps a stale mode glyph
///              until the name changes or the app restarts, and decision 11 makes that glyph the
///              owner-visible truth of a passing state.
///
/// Each deferral was defensible alone. Three of them was evidence that this was the root cause being
/// priced against one symptom at a time.
///
/// A RETRYABLE STATUS IS "WE DO NOT KNOW", NOT "IT FAILED". 429 is Telegram asking us to slow down and
/// every 5xx is Telegram failing to answer for its own reasons — in both cases the request may or may
/// not have taken effect, and both will very likely succeed later. A 4xx that is not 429 is a refusal
/// that will not change until the request does. That distinction is what every caller was trying and
/// failing to make.
///
/// NO STRING PARSING ANYWHERE. Reading a status back out of an English message is a worse defect than
/// the one it would fix — the message is written for a human and nothing stops it changing.
/// </summary>
public sealed class TelegramApiException : Exception
{
    public TelegramApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>The HTTP status Telegram answered with.</summary>
    public int StatusCode { get; }

    /// <summary>
    /// TOO MANY REQUESTS, or Telegram failing on its own side. Both mean the outcome is unknown and a
    /// later attempt is worth making; neither means the request was rejected on its merits.
    /// </summary>
    public bool Is_Retryable => StatusCode == 429 || StatusCode >= 500;
}
