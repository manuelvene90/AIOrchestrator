using AIOrchestratorCoreLib.Layout;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Layout;

/// <summary>
/// The "Organize" tiling. The owner asked for the screen to be filled in equal parts — so the
/// tiles must COVER the area exactly, with no gaps and no overlaps, whatever the window count.
/// </summary>
public class TileLayoutCalculatorTests
{
    const int AREA_X = 0;
    const int AREA_Y = 0;
    const int AREA_WIDTH = 1920;
    const int AREA_HEIGHT = 1040;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(9)]
    public void Build_Tiles_CoversTheWholeAreaExactly_WithoutOverlaps(int count)
    {
        var tiles = TileLayout_Calculator.Build_Tiles(count, AREA_X, AREA_Y, AREA_WIDTH, AREA_HEIGHT);

        Assert.Equal(count, tiles.Count);

        var coveredPixels = 0L;

        foreach (var tile in tiles)
        {
            Assert.True(tile.Width > 0 && tile.Height > 0, "every window must get a usable tile");
            Assert.True(tile.X >= AREA_X && tile.Y >= AREA_Y, "tiles stay inside the work area");
            Assert.True(tile.X + tile.Width <= AREA_X + AREA_WIDTH, "tiles never spill off the right edge");
            Assert.True(tile.Y + tile.Height <= AREA_Y + AREA_HEIGHT, "tiles never spill off the bottom edge");

            coveredPixels += (long)tile.Width * tile.Height;
        }

        // Exact coverage proves no gaps AND no overlaps at once (overlap would exceed the area).
        Assert.Equal((long)AREA_WIDTH * AREA_HEIGHT, coveredPixels);
    }

    [Fact]
    public void Build_Tiles_One_FillsTheScreen()
    {
        var tiles = TileLayout_Calculator.Build_Tiles(1, 0, 0, AREA_WIDTH, AREA_HEIGHT);

        Assert.Equal((0, 0, AREA_WIDTH, AREA_HEIGHT), Assert.Single(tiles));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Build_Tiles_TwoOrThree_AreSideBySideColumns(int count)
    {
        var tiles = TileLayout_Calculator.Build_Tiles(count, 0, 0, AREA_WIDTH, AREA_HEIGHT);

        Assert.Single(tiles.Select(tile => tile.Y).Distinct());
        Assert.Equal(count, tiles.Select(tile => tile.X).Distinct().Count());
        Assert.All(tiles, tile => Assert.Equal(AREA_HEIGHT, tile.Height));
    }

    [Fact]
    public void Build_Tiles_Four_IsATwoByTwo()
    {
        var tiles = TileLayout_Calculator.Build_Tiles(4, 0, 0, AREA_WIDTH, AREA_HEIGHT);

        Assert.Equal(2, tiles.Select(tile => tile.X).Distinct().Count());
        Assert.Equal(2, tiles.Select(tile => tile.Y).Distinct().Count());
    }

    [Fact]
    public void Build_Tiles_Five_IsTheSupervisorsColumnPlusATwoByTwo()
    {
        var tiles = TileLayout_Calculator.Build_Tiles(5, 0, 0, AREA_WIDTH, AREA_HEIGHT);

        // The FIRST tile is the supervisor's: its own full-height column on the left.
        var supervisor = tiles[0];
        Assert.Equal(0, supervisor.X);
        Assert.Equal(AREA_HEIGHT, supervisor.Height);
        Assert.Equal(AREA_WIDTH / 3, supervisor.Width);

        // The four implementers form a 2x2 in the remaining two thirds — same width as the
        // supervisor's column, so the screen reads as three even columns.
        var implementers = tiles.Skip(1).ToList();
        Assert.Equal(2, implementers.Select(tile => tile.X).Distinct().Count());
        Assert.Equal(2, implementers.Select(tile => tile.Y).Distinct().Count());
        Assert.Equal(supervisor.Width, implementers[0].Width);
    }

    [Fact]
    public void Build_Tiles_Seven_KeepsTheSupervisorsColumn_AndSpreadsTheRestOverTwoRows()
    {
        var tiles = TileLayout_Calculator.Build_Tiles(7, 0, 0, AREA_WIDTH, AREA_HEIGHT);

        Assert.Equal(AREA_HEIGHT, tiles[0].Height);
        Assert.Equal(3, tiles.Skip(1).Select(tile => tile.X).Distinct().Count());
        Assert.Equal(2, tiles.Skip(1).Select(tile => tile.Y).Distinct().Count());
    }

    [Fact]
    public void Build_Tiles_RespectsAWorkAreaOffset()
    {
        var tiles = TileLayout_Calculator.Build_Tiles(2, 100, 50, 800, 600);

        Assert.All(tiles, tile => Assert.True(tile.X >= 100 && tile.Y >= 50));
        Assert.Equal(900, tiles[^1].X + tiles[^1].Width);
    }

    [Fact]
    public void Build_Tiles_NothingToTile_ReturnsEmpty()
    {
        Assert.Empty(TileLayout_Calculator.Build_Tiles(0, 0, 0, AREA_WIDTH, AREA_HEIGHT));
        Assert.Empty(TileLayout_Calculator.Build_Tiles(3, 0, 0, 0, 0));
    }
}
