using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace OurPlanCore.Controls;

public sealed class PlanSwiftImportDialog : Window
{
    private readonly TextBox _sourceBox;
    private readonly TextBox _destinationBox;
    private readonly TextBox _jobNameBox;
    private readonly CheckBox _convertImagesBox;
    private readonly CheckBox _importAllBox;
    private readonly TextBlock _summaryText;
    private readonly TextBlock _statusText;
    private readonly Button _scanButton;
    private readonly Button _importButton;
    private readonly bool _importIntoCurrentJob;

    private PlanSwiftProjectManifest? _manifest;
    private string _scannedSource = "";

    public PlanSwiftImportOptions ImportOptions { get; private set; } = new();

    public PlanSwiftProjectManifest? Manifest => _manifest;

    public PlanSwiftImportDialog(
        string defaultDestinationRoot,
        bool importIntoCurrentJob = false,
        string currentJobName = "")
    {
        _importIntoCurrentJob = importIntoCurrentJob;
        Title = importIntoCurrentJob ? "Import PlanSwift to Current Job" : "Import PlanSwift Job";
        Width = 760;
        Height = 540;
        MinWidth = 640;
        MinHeight = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        var root = new DockPanel { Margin = new Thickness(12) };
        Content = root;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        _scanButton = new Button { Content = "Scan", MinWidth = 74, Margin = new Thickness(0, 0, 6, 0) };
        _importButton = new Button { Content = "Import", MinWidth = 82, IsDefault = true, Margin = new Thickness(0, 0, 6, 0), IsEnabled = false };
        var cancel = new Button { Content = "Cancel", MinWidth = 82, IsCancel = true };
        buttons.Children.Add(_scanButton);
        buttons.Children.Add(_importButton);
        buttons.Children.Add(cancel);

        var form = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(form, Dock.Top);
        root.Children.Add(form);

        _sourceBox = CreatePathRow(
            form,
            "PlanSwift job folder",
            "",
            "Select the source PlanSwift job folder. The original folder is read only for this import.",
            "Select PlanSwift job folder");

        if (importIntoCurrentJob)
        {
            string jobLabel = string.IsNullOrWhiteSpace(currentJobName) ? defaultDestinationRoot : currentJobName.Trim();
            var destinationText = new TextBlock
            {
                Text = $"Destination: current job ({jobLabel}) -> Pages/01. planswift and Takeoffs/01. planswift",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 7, 0, 3),
            };
            destinationText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryForegroundBrush");
            form.Children.Add(destinationText);
            _destinationBox = new TextBox { Text = defaultDestinationRoot, Visibility = Visibility.Collapsed };
            _jobNameBox = new TextBox { Visibility = Visibility.Collapsed };
        }
        else
        {
            _destinationBox = CreatePathRow(
                form,
                "OurPlanCore jobs root",
                defaultDestinationRoot,
                "Select the parent folder where the converted job will be created.",
                "Select destination jobs folder");

            form.Children.Add(new TextBlock { Text = "Imported job name", Margin = new Thickness(0, 7, 0, 3) });
            _jobNameBox = new TextBox
            {
                MinHeight = 26,
                ToolTip = "Leave blank to use the PlanSwift job name with an imported suffix.",
            };
            form.Children.Add(_jobNameBox);
        }

        _convertImagesBox = new CheckBox
        {
            Content = "Convert PlanSwift page images to visible PDF sheets",
            IsChecked = true,
            Margin = new Thickness(0, 10, 0, 0),
        };
        form.Children.Add(_convertImagesBox);

        _importAllBox = new CheckBox
        {
            Content = "Import all PlanSwift sheets and takeoff folders",
            IsChecked = false,
            Margin = new Thickness(0, 6, 0, 0),
            ToolTip = "When off, import keeps the current behavior and skips sheets with no measured takeoff geometry.",
        };
        form.Children.Add(_importAllBox);

        _statusText = new TextBlock
        {
            Text = "Choose a PlanSwift job folder, then scan it before import.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
        };
        _statusText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryForegroundBrush");
        form.Children.Add(_statusText);

        _summaryText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
        };
        var summaryScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _summaryText,
        };
        root.Children.Add(summaryScroll);

        _sourceBox.TextChanged += (_, _) => InvalidateScan();
        _scanButton.Click += (_, _) => ScanCurrentSource();
        _importButton.Click += (_, _) => AcceptImport();

        Loaded += (_, _) => _sourceBox.Focus();
    }

    private static TextBox CreatePathRow(
        Panel parent,
        string label,
        string initial,
        string tooltip,
        string browseTitle)
    {
        parent.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 3) });

        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 7) };
        var browseButton = new Button
        {
            Content = "Browse...",
            MinWidth = 88,
            Margin = new Thickness(6, 0, 0, 0),
        };
        DockPanel.SetDock(browseButton, Dock.Right);
        row.Children.Add(browseButton);

        var textBox = new TextBox
        {
            Text = initial,
            MinHeight = 26,
            ToolTip = tooltip,
        };
        row.Children.Add(textBox);
        parent.Children.Add(row);

        browseButton.Click += (_, _) => BrowseFolder(textBox, browseTitle);
        return textBox;
    }

    private static void BrowseFolder(TextBox target, string title)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false,
        };
        if (Directory.Exists(target.Text))
            dialog.FolderName = target.Text;

        if (dialog.ShowDialog() == true)
            target.Text = dialog.FolderName;
    }

    private void InvalidateScan()
    {
        string source = NormalizePath(_sourceBox.Text);
        if (string.Equals(source, _scannedSource, StringComparison.OrdinalIgnoreCase))
            return;

        _manifest = null;
        _scannedSource = "";
        _summaryText.Text = "";
        _importButton.IsEnabled = false;
        _statusText.Text = "Scan is needed for the selected PlanSwift folder.";
    }

    private void ScanCurrentSource()
    {
        try
        {
            string source = NormalizePath(_sourceBox.Text);
            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("PlanSwift job folder is required.");
            if (!Directory.Exists(source))
                throw new DirectoryNotFoundException(source);

            PlanSwiftProjectManifest manifest = PlanSwiftProjectScanner.Scan(source);
            _manifest = manifest;
            _scannedSource = source;
            if (!_importIntoCurrentJob && string.IsNullOrWhiteSpace(_jobNameBox.Text))
                _jobNameBox.Text = $"{manifest.JobName} - imported";

            _summaryText.Text = BuildPreview(manifest);
            _statusText.Text = BuildScanStatus(manifest);
            if (_importIntoCurrentJob &&
                !string.Equals(manifest.SourceFormat, PlanSwiftSourceFormats.PlanSwift, StringComparison.OrdinalIgnoreCase))
            {
                _statusText.Text = "Scan complete, but current-job import supports PlanSwift source job folders only.";
                _importButton.IsEnabled = false;
            }
            else
            {
                _importButton.IsEnabled = manifest.Pages.Count > 0 || manifest.TakeoffItems.Count > 0;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or DirectoryNotFoundException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _manifest = null;
            _scannedSource = "";
            _summaryText.Text = "";
            _importButton.IsEnabled = false;
            _statusText.Text = $"Scan failed: {ex.Message}";
            MessageBox.Show($"PlanSwift scan failed:\n{ex.Message}", "Import PlanSwift Job", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AcceptImport()
    {
        PlanSwiftProjectManifest manifest = EnsureFreshScan();
        if (_importIntoCurrentJob)
        {
            if (!string.Equals(manifest.SourceFormat, PlanSwiftSourceFormats.PlanSwift, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "Import to current job supports PlanSwift source job folders only.",
                    "Import PlanSwift to Current Job",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            string destinationJob = NormalizePath(_destinationBox.Text);
            if (string.IsNullOrWhiteSpace(destinationJob) || !Directory.Exists(destinationJob))
            {
                MessageBox.Show("Current job folder is not available.", "Import PlanSwift to Current Job", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ImportOptions = new PlanSwiftImportOptions
            {
                SourceJobPath = manifest.SourceJobPath,
                DestinationJobPath = destinationJob,
                ConvertPageImages = _convertImagesBox.IsChecked == true,
                ImportAllSheetsAndTakeoffFolders = _importAllBox.IsChecked == true,
                ImportRootFolderName = PlanSwiftImportOptions.DefaultCurrentJobImportFolderName,
            };
            DialogResult = true;
            return;
        }

        string destination = NormalizePath(_destinationBox.Text);
        if (string.IsNullOrWhiteSpace(destination))
        {
            MessageBox.Show("Destination jobs root is required.", "Import PlanSwift Job", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ImportOptions = new PlanSwiftImportOptions
        {
            SourceJobPath = manifest.SourceJobPath,
            DestinationParentPath = destination,
            DestinationJobName = _jobNameBox.Text.Trim(),
            ConvertPageImages = _convertImagesBox.IsChecked == true,
            ImportAllSheetsAndTakeoffFolders = _importAllBox.IsChecked == true,
        };
        DialogResult = true;
    }

    private PlanSwiftProjectManifest EnsureFreshScan()
    {
        string source = NormalizePath(_sourceBox.Text);
        if (_manifest != null && string.Equals(source, _scannedSource, StringComparison.OrdinalIgnoreCase))
            return _manifest;

        ScanCurrentSource();
        if (_manifest == null)
            throw new InvalidOperationException("Scan the PlanSwift job before importing.");
        return _manifest;
    }

    private static string BuildPreview(PlanSwiftProjectManifest manifest)
    {
        int sections = manifest.TakeoffItems.Sum(item => item.Sections.Count);
        int holes = manifest.TakeoffItems.Sum(item => item.Sections.Sum(section => section.Holes.Count));
        int segmentSections = manifest.Segments.Sum(segment => segment.Sections.Count);
        int missingImages = manifest.Pages.Count(page => !File.Exists(page.ImagePath));
        int pagesWithScale = manifest.Pages.Count(page => page.ScaleX > 0);

        var lines = new[]
        {
            $"Source: {manifest.SourceJobPath}",
            $"Job: {manifest.JobName}",
            $"Format: {manifest.SourceFormat}",
            "",
            $"Pages: {manifest.Pages.Count.ToString(CultureInfo.InvariantCulture)}",
            $"Pages with scale: {pagesWithScale.ToString(CultureInfo.InvariantCulture)}",
            $"Missing page images: {missingImages.ToString(CultureInfo.InvariantCulture)}",
            $"Takeoff folders: {manifest.TakeoffFolders.Count.ToString(CultureInfo.InvariantCulture)}",
            $"Takeoff items: {manifest.TakeoffItems.Count.ToString(CultureInfo.InvariantCulture)}",
            $"Measured sections: {sections.ToString(CultureInfo.InvariantCulture)}",
            $"Area subtract holes: {holes.ToString(CultureInfo.InvariantCulture)}",
            $"PlanSwift segments: {manifest.Segments.Count.ToString(CultureInfo.InvariantCulture)}",
            $"PlanSwift segments with geometry: {manifest.Segments.Count(segment => segment.Sections.Count > 0).ToString(CultureInfo.InvariantCulture)}",
            $"Segment sections: {segmentSections.ToString(CultureInfo.InvariantCulture)}",
            $"Estimate/material rows: {manifest.EstimateItems.Count.ToString(CultureInfo.InvariantCulture)}",
            $"Notes: {manifest.Notes.Count.ToString(CultureInfo.InvariantCulture)}",
            $"Warnings: {manifest.Warnings.Count.ToString(CultureInfo.InvariantCulture)}",
            "",
        }.ToList();

        if (manifest.Warnings.Count > 0)
        {
            lines.Add("Warnings preview:");
            lines.AddRange(manifest.Warnings.Take(20).Select(warning => $"- {warning}"));
            if (manifest.Warnings.Count > 20)
                lines.Add($"- ... {manifest.Warnings.Count - 20} more warnings");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildScanStatus(PlanSwiftProjectManifest manifest)
    {
        int measuredSections = manifest.TakeoffItems.Sum(item => item.Sections.Count) +
            manifest.Segments.Sum(segment => segment.Sections.Count);
        if (manifest.TakeoffFolders.Count > 0 && manifest.TakeoffItems.Count == 0 && measuredSections == 0)
        {
            return "Scan complete. Found Pages and Takeoff folders only; no measured PlanSwift sections were found.";
        }

        return "Scan complete. Review counts and warnings before import.";
    }

    private static string NormalizePath(string path)
    {
        string clean = (path ?? "").Trim().Trim('"');
        return string.IsNullOrWhiteSpace(clean) ? "" : Path.GetFullPath(clean);
    }
}
