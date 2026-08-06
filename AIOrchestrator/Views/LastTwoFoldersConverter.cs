using System.Globalization;
using System.Windows.Data;

namespace AIOrchestrator.Views;

/// <summary>Shows only the last two folders of a path (full path stays in the tooltip).</summary>
public class LastTwoFoldersConverter : IValueConverter
{
    public static readonly LastTwoFoldersConverter INSTANCE = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || path.Length == 0)
            return string.Empty;

        var segments = path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length <= 2)
            return path;

        return $"{segments[^2]}\\{segments[^1]}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException("LastTwoFoldersConverter is one-way");
    }
}
