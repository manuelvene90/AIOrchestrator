namespace AIOrchestratorCoreLib.Layout;

/// <summary>
/// Splits a screen area into tiles for the "Organize" button. The layouts are PRESCRIBED, not
/// derived from a scoring heuristic, because the owner knows what they want to look at:
///
///   1 → full screen
///   2 → two columns
///   3 → three columns
///   4 → 2×2
///   5+ → the FIRST window (the supervisor) takes a full-height column of its own, and the rest
///        tile in the remaining area, two rows deep — so five reads as "supervisor + a 2×2 of
///        implementers", with every tile the same width.
///
/// Pure geometry: the arrangement is fully testable without touching a window.
/// </summary>
public static class TileLayout_Calculator
{
    /// <summary>Above this count the supervisor stops sharing the grid and gets its own column.</summary>
    const int SUPERVISOR_COLUMN_FROM = 5;

    /// <summary>Rows the non-supervisor windows are laid out in once the supervisor has its column.</summary>
    const int IMPLEMENTER_ROWS = 2;

    public static IReadOnlyList<(int X, int Y, int Width, int Height)> Build_Tiles(
        int count,
        int areaX,
        int areaY,
        int areaWidth,
        int areaHeight)
    {
        if (count <= 0 || areaWidth <= 0 || areaHeight <= 0)
            return [];

        if (count < SUPERVISOR_COLUMN_FROM)
            return Build_Grid(count, areaX, areaY, areaWidth, areaHeight, Pick_SmallLayoutColumns(count));

        // The supervisor's column is exactly as wide as one tile of the grid beside it, so the
        // whole screen reads as one even set of columns.
        var implementerColumns = (count - 1 + IMPLEMENTER_ROWS - 1) / IMPLEMENTER_ROWS;
        var totalColumns = implementerColumns + 1;
        var supervisorWidth = areaWidth / totalColumns;

        List<(int X, int Y, int Width, int Height)> tiles = [(areaX, areaY, supervisorWidth, areaHeight)];

        foreach (var tile in Build_Grid(count - 1, areaX + supervisorWidth, areaY, areaWidth - supervisorWidth, areaHeight, implementerColumns))
            tiles.Add(tile);

        return tiles;
    }

    /// <summary>1 → 1 column, 2 → 2, 3 → 3 (side by side), 4 → 2 (a 2×2).</summary>
    static int Pick_SmallLayoutColumns(int count)
    {
        return count == 4 ? 2 : count;
    }

    static IReadOnlyList<(int X, int Y, int Width, int Height)> Build_Grid(
        int count,
        int areaX,
        int areaY,
        int areaWidth,
        int areaHeight,
        int columns)
    {
        List<(int X, int Y, int Width, int Height)> tiles = [];

        var rows = (count + columns - 1) / columns;
        var rowHeight = areaHeight / rows;

        for (var row = 0; row < rows; row++)
        {
            // A short last row STRETCHES to fill its width, so the screen is never left with a hole.
            var windowsInRow = Math.Min(columns, count - (row * columns));
            var tileWidth = areaWidth / windowsInRow;

            for (var column = 0; column < windowsInRow; column++)
            {
                // The last tile of a row (and of a column) absorbs the integer-division remainder.
                var width = column == windowsInRow - 1 ? areaWidth - (tileWidth * column) : tileWidth;
                var height = row == rows - 1 ? areaHeight - (rowHeight * row) : rowHeight;

                tiles.Add((areaX + (tileWidth * column), areaY + (rowHeight * row), width, height));
            }
        }

        return tiles;
    }
}
