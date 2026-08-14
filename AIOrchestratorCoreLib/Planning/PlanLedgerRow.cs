namespace AIOrchestratorCoreLib.Planning;

/// <summary>
/// One ledger line as the desktop should DRAW it — the glyph, which palette entry colours it, and
/// whether it reads as delivered, active or dropped.
///
/// It carries a brush KEY rather than a brush: the palette lives in the WPF resource dictionary and
/// CoreLib must not reference it. That split is the whole point — the decision is here where a test
/// can reach it, the lookup stays in the window where it belongs.
/// </summary>
public readonly record struct PlanLedgerRow(string Glyph, string BrushKey, string Text, double Opacity, bool IsBold);
