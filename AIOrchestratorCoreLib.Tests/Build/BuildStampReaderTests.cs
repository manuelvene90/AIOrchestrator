using AIOrchestratorCoreLib.Build;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Build;

/// <summary>
/// "AM I RUNNING MY CHANGE?" HAS TO BE ANSWERABLE WITHOUT AN INVESTIGATION. On 2026-08-14 the owner
/// spent an evening reading fixes into an app launched from "bin\Debug - Copia", built at 18:35,
/// while every fix under discussion had gone into bin\Debug hours later. Nothing on screen said so,
/// and the only way to find out was to go and stat the file — which is what this stamp replaces.
///
/// Two facts, because either alone is uninformative: WHERE the running binary lives (the folder name
/// is what exposes a copy) and WHEN it was built.
/// </summary>
public class BuildStampReaderTests : IDisposable
{
    readonly string _tempFolder;

    public BuildStampReaderTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-buildstamp-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        Directory.Delete(_tempFolder, recursive: true);
    }

    string Write_Assembly(string folderName, DateTime builtLocal)
    {
        var folder = Path.Combine(_tempFolder, folderName);
        Directory.CreateDirectory(folder);

        var assemblyPath = Path.Combine(folder, "AIOrchestrator.dll");
        File.WriteAllText(assemblyPath, "not really an assembly");
        File.SetLastWriteTime(assemblyPath, builtLocal);

        return assemblyPath;
    }

    [Fact]
    public void Describe_ABuildFromToday_ShowsTheTimeAndTheFolderItRunsFrom()
    {
        var now = new DateTime(2026, 8, 14, 23, 10, 0);
        var assemblyPath = Write_Assembly("Debug", new DateTime(2026, 8, 14, 22, 43, 0));

        var description = BuildStamp_Reader.Describe(assemblyPath, now);

        Assert.Contains("22:43", description);
        Assert.Contains("Debug", description);
    }

    /// <summary>
    /// THE FOLDER NAME IS THE HALF THAT EXPOSES A STALE COPY, so it is never abbreviated away: "Debug"
    /// and "Debug - Copia" are the same time of day and completely different answers.
    /// </summary>
    [Fact]
    public void Describe_ACopyFolder_NamesItVerbatim()
    {
        var now = new DateTime(2026, 8, 14, 23, 10, 0);
        var assemblyPath = Write_Assembly("Debug - Copia", new DateTime(2026, 8, 14, 18, 35, 0));

        var description = BuildStamp_Reader.Describe(assemblyPath, now);

        Assert.Contains("Debug - Copia", description);
        Assert.Contains("18:35", description);
    }

    /// <summary>
    /// A build from another day must not read as "18:35" and be mistaken for this evening's — the
    /// date is what makes a week-old copy obvious at a glance.
    /// </summary>
    [Fact]
    public void Describe_ABuildFromAnotherDay_CarriesTheDateToo()
    {
        var now = new DateTime(2026, 8, 14, 23, 10, 0);
        var assemblyPath = Write_Assembly("Debug", new DateTime(2026, 8, 11, 18, 35, 0));

        var description = BuildStamp_Reader.Describe(assemblyPath, now);

        Assert.Contains("11", description);
        Assert.Contains("18:35", description);
    }

    /// <summary>
    /// SAYING NOTHING AND SAYING "FRESH" ARE DIFFERENT ANSWERS. A stamp that cannot read its own
    /// binary reports that it could not, rather than rendering a blank the owner reads as fine — the
    /// same rule the hooks follow when they cannot evaluate their predicate.
    /// </summary>
    [Fact]
    public void Describe_AnAssemblyItCannotFind_SaysSo_RatherThanShowingNothing()
    {
        var description = BuildStamp_Reader.Describe(Path.Combine(_tempFolder, "nope", "AIOrchestrator.dll"), DateTime.Now);

        Assert.Contains("unknown", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Describe_RunningApp_AnswersForTheProcessActuallyRunning()
    {
        var description = BuildStamp_Reader.Describe_RunningApp();

        Assert.False(string.IsNullOrWhiteSpace(description));
    }
}
