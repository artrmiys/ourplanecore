using System;
using System.Globalization;
using System.Windows.Data;

namespace OurPlaneCore.Controls;

/// <summary>
/// Groups whose names start with "★" (Pinned) or "🕒" (Recent) start expanded;
/// "📁 From …" and "⚠ Unavailable" start collapsed.
/// </summary>
public sealed class GroupNameToIsExpandedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string name = value as string ?? "";
        return name.StartsWith("★", StringComparison.Ordinal) ||
               name.StartsWith("🕒", StringComparison.Ordinal);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
