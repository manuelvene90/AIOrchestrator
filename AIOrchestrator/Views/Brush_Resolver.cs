using System.Windows;
using System.Windows.Media;

namespace AIOrchestrator.Views;

/// <summary>
/// Resource lookup that CANNOT raise. There were two identical copies of this — one per window,
/// item 12 — and both called <c>FindResource</c>, which THROWS on a missing key. The
/// <c>Brushes.Gray</c> line beneath it therefore only ever caught "found, but not a Brush"; the case
/// it looked like it was guarding against, a key that does not exist, went straight past it as an
/// exception.
///
/// That matters because of WHERE it is called. Build_MemberRow runs inside the refresh that assigns
/// ItemsSource as its last statement, so one unresolvable key stops every card in every
/// orchestration from updating, re-throwing every 5 seconds — the identical blast radius as the
/// unhandled enum value, from the identical position.
///
/// A wrong colour is a cosmetic defect. A dead dashboard is not, and the app cannot tell the owner
/// anything once it stops refreshing. So this fails soft, and MemberStateBrushKeyTests independently
/// asserts that every key the descriptor can return is actually declared in App.xaml — which is the
/// evidence this file cannot provide for itself, since the test suite never compiles this project.
/// </summary>
public static class Brush_Resolver
{
    public static Brush Find_OrFallback(FrameworkElement element, string resourceKey)
    {
        // TryFindResource returns null where FindResource throws. That is the entire fix.
        if (element.TryFindResource(resourceKey) is Brush brush)
            return brush;

        return Brushes.Gray;
    }
}
