using AIOrchestratorCoreLib.Formatting;
using AIOrchestratorCoreLib.Status.SessionModelReading;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Formatting;

/// <summary>
/// One wording for a model-and-effort reading, on every surface. Item 12 of the project decisions:
/// never a second copy of a formatter — the pulse's lead line and its member rows must say the same
/// thing about the same session, and so must anything that quotes the dial later.
/// </summary>
public class ModelReadingFormatterTests
{
    [Fact]
    public void ItReadsAsTheFieldTheSurfacesAppend()
    {
        Assert.Equal("Fable 5.1 xhigh", ModelReading_Formatter.Describe(Reading("Fable 5.1", "xhigh")));
    }

    /// <summary>
    /// An unknown effort leaves JUST the model, with nothing trailing: no placeholder, no dangling
    /// space. An older Claude Code and a model without the dial both land here, and "Opus 5 ?" would
    /// read as a reading that failed rather than a dial that does not exist.
    /// </summary>
    [Fact]
    public void AnUnknownEffortLeavesJustTheModel()
    {
        Assert.Equal("Opus 5", ModelReading_Formatter.Describe(Reading("Opus 5", null)));
    }

    /// <summary>
    /// No reading means NO FIELD, so a surface drops it rather than printing an empty one. A row
    /// that ends in a dangling separator reads as a value that failed to load, when the truth is
    /// that the session has not reported one.
    /// </summary>
    [Fact]
    public void NoReadingProducesNoField()
    {
        Assert.Null(ModelReading_Formatter.Describe_OrNull(null));
    }

    [Fact]
    public void TheNullableFormOfTheSameReadingSaysTheSameThing()
    {
        Assert.Equal(
            ModelReading_Formatter.Describe(Reading("Fable 5.1", "xhigh")),
            ModelReading_Formatter.Describe_OrNull(Reading("Fable 5.1", "xhigh")));
    }

    static ISessionModelReading Reading(string model, string? effort)
    {
        return SessionModelReading_Factory.Create(model, effort);
    }
}
