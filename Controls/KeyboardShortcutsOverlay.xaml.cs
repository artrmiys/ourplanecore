using System.Windows.Controls;

namespace OurPlanCore.Controls;

public partial class KeyboardShortcutsOverlay : UserControl
{
    public KeyboardShortcutsOverlay()
    {
        InitializeComponent();
        DataContext = new { Columns = KeyboardShortcutCatalog.Columns };
    }
}
