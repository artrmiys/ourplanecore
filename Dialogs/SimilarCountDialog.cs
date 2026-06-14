using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace OurPlaneCore.Controls;

// Threshold tuner for offline "count similar symbols". The scan callback
// re-runs the matcher and refreshes the viewport ghost preview; the dialog
// only owns the controls, debouncing and cancellation of stale scans.
public sealed class SimilarCountDialog : Window
{
    public float Threshold { get; private set; }
    public bool IncludeRotations { get; private set; }
    public bool IncludeMirrored { get; private set; }
    public bool QueueAiDoubleCheck { get; private set; }
    public event EventHandler? Accepted;
    public event EventHandler? Cancelled;

    private readonly Func<float, bool, bool, CancellationToken, Task<int>> _scan;
    private readonly Slider _thresholdSlider;
    private readonly CheckBox _rotationsBox;
    private readonly CheckBox _mirroredBox;
    private readonly CheckBox _aiBox;
    private readonly TextBlock _foundLabel;
    private readonly TextBlock _thresholdLabel;
    private readonly Button _addButton;
    private readonly DispatcherTimer _debounce;
    private CancellationTokenSource? _scanCts;
    private int _lastFound;
    private int _lastTotal;
    private bool _accepted;

    public SimilarCountDialog(
        Func<float, bool, bool, CancellationToken, Task<int>> scan,
        float initialThreshold,
        bool initialRotations,
        bool initialMirrored,
        bool aiAvailable)
    {
        _scan = scan;
        Title = "Count Similar Symbols";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(14) };
        Content = panel;

        _foundLabel = new TextBlock
        {
            Text = "Scanning...",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
        };
        panel.Children.Add(_foundLabel);

        _thresholdLabel = new TextBlock { Margin = new Thickness(0, 0, 0, 2) };
        panel.Children.Add(_thresholdLabel);
        _thresholdSlider = new Slider
        {
            Minimum = AppSettingsStore.SimilarCountThresholdMin,
            Maximum = AppSettingsStore.SimilarCountThresholdMax,
            Value = Math.Clamp(
                initialThreshold,
                (float)AppSettingsStore.SimilarCountThresholdMin,
                (float)AppSettingsStore.SimilarCountThresholdMax),
            TickFrequency = 0.05,
            IsSnapToTickEnabled = false,
            Margin = new Thickness(0, 0, 0, 8),
        };
        panel.Children.Add(_thresholdSlider);

        _rotationsBox = new CheckBox
        {
            Content = "Match rotated copies (90° / 180° / 270°)",
            IsChecked = initialRotations,
            Margin = new Thickness(0, 0, 0, 8),
        };
        panel.Children.Add(_rotationsBox);

        _mirroredBox = new CheckBox
        {
            Content = "Match mirrored copies",
            IsChecked = initialMirrored,
            Margin = new Thickness(0, 0, 0, 8),
        };
        panel.Children.Add(_mirroredBox);

        _aiBox = new CheckBox
        {
            Content = "Also ask AI to double-check (online)",
            IsChecked = false,
            IsEnabled = aiAvailable,
            ToolTip = aiAvailable
                ? "Queues an OpenAI request with the symbol crop; the answer lands in the AI Inbox."
                : "OPENAI_API_KEY is not set; offline matching still works.",
            Margin = new Thickness(0, 0, 0, 10),
        };
        panel.Children.Add(_aiBox);

        var hint = new TextBlock
        {
            Text = "Matches preview as blue ghosts on the sheet. Lower the threshold to find more, raise it to drop false hits.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
            Margin = new Thickness(0, 0, 0, 10),
        };
        panel.Children.Add(hint);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _addButton = new Button
        {
            Content = "Add Markers",
            MinWidth = 96,
            IsDefault = true,
            IsEnabled = false,
            Margin = new Thickness(0, 0, 6, 0),
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true };
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(_addButton);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _ = RunScanAsync();
        };

        _thresholdSlider.ValueChanged += (_, _) =>
        {
            if (!IsInitialized)
                return;
            UpdateThresholdLabel();
            _debounce.Stop();
            _debounce.Start();
        };
        _rotationsBox.Checked += (_, _) => _ = RunScanAsync();
        _rotationsBox.Unchecked += (_, _) => _ = RunScanAsync();
        _mirroredBox.Checked += (_, _) => _ = RunScanAsync();
        _mirroredBox.Unchecked += (_, _) => _ = RunScanAsync();

        _addButton.Click += (_, _) =>
        {
            Threshold = (float)_thresholdSlider.Value;
            IncludeRotations = _rotationsBox.IsChecked == true;
            IncludeMirrored = _mirroredBox.IsChecked == true;
            QueueAiDoubleCheck = _aiBox.IsChecked == true;
            _accepted = true;
            Accepted?.Invoke(this, EventArgs.Empty);
            Close();
        };

        Loaded += (_, _) =>
        {
            // Sit top-left over the owner so the sheet (and the ghost
            // preview) stays visible while tuning.
            if (Owner != null)
            {
                Left = Owner.Left + 90;
                Top = Owner.Top + 140;
            }
            UpdateThresholdLabel();
            _ = RunScanAsync();
        };
        Closed += (_, _) =>
        {
            _debounce.Stop();
            _scanCts?.Cancel();
            if (!_accepted)
                Cancelled?.Invoke(this, EventArgs.Empty);
        };
    }

    private void UpdateThresholdLabel() =>
        _thresholdLabel.Text = $"Similarity threshold: {_thresholdSlider.Value:0.00} (precision default {AppSettingsStore.SimilarCountThresholdDefault:0.00})";

    public void SetReviewCounts(int included, int total)
    {
        _lastFound = Math.Max(0, included);
        _lastTotal = Math.Max(0, total);

        if (_lastTotal == 0)
        {
            _foundLabel.Text = "Found 0 symbols.";
            _addButton.Content = "Add Markers";
            _addButton.IsEnabled = false;
            return;
        }

        if (_lastFound == _lastTotal)
        {
            _foundLabel.Text = _lastFound == 1 ? "Found 1 symbol." : $"Found {_lastFound} symbols.";
            _addButton.Content = "Add Markers";
        }
        else
        {
            _foundLabel.Text = $"Included {_lastFound} of {_lastTotal} symbols.";
            _addButton.Content = $"Add {_lastFound}";
        }

        _addButton.IsEnabled = _lastFound > 0;
    }

    private async Task RunScanAsync()
    {
        _scanCts?.Cancel();
        var cts = new CancellationTokenSource();
        _scanCts = cts;
        _foundLabel.Text = "Scanning...";
        _addButton.IsEnabled = false;
        try
        {
            int found = await _scan(
                (float)_thresholdSlider.Value,
                _rotationsBox.IsChecked == true,
                _mirroredBox.IsChecked == true,
                cts.Token);
            if (cts.IsCancellationRequested)
                return;

            SetReviewCounts(found, found);
        }
        catch (OperationCanceledException)
        {
            // A newer scan superseded this one.
        }
        catch (Exception ex)
        {
            _foundLabel.Text = $"Scan failed: {ex.Message}";
            AppLog.Warn(ex, "Similar count scan failed.");
        }
    }
}
