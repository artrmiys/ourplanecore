using System;
using System.Globalization;
using System.Windows.Data;

namespace OurPlaneCore.Controls;

/// <summary>
/// Pinned and Recent groups start expanded; source-folder and unavailable groups start collapsed.
/// </summary>
public sealed class GroupNameToIsExpandedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string name = value as string ?? "";
        return name.StartsWith("Pinned", StringComparison.Ordinal) ||
               name.StartsWith("Recent", StringComparison.Ordinal);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
