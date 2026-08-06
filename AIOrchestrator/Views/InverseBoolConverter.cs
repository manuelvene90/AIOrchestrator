using System.Globalization;
using System.Windows.Data;

namespace AIOrchestrator.Views;

/// <summary>Inverts a bool for IsEnabled bindings (e.g. disable buttons on closed orchestrations).</summary>
public class InverseBoolConverter : IValueConverter
{
    public static readonly InverseBoolConverter INSTANCE = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return !boolValue;

        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException("InverseBoolConverter is one-way");
    }
}
