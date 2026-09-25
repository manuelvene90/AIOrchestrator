using AIOrchestratorCoreLib.Bridge.TopicNaming;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.TopicNaming;

public class TopicRenameReporterTests
{
    [Fact]
    public void Record_AFirstRename_IsNews()
    {
        var reporter = TopicRenameReporter_Factory.Create();

        Assert.Equal("renamed topic 7 to 'a name'", reporter.Record_OrNull("o", 7, "a name", TopicRenameAnswers.Renamed));
    }

    [Fact]
    public void Record_TheSameOutcomeAgain_IsSilent()
    {
        var reporter = TopicRenameReporter_Factory.Create();
        reporter.Record_OrNull("o", 7, "a name", TopicRenameAnswers.AlreadyNamed);

        Assert.Null(reporter.Record_OrNull("o", 7, "a name", TopicRenameAnswers.AlreadyNamed));
    }

    [Fact]
    public void Record_ADifferentAnswerForTheSameName_IsNews()
    {
        var reporter = TopicRenameReporter_Factory.Create();
        reporter.Record_OrNull("o", 7, "a name", TopicRenameAnswers.Renamed);

        Assert.Equal("topic 7 already named 'a name'", reporter.Record_OrNull("o", 7, "a name", TopicRenameAnswers.AlreadyNamed));
    }

    [Fact]
    public void Record_ANewNameOrANewTopic_IsNews()
    {
        var reporter = TopicRenameReporter_Factory.Create();
        reporter.Record_OrNull("o", 7, "a name", TopicRenameAnswers.Renamed);

        Assert.NotNull(reporter.Record_OrNull("o", 7, "another name", TopicRenameAnswers.Renamed));
        Assert.NotNull(reporter.Record_OrNull("o", 8, "another name", TopicRenameAnswers.Renamed));
    }

    [Fact]
    public void Record_IsPerOrchestration()
    {
        var reporter = TopicRenameReporter_Factory.Create();
        reporter.Record_OrNull("o1", 7, "a name", TopicRenameAnswers.Renamed);

        Assert.NotNull(reporter.Record_OrNull("o2", 7, "a name", TopicRenameAnswers.Renamed));
    }

    [Fact]
    public void RememberCreated_MakesAnIdenticalRenameSilent_ButNotANotModified()
    {
        var reporter = TopicRenameReporter_Factory.Create();
        reporter.Remember_Created("o", 7, "a name");

        Assert.Null(reporter.Record_OrNull("o", 7, "a name", TopicRenameAnswers.Renamed));
        Assert.Equal((7L, "a name", TopicRenameAnswers.Renamed), reporter.Last_OrNull("o"));
        Assert.NotNull(reporter.Record_OrNull("o", 7, "a name", TopicRenameAnswers.AlreadyNamed));
    }

    [Fact]
    public void LastOrNull_UnknownOrchestration_IsNull()
    {
        Assert.Null(TopicRenameReporter_Factory.Create().Last_OrNull("o"));
    }
}
