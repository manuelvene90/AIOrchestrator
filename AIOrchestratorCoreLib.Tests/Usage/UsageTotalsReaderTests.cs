using AIOrchestratorCoreLib.Usage;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Usage;

/// <summary>
/// Guards the figures behind the cards, the detail window and the /tokens command — all three
/// now read through this one component.
/// </summary>
public class UsageTotalsReaderTests : IDisposable
{
    readonly string _tempFolder;

    public UsageTotalsReaderTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"aiorch-usage-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        Directory.Delete(_tempFolder, recursive: true);
    }

    [Fact]
    public void Read_Tokens_SumsEveryTokenFieldShape_WhereverItSitsInThePayload()
    {
        var file = Write_UsageFile("""
            {
              "model": { "display_name": "Opus" },
              "cost": { "total_cost_usd": 1.25 },
              "usage": {
                "input_tokens": 100,
                "output_tokens": 20,
                "cache_creation_input_tokens": 5,
                "cache_read_input_tokens": 7,
                "nested": { "total_output_tokens": 3 },
                "not_a_token_field": 9999
              }
            }
            """);

        Assert.Equal(135, UsageTotals_Reader.Read_Tokens_OrNull(file));
        Assert.Equal(1.25, UsageTotals_Reader.Read_Cost_OrNull(file));
    }

    [Fact]
    public void Read_MissingOrGarbageFile_ReturnsNull_NeverThrows()
    {
        var missing = Path.Combine(_tempFolder, "absent.usage.json");
        var garbage = Write_UsageFile("not json at all");

        Assert.Null(UsageTotals_Reader.Read_Tokens_OrNull(missing));
        Assert.Null(UsageTotals_Reader.Read_Cost_OrNull(missing));
        Assert.Null(UsageTotals_Reader.Read_Tokens_OrNull(garbage));
        Assert.Null(UsageTotals_Reader.Read_Cost_OrNull(garbage));
    }

    [Theory]
    [InlineData(999, "999 tok")]
    [InlineData(1_500, "1.5k tok")]
    [InlineData(2_400_000, "2.4M tok")]
    public void Format_Tokens_ScalesTheUnit(long tokens, string expected)
    {
        Assert.Equal(expected, UsageTotals_Reader.Format_Tokens(tokens));
    }

    string Write_UsageFile(string content)
    {
        var path = Path.Combine(_tempFolder, $"{Guid.NewGuid():N}.usage.json");
        File.WriteAllText(path, content);
        return path;
    }
}
