using System;
using System.Windows.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private static MenuItem MakeMenuItem(string header, bool isEnabled, Action action)
    {
        var item = new MenuItem { Header = header, IsEnabled = isEnabled };
        item.Click += (_, _) => action();
        return item;
    }

    private static MenuItem MakeSubmenu(string header, params object[] children)
    {
        var item = new MenuItem { Header = header };
        foreach (object child in children)
            item.Items.Add(child);
        return item;
    }

    // Non-clickable caption used to label a section inside a context menu.
    private static MenuItem MakeMenuHeader(string text)
    {
        return new MenuItem
        {
            Header = text,
            IsEnabled = false,
            FontWeight = System.Windows.FontWeights.SemiBold,
            FontSize = 10.5,
        };
    }
}
