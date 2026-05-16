using System.Windows.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    private readonly List<string> _threeDLogLines = [];
    private readonly HashSet<string> _threeDLoggedMeshIssues = new(StringComparer.OrdinalIgnoreCase);
    private TextBox? _threeDLogBox;

    private void LogThreeD(string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss} {message}";
        _threeDLogLines.Add(line);
        while (_threeDLogLines.Count > 80)
            _threeDLogLines.RemoveAt(0);
        RefreshThreeDLogBox();
    }

    private void LogThreeDOnce(string key, string message)
    {
        if (_threeDLoggedMeshIssues.Add(key))
            LogThreeD(message);
    }

    private void RefreshThreeDLogBox()
    {
        if (_threeDLogBox == null)
            return;

        _threeDLogBox.Text = _threeDLogLines.Count == 0
            ? "No 3D log messages yet."
            : string.Join(Environment.NewLine, _threeDLogLines);
        _threeDLogBox.ScrollToEnd();
    }

    private void ClearThreeDMeshIssueLogKeys()
    {
        _threeDLoggedMeshIssues.Clear();
    }
}
