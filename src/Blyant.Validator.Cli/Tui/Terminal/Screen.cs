namespace Blyant.Validator.Cli.Tui.Terminal;

/// <summary>
/// The size the pages are drawn in: the terminal window less its last column and row, and at least 79x23 (a window
/// of 80x24 is enough). Adapted from Blyant's Init.cs (automatic size only).
/// </summary>
internal static class Screen
{
    internal const int MinLineWidth = 79;
    internal const int MinLineCount = 23;
    private const int MaxLineWidth = 1000;
    private const int MaxLineCount = 500;

    internal static int LineWidth { get; private set; } = MinLineWidth;
    internal static int LineCount { get; private set; } = MinLineCount;

    /// <summary>
    /// Set the dimensions from the size of the terminal window. One row and one column are left unused, as writing in
    /// the last column of the window moves the cursor to the next line (and in the bottom right corner makes some
    /// terminals scroll)
    /// </summary>
    /// <returns>True if the dimensions were changed</returns>
    internal static bool ApplyAutoSize()
    {
        int width, height;
        try
        {
            width = Console.WindowWidth - 1;
            height = Console.WindowHeight - 1;
        }
        catch
        {
            // The size of the window is not available: keep the dimensions
            return false;
        }

        width = Math.Clamp(width, MinLineWidth, MaxLineWidth);
        height = Math.Clamp(height, MinLineCount, MaxLineCount);
        if (width == LineWidth && height == LineCount) return false;
        LineWidth = width;
        LineCount = height;
        return true;
    }
}
