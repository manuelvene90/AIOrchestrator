using System.Text.RegularExpressions;
using AIOrchestratorCoreLib.Status;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// Proves the brush keys RESOLVE, which "the key is not blank" never did.
///
/// The gap was real: resource lookup went through FindResource, which THROWS on a missing key, so
/// the Brushes.Gray fallback beneath it only ever caught "found, but not a Brush". A state whose key
/// did not exist crashed inside Build_MemberRow — the same position and the same blast radius as the
/// unhandled enum value, every card in every orchestration frozen, re-throwing every 5 seconds.
///
/// The resolver now fails soft, but that alone would turn a crash into a silently grey card. This is
/// the half that keeps the failure LOUD, and it has to live here because `dotnet test` never
/// compiles the WPF project: App.xaml is read as TEXT, which is the only way this project can see
/// across that boundary.
/// </summary>
public class MemberStateBrushKeyTests
{
    [Fact]
    public void EveryBrushKeyAStateCanReturnIsDeclaredInAppXaml()
    {
        var declared = Read_DeclaredResourceKeys();

        // If the file moves or its shape changes, fail rather than pass on an empty set — a test
        // that reads nothing and asserts every member of nothing is the vacuous kind this branch has
        // spent the night removing.
        Assert.True(declared.Count > 5, $"App.xaml gave {declared.Count} keys — the harness is not reading it");

        foreach (var state in Enum.GetValues<MemberStates>())
        {
            var key = MemberState_Descriptor.Brush_Key(state);

            Assert.True(declared.Contains(key), $"{state} maps to brush key '{key}', which App.xaml does not declare");
        }
    }

    static HashSet<string> Read_DeclaredResourceKeys()
    {
        var appXaml = Find_AppXaml_OrNull();

        Assert.NotNull(appXaml);

        HashSet<string> keys = [];

        foreach (Match match in Regex.Matches(File.ReadAllText(appXaml), "x:Key=\"([^\"]+)\""))
            keys.Add(match.Groups[1].Value);

        return keys;
    }

    /// <summary>
    /// Walks up from the test binary to the repo root. The suite runs from bin/Debug/net10.0, and the
    /// WPF project is a sibling of this one — there is no project reference to ask, by design.
    /// </summary>
    static string? Find_AppXaml_OrNull()
    {
        var folder = AppContext.BaseDirectory;

        for (var depth = 0; depth < 8; depth++)
        {
            var candidate = Path.Combine(folder, "AIOrchestrator", "App.xaml");

            if (File.Exists(candidate))
                return candidate;

            var parent = Directory.GetParent(folder);

            if (parent == null)
                return null;

            folder = parent.FullName;
        }

        return null;
    }
}
