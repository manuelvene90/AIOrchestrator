namespace AIOrchestratorCoreLib.Layout;

/// <summary>
/// Splits a screen area into equal tiles for N windows. Pure geometry so the arrangement can be
/// tested without touching a single window: the column count is CHOSEN, not fixed, by scoring
/// every candidate on how close its tiles land to a comfortable reading shape — three terminals on
/// a wide monitor want three columns, four want a 2×2.
/// </summary>
public static class TileLayout_Calculator
{
    /// <summary>
    /// Terminals read best clearly wider than tall. Tuned so the common counts land on the layouts
    /// a person would draw by hand: 2 → side by side, 3 → two over one, 4 → a 2×2, 6 → 3×2.
    /// </summary>
    const double TARGET_TILE_ASPECT = 1.6;

    public static IReadOnlyList<(int X, int Y, int Width, int Height)> Build_Tiles(
        int count,
        int areaX,
        int areaY,
        int areaWidth,
        int areaHeight)
    {
        List<(int X, int Y, int Width, int Height)> tiles = [];

        if (count <= 0 || areaWidth <= 0 || areaHeight <= 0)
            return tiles;

        var columns = Pick_ColumnCount(count, areaWidth, areaHeight);
        var rows = (count + columns - 1) / columns;
        var rowHeight = areaHeight / rows;

        for (var row = 0; row < rows; row++)
        {
            // The last row usually holds fewer windows — they STRETCH to fill it, so the screen is
            // fully used instead of leaving a hole.
            var windowsInRow = Math.Min(columns, count - (row * columns));
            var tileWidth = areaWidth / windowsInRow;

            for (var column = 0; column < windowsInRow; column++)
            {
                // The last tile of a row/column absorbs the integer-division remainder.
                var width = column == windowsInRow - 1 ? areaWidth - (tileWidth * column) : tileWidth;
                var height = row == rows - 1 ? areaHeight - (rowHeight * row) : rowHeight;

                tiles.Add((areaX + (tileWidth * column), areaY + (rowHeight * row), width, height));
            }
        }

        return tiles;
    }

    static int Pick_ColumnCount(int count, int areaWidth, int areaHeight)
    {
        var bestColumns = 1;
        var bestScore = double.MaxValue;

        for (var columns = 1; columns <= count; columns++)
        {
            var rows = (count + columns - 1) / columns;
            var tileAspect = (double)areaWidth / columns / ((double)areaHeight / rows);

            // Ratio distance, so "twice as wide as wanted" and "half as wide" score alike.
            var score = Math.Abs(Math.Log(tileAspect / TARGET_TILE_ASPECT));

            if (score < bestScore)
            {
                bestScore = score;
                bestColumns = columns;
            }
        }

        return bestColumns;
    }
}
