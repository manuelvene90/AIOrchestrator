namespace AIOrchestratorCoreLib.GeneralSupervision.ParkedCloseRequest;

/// <summary>
/// What a tap produced, and WHAT IT WAS ABOUT.
///
/// The request travels back with the outcome because the sentence that replaces the prompt has to name
/// the same thing the prompt named. Returning the outcome alone collapsed the kind away, and every
/// decision sentence then described the orchestration — so a member close that failed told the owner
/// the whole orchestration might be half-closed. The executor reads the request as its first act, so
/// the information was already in hand and was simply being discarded.
///
/// It is NULL only when the request could not be read, which is the one case where nobody can say what
/// the tap was about — and the wording falls back accordingly rather than guessing.
///
/// <para>
/// THE ARCHIVE LABEL TRAVELS TOO (pre-flight ruling E, sibling plan 2026-09-23). <see cref="Outcome"/>
/// says whether the arm RAN to completion, not what it decided: a sibling request re-validated at the
/// tap and refused completes cleanly — outcome Closed — having started nothing. Rendered from the outcome
/// alone that read "✅ Started — you confirmed", the one sentence decision 21 forbids. The label is what
/// the arm actually did ("started", or the refusal's own word), so the sentence can say it. Null where
/// no arm ran (an unreadable request, a decline).
/// </para>
/// </summary>
public readonly record struct CloseTapResult(CloseTapOutcomes Outcome, IParkedCloseRequest? Request, string? ArchiveLabel = null);
