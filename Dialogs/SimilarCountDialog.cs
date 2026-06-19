using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace OurPlaneCore.Controls;

public readonly record struct SimilarCountScanResult(
    int Included,
    int Total,
    float MinScore,
    float MaxScore,
    int WeakCount = 0,
    int AlreadyCountedCount = 0,
    string LimitSummary = "",
    int OtherSheetNewCount = 0,
    int OtherSheetCount = 0,
    string OtherSheetSummary = "")
{
    public int NewCandidateCount => Math.Max(0, Total - AlreadyCountedCount);
    public int ExcludedNewCount => Math.Max(0, NewCandidateCount - Included);
}

// Threshold tuner for offline "count similar symbols". The scan callback
// re-runs the matcher and refreshes the viewport ghost preview; the dialog
// only owns the controls, debouncing and cancellation of stale scans.
public sealed class SimilarCountDialog : Window
{
    private const double StrictThresholdPreset = 0.98;
    private const double LooseThresholdPreset = 0.82;

    public float Threshold { get; private set; }
    public bool IncludeRotations { get; private set; }
    public bool IncludeMirrored { get; private set; }
    public bool IncludeAllSheets { get; private set; }
    public bool QueueAiDoubleCheck { get; private set; }
    public event EventHandler? Accepted;
    public event EventHandler? Cancelled;
    public event EventHandler? IncludeAllRequested;
    public event EventHandler? StrongOnlyRequested;

    private readonly Func<float, bool, bool, bool, CancellationToken, Task<SimilarCountScanResult>> _scan;
    private readonly Slider _thresholdSlider;
    private readonly CheckBox _rotationsBox;
    private readonly CheckBox _mirroredBox;
    private readonly CheckBox _allSheetsBox;
    private readonly CheckBox _aiBox;
    private readonly TextBlock _foundLabel;
    private readonly TextBlock _reviewDetailsLabel;
    private readonly TextBlock _otherSheetsLabel;
    private readonly TextBlock _thresholdLabel;
    private readonly Button _strictButton;
    private readonly Button _defaultButton;
    private readonly Button _looseButton;
    private readonly Button _includeAllButton;
    private readonly Button _strongOnlyButton;
    private readonly Button _addButton;
    private readonly string _destinationName;
    private readonly DispatcherTimer _debounce;
    private CancellationTokenSource? _scanCts;
    private int _lastFound;
    private int _lastTotal;
    private float _lastMinScore;
    private float _lastMaxScore;
    private int _lastWeakCount;
    private int _lastAlreadyCountedCount;
    private int _lastOtherSheetNewCount;
    private string _lastLimitSummary = "";
    private bool _suppressThresholdScan;
    private bool _accepted;
    private bool _scanRunning;
    private bool _scanRequestedWhileRunning;

    private readonly record struct SimilarCountScanRequest(
        float Threshold,
        bool Rotations,
        bool Mirrored,
        bool AllSheets);

    public SimilarCountDialog(
        Func<float, bool, bool, bool, CancellationToken, Task<SimilarCountScanResult>> scan,
        float initialThreshold,
        bool initialRotations,
        bool initialMirrored,
        bool initialAllSheets,
        string destinationName,
        bool aiAvailable,
        string templateWarning = "")
    {
        _scan = scan;
        _destinationName = CleanDestinationName(destinationName);
        Title = $"Count Similar: {_destinationName}";
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

        if (!string.IsNullOrWhiteSpace(templateWarning))
        {
            panel.Children.Add(new TextBlock
            {
                Text = templateWarning.Trim(),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.78,
                Margin = new Thickness(0, -6, 0, 10),
            });
        }

        _reviewDetailsLabel = new TextBlock
        {
            Text = "",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72,
            Margin = new Thickness(0, -6, 0, 10),
        };
        panel.Children.Add(_reviewDetailsLabel);

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

        var thresholdPresets = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, -2, 0, 8),
        };
        _strictButton = new Button
        {
            Content = "Strict",
            MinWidth = 66,
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Use the highest precision threshold.",
        };
        _defaultButton = new Button
        {
            Content = "Default",
            MinWidth = 66,
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Return to the precision default threshold.",
        };
        _looseButton = new Button
        {
            Content = "Loose",
            MinWidth = 66,
            ToolTip = "Find more candidates; weak ones stay review-only.",
        };
        _strictButton.Click += (_, _) => ApplyThresholdPreset(StrictThresholdPreset);
        _defaultButton.Click += (_, _) => ApplyThresholdPreset(AppSettingsStore.SimilarCountThresholdDefault);
        _looseButton.Click += (_, _) => ApplyThresholdPreset(LooseThresholdPreset);
        thresholdPresets.Children.Add(_strictButton);
        thresholdPresets.Children.Add(_defaultButton);
        thresholdPresets.Children.Add(_looseButton);
        panel.Children.Add(thresholdPresets);

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

        _allSheetsBox = new CheckBox
        {
            Content = "Search all sheets in this job",
            IsChecked = initialAllSheets,
            ToolTip = "Also scan every other sheet for this symbol; strong matches are added there automatically.",
            Margin = new Thickness(0, 0, 0, 8),
        };
        panel.Children.Add(_allSheetsBox);

        _otherSheetsLabel = new TextBlock
        {
            Text = "",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78,
            Margin = new Thickness(0, -2, 0, 8),
        };
        panel.Children.Add(_otherSheetsLabel);

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
            Text = "Blue markers add now, orange markers are weak candidates, gray check markers are already counted.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
            Margin = new Thickness(0, 0, 0, 10),
        };
        panel.Children.Add(hint);

        var reviewButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 10),
        };
        _includeAllButton = new Button
        {
            Content = "Include all",
            MinWidth = 86,
            IsEnabled = false,
            Margin = new Thickness(0, 0, 6, 0),
        };
        _strongOnlyButton = new Button
        {
            Content = "Strong only",
            MinWidth = 86,
            IsEnabled = false,
        };
        _includeAllButton.Click += (_, _) => IncludeAllRequested?.Invoke(this, EventArgs.Empty);
        _strongOnlyButton.Click += (_, _) => StrongOnlyRequested?.Invoke(this, EventArgs.Empty);
        reviewButtons.Children.Add(_includeAllButton);
        reviewButtons.Children.Add(_strongOnlyButton);
        panel.Children.Add(reviewButtons);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        _addButton = new Button
        {
            Content = AddButtonText(0),
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

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
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
            if (_suppressThresholdScan)
                return;

            _debounce.Stop();
            _debounce.Start();
        };
        _rotationsBox.Checked += (_, _) => _ = RunScanAsync();
        _rotationsBox.Unchecked += (_, _) => _ = RunScanAsync();
        _mirroredBox.Checked += (_, _) => _ = RunScanAsync();
        _mirroredBox.Unchecked += (_, _) => _ = RunScanAsync();
        _allSheetsBox.Checked += (_, _) => _ = RunScanAsync();
        _allSheetsBox.Unchecked += (_, _) => _ = RunScanAsync();

        _addButton.Click += (_, _) =>
        {
            Threshold = (float)_thresholdSlider.Value;
            IncludeRotations = _rotationsBox.IsChecked == true;
            IncludeMirrored = _mirroredBox.IsChecked == true;
            IncludeAllSheets = _allSheetsBox.IsChecked == true;
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

    private void ApplyThresholdPreset(double threshold)
    {
        _suppressThresholdScan = true;
        try
        {
            _thresholdSlider.Value = Math.Clamp(
                threshold,
                AppSettingsStore.SimilarCountThresholdMin,
                AppSettingsStore.SimilarCountThresholdMax);
            UpdateThresholdLabel();
        }
        finally
        {
            _suppressThresholdScan = false;
        }

        _debounce.Stop();
        _ = RunScanAsync();
    }

    public void SetReviewCounts(
        int included,
        int total,
        float minScore = 0f,
        float maxScore = 0f,
        int weakCount = 0,
        int alreadyCountedCount = 0,
        string limitSummary = "",
        int otherSheetNewCount = 0,
        int otherSheetCount = 0,
        string otherSheetSummary = "")
    {
        _lastFound = Math.Max(0, included);
        _lastTotal = Math.Max(0, total);
        _lastMinScore = Math.Clamp(minScore, 0f, 1f);
        _lastMaxScore = Math.Clamp(maxScore, 0f, 1f);
        _lastWeakCount = Math.Max(0, weakCount);
        _lastAlreadyCountedCount = Math.Max(0, alreadyCountedCount);
        _lastLimitSummary = limitSummary.Trim();
        _lastOtherSheetNewCount = Math.Max(0, otherSheetNewCount);
        _otherSheetsLabel.Text = otherSheetSummary.Trim();
        var result = new SimilarCountScanResult(
            _lastFound,
            _lastTotal,
            _lastMinScore,
            _lastMaxScore,
            _lastWeakCount,
            _lastAlreadyCountedCount,
            _lastLimitSummary,
            _lastOtherSheetNewCount,
            Math.Max(0, otherSheetCount),
            otherSheetSummary.Trim());

        if (_lastTotal == 0 && _lastOtherSheetNewCount == 0)
        {
            _foundLabel.Text = "Found 0 symbols.";
            _reviewDetailsLabel.Text = "";
            _addButton.Content = AddButtonText(0);
            _addButton.IsEnabled = false;
            _includeAllButton.IsEnabled = false;
            _strongOnlyButton.IsEnabled = false;
            return;
        }

        if (_lastFound == _lastTotal && _lastAlreadyCountedCount == 0)
        {
            _foundLabel.Text = _lastFound == 1
                ? $"Found 1 symbol{ScoreSuffix()}."
                : $"Found {_lastFound} symbols{ScoreSuffix()}.";
            _addButton.Content = AddButtonText(_lastFound);
        }
        else
        {
            _foundLabel.Text = $"Ready to add {_lastFound} of {_lastTotal} found{ScoreSuffix()}.";
            _addButton.Content = AddButtonText(_lastFound);
        }

        _reviewDetailsLabel.Text = ReviewDetails(result);
        _addButton.IsEnabled = _lastFound > 0 || _lastOtherSheetNewCount > 0;
        _includeAllButton.IsEnabled = _lastFound < result.NewCandidateCount;
        _strongOnlyButton.IsEnabled = result.NewCandidateCount > 0;
    }

    private static string ReviewDetails(SimilarCountScanResult result)
    {
        if (result.Total <= 0)
            return "";

        var parts = new List<string>
        {
            $"New {result.NewCandidateCount}",
        };
        if (result.WeakCount > 0)
            parts.Add($"Weak {result.WeakCount}");
        if (result.AlreadyCountedCount > 0)
            parts.Add($"Already counted {result.AlreadyCountedCount}");
        if (result.ExcludedNewCount > 0)
            parts.Add($"Excluded {result.ExcludedNewCount}");
        if (!string.IsNullOrWhiteSpace(result.LimitSummary))
            parts.Add(result.LimitSummary);
        return string.Join(" | ", parts);
    }

    private string ScoreSuffix()
    {
        if (_lastFound <= 0 || _lastMaxScore <= 0f)
            return "";

        return Math.Abs(_lastMaxScore - _lastMinScore) < 0.005f
            ? $" (score {_lastMaxScore:0.00})"
            : $" (score {_lastMinScore:0.00}-{_lastMaxScore:0.00})";
    }

    private string AddButtonText(int count)
    {
        string name = _destinationName.Length <= 22
            ? _destinationName
            : _destinationName[..21] + "...";
        return count <= 0
            ? $"Add to {name}"
            : $"Add {count} to {name}";
    }

    private static string CleanDestinationName(string destinationName)
    {
        string clean = string.IsNullOrWhiteSpace(destinationName)
            ? "Similar Count"
            : destinationName.Trim();
        return clean.Length <= 48 ? clean : clean[..47] + "...";
    }

    private async Task RunScanAsync()
    {
        _scanCts?.Cancel();
        _scanRequestedWhileRunning = true;
        if (_scanRunning)
            return;

        _scanRunning = true;
        try
        {
            while (_scanRequestedWhileRunning)
            {
                _scanRequestedWhileRunning = false;
                await RunLatestScanAsync();
            }
        }
        finally
        {
            _scanRunning = false;
            if (_scanRequestedWhileRunning && !_accepted)
                _ = RunScanAsync();
        }
    }

    private SimilarCountScanRequest CurrentScanRequest() => new(
        (float)_thresholdSlider.Value,
        _rotationsBox.IsChecked == true,
        _mirroredBox.IsChecked == true,
        _allSheetsBox.IsChecked == true);

    private async Task RunLatestScanAsync()
    {
        var cts = new CancellationTokenSource();
        _scanCts = cts;
        SimilarCountScanRequest request = CurrentScanRequest();
        _foundLabel.Text = "Scanning...";
        _reviewDetailsLabel.Text = "";
        _addButton.IsEnabled = false;
        _includeAllButton.IsEnabled = false;
        _strongOnlyButton.IsEnabled = false;
        try
        {
            SimilarCountScanResult result = await _scan(
                request.Threshold,
                request.Rotations,
                request.Mirrored,
                request.AllSheets,
                cts.Token);
            if (cts.IsCancellationRequested || !ReferenceEquals(_scanCts, cts))
                return;

            SetReviewCounts(
                result.Included,
                result.Total,
                result.MinScore,
                result.MaxScore,
                result.WeakCount,
                result.AlreadyCountedCount,
                result.LimitSummary,
                result.OtherSheetNewCount,
                result.OtherSheetCount,
                result.OtherSheetSummary);
        }
        catch (OperationCanceledException)
        {
            // A newer scan superseded this one.
        }
        catch (Exception ex)
        {
            if (!ReferenceEquals(_scanCts, cts))
                return;

            _foundLabel.Text = $"Scan failed: {ex.Message}";
            AppLog.Warn(ex, "Similar count scan failed.");
        }
        finally
        {
            if (ReferenceEquals(_scanCts, cts))
                _scanCts = null;
        }
    }
}
