using System.Windows;

namespace OurPlanCore;

public partial class MainWindow
{
    private string? _repeatDrawingTool;

    private void BtnRepeatDrawing_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tool })
            return;

        if (_repeatDrawingTool == tool)
            SetTool("select");
        else
            SetTool(tool, forceNewTakeoff: IsRecordTool(tool), repeatDrawing: true);
    }

    private void SyncRepeatDrawingButtons()
    {
        BtnLineRepeat.IsChecked = _repeatDrawingTool == "line";
        BtnBeamRepeat.IsChecked = _repeatDrawingTool == "beam";
    }
}
