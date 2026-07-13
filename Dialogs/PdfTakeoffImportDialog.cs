using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace OurPlanCore.Controls;

public sealed class PdfTakeoffImportDialog : Window
{
    private readonly TextBox _sourceBox;
    private readonly TextBox _jobsRootBox;
    private readonly TextBox _jobNameBox;
    private readonly RadioButton _newJobRadio;
    private readonly RadioButton _currentJobRadio;
    private readonly CheckBox _takeoffsBox;
    private readonly CheckBox _rulersBox;
    private readonly CheckBox _cleanPdfBox;
    private readonly bool _hasCurrentJob;

    public PdfTakeoffImportOptions ImportOptions { get; private set; } = new();

    public PdfTakeoffImportDialog(
        string defaultSourceFolder,
        string defaultJobsRoot,
        string defaultJobName,
        bool hasCurrentJob,
        string currentJobName = "")
    {
        _hasCurrentJob = hasCurrentJob;
        Title = "Import PDF Takeoffs";
        Width = 760;
        Height = 520;
        MinWidth = 660;
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

        var importButton = new Button { Content = "Scan / Preview", MinWidth = 112, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancelButton = new Button { Content = "Cancel", MinWidth = 82, IsCancel = true };
        buttons.Children.Add(importButton);
        buttons.Children.Add(cancelButton);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        root.Children.Add(scroll);

        var form = new StackPanel();
        scroll.Content = form;

        _sourceBox = CreatePathRow(
            form,
            "PDF folder",
            defaultSourceFolder,
            "Select the folder containing PDF takeoff annotations. PDFs are scanned recursively.",
            "Select folder with PDF takeoff annotations");

        form.Children.Add(new TextBlock { Text = "Import mode", Margin = new Thickness(0, 9, 0, 4) });
        _newJobRadio = new RadioButton
        {
            Content = "Create new job from PDF takeoffs",
            IsChecked = true,
            Margin = new Thickness(0, 0, 0, 4),
        };
        _currentJobRadio = new RadioButton
        {
            Content = string.IsNullOrWhiteSpace(currentJobName)
                ? "Import into current job"
                : $"Import into current job ({currentJobName})",
            IsEnabled = hasCurrentJob,
            Margin = new Thickness(0, 0, 0, 4),
        };
        form.Children.Add(_newJobRadio);
        form.Children.Add(_currentJobRadio);

        _jobsRootBox = CreatePathRow(
            form,
            "New job parent folder",
            defaultJobsRoot,
            "Select where the new OurPlanCore job folder will be created.",
            "Select OurPlanCore jobs root folder");

        form.Children.Add(new TextBlock { Text = "New job name", Margin = new Thickness(0, 7, 0, 3) });
        _jobNameBox = new TextBox
        {
            Text = defaultJobName,
            MinHeight = 26,
            ToolTip = "Used only when Create new job is selected.",
        };
        form.Children.Add(_jobNameBox);

        form.Children.Add(new TextBlock { Text = "Import options", Margin = new Thickness(0, 12, 0, 4) });
        _takeoffsBox = new CheckBox
        {
            Content = "Import PDF takeoff lines / areas / counts as editable takeoffs",
            IsChecked = true,
            Margin = new Thickness(0, 0, 0, 4),
        };
        _rulersBox = new CheckBox
        {
            Content = "Import PDF dimensions as Ruler annotations",
            IsChecked = true,
            Margin = new Thickness(0, 0, 0, 4),
        };
        _cleanPdfBox = new CheckBox
        {
            Content = "Remove supported PDF measurement annotations from sheet background",
            IsChecked = true,
            Margin = new Thickness(0, 0, 0, 4),
        };
        form.Children.Add(_takeoffsBox);
        form.Children.Add(_rulersBox);
        form.Children.Add(_cleanPdfBox);

        var hint = new TextBlock
        {
            Text = "The source PDFs remain unchanged. When cleanup is enabled, OurPlanCore imports a clean copy so dimensions and takeoff lines appear only as editable app objects.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryForegroundBrush");
        form.Children.Add(hint);

        _newJobRadio.Checked += (_, _) => UpdateDestinationEnabledState();
        _currentJobRadio.Checked += (_, _) => UpdateDestinationEnabledState();
        _sourceBox.TextChanged += (_, _) => MaybeRefreshDefaultJobName();
        importButton.Click += (_, _) => Accept();
        Loaded += (_, _) => _sourceBox.Focus();
        UpdateDestinationEnabledState();
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

    private void UpdateDestinationEnabledState()
    {
        bool newJob = _newJobRadio.IsChecked == true || !_hasCurrentJob;
        _jobsRootBox.IsEnabled = newJob;
        _jobNameBox.IsEnabled = newJob;
        if (!_hasCurrentJob)
            _newJobRadio.IsChecked = true;
    }

    private void MaybeRefreshDefaultJobName()
    {
        if (!string.IsNullOrWhiteSpace(_jobNameBox.Text))
            return;

        string source = NormalizePath(_sourceBox.Text);
        if (!string.IsNullOrWhiteSpace(source))
            _jobNameBox.Text = DefaultJobName(source);
    }

    private void Accept()
    {
        string source = NormalizePath(_sourceBox.Text);
        if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source))
        {
            MessageBox.Show("Select a PDF folder first.", "Import PDF Takeoffs",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        bool newJob = _newJobRadio.IsChecked == true || !_hasCurrentJob;
        string jobsRoot = NormalizePath(_jobsRootBox.Text);
        string jobName = _jobNameBox.Text.Trim();
        if (newJob)
        {
            if (string.IsNullOrWhiteSpace(jobsRoot))
            {
                MessageBox.Show("Select a new job parent folder.", "Import PDF Takeoffs",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(jobName))
            {
                MessageBox.Show("Enter a new job name.", "Import PDF Takeoffs",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        bool importTakeoffs = _takeoffsBox.IsChecked == true;
        bool importRulers = _rulersBox.IsChecked == true;
        if (!importTakeoffs && !importRulers)
        {
            MessageBox.Show("Enable at least one import option: takeoffs or rulers.", "Import PDF Takeoffs",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ImportOptions = new PdfTakeoffImportOptions
        {
            Mode = newJob ? PdfTakeoffImportMode.CreateNewJob : PdfTakeoffImportMode.ImportIntoCurrentJob,
            SourceFolder = source,
            JobsRootPath = jobsRoot,
            JobName = jobName,
            ImportTakeoffs = importTakeoffs,
            ImportDimensionsAsRulers = importRulers,
            RemoveSupportedPdfAnnotations = _cleanPdfBox.IsChecked == true,
        };
        DialogResult = true;
    }

    private static string NormalizePath(string value) =>
        (value ?? "").Trim().Trim('"');

    public static string DefaultJobName(string sourceFolder)
    {
        string clean = sourceFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string name = Path.GetFileName(clean);
        return string.IsNullOrWhiteSpace(name) ? "PDF Takeoffs" : name;
    }
}
