using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OurPlanCore;

public partial class MainWindow
{
    private BeamAnnotationConfig _beamAnnotationConfig = BeamAnnotationConfig.BuildDefault();
    private CheckBox? _beamAnnotationKeepLineBox;
    private TextBox? _beamAnnotationColorBox;
    private TextBlock? _beamAnnotationStatus;
    private bool _beamAnnotationBinding;

    private void AppendBeamAnnotationSettings(StackPanel root)
    {
        root.Children.Add(Header("Beam annotation line"));
        root.Children.Add(new TextBlock
        {
            Text = "The Beam tool always keeps its blue dimension. This optional companion is a simple annotation line along the same two measured points.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 6),
            Foreground = TryFindResource("SecondaryForegroundBrush") as Brush,
        });

        _beamAnnotationKeepLineBox = new CheckBox
        {
            Content = "Add annotation line for every new Beam",
            Margin = new Thickness(0, 0, 0, 6),
            FontSize = 12,
        };
        root.Children.Add(_beamAnnotationKeepLineBox);

        var colorRow = HBar();
        colorRow.Children.Add(new TextBlock
        {
            Text = "Line color (hex):",
            Width = 130,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
        });
        _beamAnnotationColorBox = new TextBox
        {
            Width = 100,
            Text = "#FF0000",
        };
        colorRow.Children.Add(_beamAnnotationColorBox);
        root.Children.Add(colorRow);

        _beamAnnotationStatus = StatusLine();
        root.Children.Add(_beamAnnotationStatus);

        var actions = HBar();
        actions.Children.Add(MgrButton("Apply now", (_, _) => ApplyBeamAnnotationDraft(), primary: true));
        actions.Children.Add(MgrButton("Reset", (_, _) =>
        {
            SetBeamAnnotationEditor(BeamAnnotationConfig.BuildDefault());
            SetBeamAnnotationStatus("Built-in default loaded: off, red #FF0000. Click Apply or save it.");
        }));
        actions.Children.Add(MgrButton("Save global default", (_, _) => SaveGlobalBeamAnnotationDraft()));
        actions.Children.Add(MgrButton("Save as this job", (_, _) => SaveJobBeamAnnotationDraft()));
        actions.Children.Add(MgrButton("Clear job override", (_, _) => ClearJobBeamAnnotationDraft()));
        root.Children.Add(actions);

        BindBeamAnnotationSettings();
    }

    private void BindBeamAnnotationSettings()
    {
        if (_beamAnnotationKeepLineBox == null || _beamAnnotationColorBox == null)
            return;

        SetBeamAnnotationEditor(_beamAnnotationConfig);
        string source = _currentJob != null &&
                        SettingsPresetStore.LoadJobBeamAnnotationOverride(_currentJob) != null
            ? "job override"
            : SettingsPresetStore.LoadGlobalBeamAnnotation() != null
                ? "global default"
                : "built-in default";
        SetBeamAnnotationStatus($"Effective source: {source}. Blue Beam dimension remains enabled.");
    }

    private void SetBeamAnnotationEditor(BeamAnnotationConfig config)
    {
        if (_beamAnnotationKeepLineBox == null || _beamAnnotationColorBox == null)
            return;

        _beamAnnotationBinding = true;
        try
        {
            _beamAnnotationKeepLineBox.IsChecked = config.KeepLineAnnotation;
            _beamAnnotationColorBox.Text = BeamAnnotationConfig.NormalizeColor(config.LineColor);
        }
        finally
        {
            _beamAnnotationBinding = false;
        }
    }

    private bool TryReadBeamAnnotationEditor(out BeamAnnotationConfig config)
    {
        config = BeamAnnotationConfig.BuildDefault();
        if (_beamAnnotationBinding ||
            _beamAnnotationKeepLineBox == null ||
            _beamAnnotationColorBox == null)
        {
            return false;
        }

        if (!BeamAnnotationConfig.TryNormalizeColor(_beamAnnotationColorBox.Text, out string color))
        {
            SetBeamAnnotationStatus("Enter a color as #RRGGBB, for example #FF0000.");
            _beamAnnotationColorBox.Focus();
            _beamAnnotationColorBox.SelectAll();
            return false;
        }

        config = new BeamAnnotationConfig
        {
            KeepLineAnnotation = _beamAnnotationKeepLineBox.IsChecked == true,
            LineColor = color,
        };
        return true;
    }

    private void ApplyBeamAnnotationDraft()
    {
        if (!TryReadBeamAnnotationEditor(out BeamAnnotationConfig config))
            return;

        _beamAnnotationConfig = config.Clone();
        BeamAnnotationConfigProvider.Install(_beamAnnotationConfig);
        SetBeamAnnotationStatus(
            $"Applied now: line {(_beamAnnotationConfig.KeepLineAnnotation ? "on" : "off")}, {_beamAnnotationConfig.LineColor}.");
        TxtStatus.Text = "Beam annotation default applied for this app session.";
    }

    private void SaveGlobalBeamAnnotationDraft()
    {
        if (!TryReadBeamAnnotationEditor(out BeamAnnotationConfig config))
            return;

        SettingsPresetStore.SaveGlobalBeamAnnotation(config);
        _beamAnnotationConfig = config.Clone();
        BeamAnnotationConfigProvider.Install(_beamAnnotationConfig);
        SetBeamAnnotationStatus("Saved and applied as the global Beam default.");
        TxtStatus.Text = "Beam annotation global default saved.";
    }

    private void SaveJobBeamAnnotationDraft()
    {
        if (_currentJob == null)
        {
            SetBeamAnnotationStatus("Open a job before saving a job override.");
            return;
        }
        if (!EnsureCurrentJobWritable("save the Beam annotation job override") ||
            !TryReadBeamAnnotationEditor(out BeamAnnotationConfig config))
        {
            return;
        }

        SettingsPresetStore.SaveJobBeamAnnotationOverride(_currentJob, config);
        _beamAnnotationConfig = config.Clone();
        BeamAnnotationConfigProvider.Install(_beamAnnotationConfig);
        SetBeamAnnotationStatus("Saved and applied as this job's Beam override.");
        TxtStatus.Text = "Beam annotation job override saved.";
    }

    private void ClearJobBeamAnnotationDraft()
    {
        if (_currentJob == null)
        {
            SetBeamAnnotationStatus("Open a job before clearing a job override.");
            return;
        }
        if (!EnsureCurrentJobWritable("clear the Beam annotation job override"))
            return;

        if (!SettingsPresetStore.ClearJobBeamAnnotationOverride(_currentJob))
        {
            SetBeamAnnotationStatus("The job override could not be cleared.");
            return;
        }

        _beamAnnotationConfig = SettingsPresetStore.ResolveBeamAnnotation(_currentJob).Clone();
        BeamAnnotationConfigProvider.Install(_beamAnnotationConfig);
        BindBeamAnnotationSettings();
        TxtStatus.Text = "Beam annotation job override cleared.";
    }

    private void SetBeamAnnotationStatus(string text)
    {
        if (_beamAnnotationStatus != null)
            _beamAnnotationStatus.Text = text;
    }
}
