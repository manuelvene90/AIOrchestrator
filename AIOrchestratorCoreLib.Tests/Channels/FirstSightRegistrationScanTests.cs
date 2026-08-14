using Xunit;

namespace AIOrchestratorCoreLib.Tests.Channels;

/// <summary>
/// FIRST SIGHT IS REGISTERED IN EXACTLY ONE PLACE — the property the behaviour tests cannot see.
///
/// <para>
/// It was registered three times: the baseline pass and each of the two sweeps kept a set of its own.
/// Every pair of those left a window — a channel one consumer had seen and another had not — and an
/// offence arriving inside it was absorbed as history by whichever got there second, into memos that
/// never release. The entry sits on disk, its writer believes it visible, and nothing will ever report
/// it (rev-10 F1, and the residual rev-9 named on top of it).
/// </para>
/// <para>
/// WHY A SCAN AND NOT A BEHAVIOUR TEST. A revert that gives each sweep its own registration back leaves
/// every probe on this branch GREEN — that is exactly how the defect survived four reviewers. The
/// property is structural, so only the source can answer it: one registration, in the method that also
/// seeds both memos, because a single registration is what forces a single absorption.
/// </para>
/// <para>
/// IT REFUSES TO RUN IF IT CANNOT FIND THE ENGINE, for the same reason as
/// <see cref="MemoKeyCompositionScanTests"/>: every assertion here is about absence, and a scan that
/// read nothing would be the strongest possible pass while meaning nothing at all (decision 20).
/// </para>
/// </summary>
public class FirstSightRegistrationScanTests
{
    const string ENGINE_FILE = "BridgeEngineModel.cs";
    const string FIRST_SIGHT_SET = "_channelsFirstSighted";
    const string REGISTRATION = "_channelsFirstSighted.Add(";

    /// <summary>The method that registers sight and seeds both memos, which is the only place allowed to.</summary>
    const string REGISTERING_METHOD = "void Apply_Baselines(";

    [Fact]
    public void FirstSightIsRegisteredInExactlyOnePlace_AndItIsTheMethodThatSeedsBothMemos()
    {
        var engineSource = Read_EngineSource();

        // THE HARNESS PROVES ITSELF FIRST: a scan that read an empty string would pass every count
        // assertion below by finding zero of everything.
        Assert.True(engineSource.Length > 10_000, $"the scan read {engineSource.Length} characters — that is not {ENGINE_FILE}");
        Assert.Contains(FIRST_SIGHT_SET, engineSource);
        Assert.Contains(REGISTERING_METHOD, engineSource);

        var lines = engineSource.Split('\n');

        List<string> registrations = [];

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(REGISTRATION))
                registrations.Add($"{i + 1}: {lines[i].Trim()}");
        }

        Assert.Single(registrations);

        // AND IT IS IN THE RIGHT PLACE. One registration in the wrong method — inside a sweep, say —
        // would satisfy the count while restoring the defect, because that sweep would then take
        // sight without the other consumer's memo being seeded.
        var registrationLine = int.Parse(registrations[0].Split(':')[0]);
        var methodLine = Line_Of(lines, REGISTERING_METHOD);
        var nextMethodLine = Next_MethodAfter(lines, methodLine);

        Assert.True(
            registrationLine > methodLine && registrationLine < nextMethodLine,
            $"first sight is registered at line {registrationLine}, outside {REGISTERING_METHOD} (lines {methodLine}-{nextMethodLine})");
    }

    static int Line_Of(string[] lines, string fragment)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(fragment))
                return i + 1;
        }

        throw new Exception($"'{fragment}' is not in {ENGINE_FILE} — the scan cannot locate what it is testing");
    }

    /// <summary>The next method signature at class level, which bounds the one we are inside.</summary>
    static int Next_MethodAfter(string[] lines, int methodLine)
    {
        for (var i = methodLine; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("    }"))
                return i + 1;
        }

        return lines.Length;
    }

    static string Read_EngineSource()
    {
        var folder = AppContext.BaseDirectory;

        for (var depth = 0; depth < 8; depth++)
        {
            var candidate = Path.Combine(folder, "AIOrchestratorCoreLib", "Bridge", "BridgeEngine", ENGINE_FILE);

            if (File.Exists(candidate))
                return File.ReadAllText(candidate);

            var parent = Directory.GetParent(folder);

            if (parent == null)
                break;

            folder = parent.FullName;
        }

        return string.Empty;
    }
}
