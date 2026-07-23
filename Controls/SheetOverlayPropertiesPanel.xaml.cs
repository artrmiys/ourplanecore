using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OurPlanCore.Controls;

public enum SheetOverlayPropertiesCommand
{
    OpenSource,
    ChooseReplace,
    NextMatch,
    Clear,
    AutoFit,
    FindBest,
    EditByPoints,
    ResetTransform,
    SetVisibility,
    SetColor,
}

public enum SheetOverlayTransformComponent
{
    OffsetX,
    OffsetY,
    Scale,
    Rotation,
}

public sealed record SheetOverlayPropertiesState(
    string TargetSheetName,
    string SourceSheetName,
    string SourcePath,
    bool HasOverlay,
    bool IsVisible,
    bool IsReadOnly,
    double OffsetXPt,
    double OffsetYPt,
    double OverlayScale,
    double OverlayRotationDegrees,
    string OverlayColor,
    double OffsetSliderRangePt);

public sealed record SheetOverlayTransformValues(
    double OffsetXPt,
    double OffsetYPt,
    double OverlayScale,
    double OverlayRotationDegrees);

public sealed class SheetOverlayPropertiesCommandEventArgs(
    SheetOverlayPropertiesCommand command,
    string value = "",
    bool? toggleValue = null) : EventArgs
{
    public SheetOverlayPropertiesCommand Command { get; } = command;
    public string Value { get; } = value;
    public bool? ToggleValue { get; } = toggleValue;
}

public sealed class SheetOverlayTransformEventArgs(
    SheetOverlayTransformComponent component,
    SheetOverlayTransformValues values) : EventArgs
{
    public SheetOverlayTransformComponent Component { get; } = component;
    public SheetOverlayTransformValues Values { get; } = values;
}

public partial class SheetOverlayPropertiesPanel : UserControl
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private bool _updating;
    private bool _previewPending;
    private bool _hasOverlay;
    private bool _isReadOnly;

    public event EventHandler<SheetOverlayPropertiesCommandEventArgs>? CommandRequested;
    public event EventHandler<SheetOverlayTransformEventArgs>? TransformPreviewRequested;
    public event EventHandler? TransformCommitRequested;
    public event EventHandler? TransformCancelRequested;

    public SheetOverlayPropertiesPanel()
    {
        InitializeComponent();
    }

    public void SetState(SheetOverlayPropertiesState state)
    {
        _updating = true;
        try
        {
            _hasOverlay = state.HasOverlay;
            _isReadOnly = state.IsReadOnly;
            _previewPending = false;

            TxtTargetSheet.Text = string.IsNullOrWhiteSpace(state.TargetSheetName)
                ? "No sheet open"
                : state.TargetSheetName;
            TxtSourceSheet.Text = state.HasOverlay
                ? state.SourceSheetName
                : "Not set";
            TxtSourceSheet.ToolTip = string.IsNullOrWhiteSpace(state.SourcePath)
                ? null
                : state.SourcePath;
            TxtPanelStatus.Text = BuildStatus(state);
            ChkVisible.IsChecked = state.IsVisible;

            double range = Math.Clamp(
                Math.Max(720, state.OffsetSliderRangePt),
                720,
                100000);
            SldOffsetX.Minimum = -range;
            SldOffsetX.Maximum = range;
            SldOffsetY.Minimum = -range;
            SldOffsetY.Maximum = range;
            ApplyTransformValues(new SheetOverlayTransformValues(
                state.OffsetXPt,
                state.OffsetYPt,
                state.OverlayScale,
                state.OverlayRotationDegrees));
            UpdateColorSelection(state.OverlayColor);
            ApplyAvailability();
        }
        finally
        {
            _updating = false;
        }
    }

    public void ApplyPreviewValues(SheetOverlayTransformValues values)
    {
        _updating = true;
        try
        {
            ApplyTransformValues(values, preserveFocusedEditor: true);
        }
        finally
        {
            _updating = false;
        }
    }

    public void CompletePendingPreview() =>
        _previewPending = false;

    private static string BuildStatus(SheetOverlayPropertiesState state)
    {
        if (string.IsNullOrWhiteSpace(state.TargetSheetName))
            return "Open a sheet to configure its overlay.";
        if (!state.HasOverlay)
            return state.IsReadOnly
                ? "No overlay is configured. This job is read-only."
                : "Choose a source sheet or use Find Best.";
        return state.IsReadOnly
            ? "Read-only: overlay properties cannot be changed."
            : "Orange frame marks the active overlay.";
    }

    private void ApplyAvailability()
    {
        bool canEdit = !_isReadOnly;
        bool canEditOverlay = _hasOverlay && canEdit;
        ChkVisible.IsEnabled = canEditOverlay;
        BtnOpenSource.IsEnabled = _hasOverlay;
        BtnChooseReplace.IsEnabled = canEdit;
        BtnNextMatch.IsEnabled = canEditOverlay;
        BtnClear.IsEnabled = canEditOverlay;
        BtnAutoFit.IsEnabled = canEdit;
        BtnFindBest.IsEnabled = canEdit;
        BtnEditPoints.IsEnabled = canEditOverlay;
        BtnReset.IsEnabled = canEditOverlay;

        foreach (Control control in TransformControls())
            control.IsEnabled = canEditOverlay;
        foreach (Button button in ColorButtons())
            button.IsEnabled = canEditOverlay;
    }

    private Control[] TransformControls() =>
    [
        SldOffsetX,
        SldOffsetY,
        SldScale,
        SldRotation,
        TxtOffsetX,
        TxtOffsetY,
        TxtScale,
        TxtRotation,
    ];

    private Button[] ColorButtons() =>
    [
        BtnColorRed,
        BtnColorBlue,
        BtnColorGreen,
        BtnColorOrange,
        BtnColorMagenta,
        BtnColorGray,
    ];

    private void ApplyTransformValues(
        SheetOverlayTransformValues values,
        bool preserveFocusedEditor = false)
    {
        double x = Math.Clamp(values.OffsetXPt, SldOffsetX.Minimum, SldOffsetX.Maximum);
        double y = Math.Clamp(values.OffsetYPt, SldOffsetY.Minimum, SldOffsetY.Maximum);
        double scale = Math.Clamp(values.OverlayScale, 0.05, 20);
        double rotation = NormalizeRotation(values.OverlayRotationDegrees);

        SldOffsetX.Value = x;
        SldOffsetY.Value = y;
        SldScale.Value = Math.Clamp(scale, SldScale.Minimum, SldScale.Maximum);
        SldRotation.Value = rotation;
        SetEditorValue(TxtOffsetX, values.OffsetXPt, preserveFocusedEditor);
        SetEditorValue(TxtOffsetY, values.OffsetYPt, preserveFocusedEditor);
        SetEditorValue(TxtScale, scale, preserveFocusedEditor);
        SetEditorValue(TxtRotation, rotation, preserveFocusedEditor);
    }

    private static void SetEditorValue(
        TextBox editor,
        double value,
        bool preserveFocusedEditor)
    {
        if (preserveFocusedEditor && ReferenceEquals(Keyboard.FocusedElement, editor))
            return;

        editor.Text = FormatNumber(value);
    }

    private void TransformSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || !_hasOverlay || _isReadOnly ||
            sender is not Slider slider ||
            !TryReadComponent(slider.Tag, out SheetOverlayTransformComponent component))
        {
            return;
        }

        _updating = true;
        try
        {
            TextBoxFor(component).Text = FormatNumber(slider.Value);
        }
        finally
        {
            _updating = false;
        }

        RaisePreview(component);
    }

    private void TransformSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        CommitPreviewIfPending();

    private void TransformSlider_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or
            Key.PageUp or Key.PageDown or Key.Home or Key.End)
        {
            CommitPreviewIfPending();
        }
    }

    private void TransformTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating || !_hasOverlay || _isReadOnly ||
            sender is not TextBox box ||
            !TryReadComponent(box.Tag, out SheetOverlayTransformComponent component) ||
            !TryReadTransformValues(out _))
        {
            return;
        }

        RaisePreview(component);
    }

    private void TransformTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        CommitPreviewIfPending();

    private void TransformTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitPreviewIfPending();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && CancelPreviewIfPending())
        {
            e.Handled = true;
        }
    }

    private void RaisePreview(SheetOverlayTransformComponent component)
    {
        if (!TryReadTransformValues(out SheetOverlayTransformValues values))
            return;

        _previewPending = true;
        TransformPreviewRequested?.Invoke(this, new SheetOverlayTransformEventArgs(component, values));
    }

    private bool TryReadTransformValues(out SheetOverlayTransformValues values)
    {
        values = new SheetOverlayTransformValues(0, 0, 1, 0);
        if (!TryParse(TxtOffsetX.Text, out double x) ||
            !TryParse(TxtOffsetY.Text, out double y) ||
            !TryParse(TxtScale.Text, out double scale) ||
            !TryParse(TxtRotation.Text, out double rotation))
        {
            return false;
        }

        values = new SheetOverlayTransformValues(
            Math.Clamp(x, -100000, 100000),
            Math.Clamp(y, -100000, 100000),
            Math.Clamp(scale, 0.05, 20),
            NormalizeRotation(rotation));
        return true;
    }

    private void CommitPreviewIfPending()
    {
        if (!_previewPending)
            return;

        _previewPending = false;
        TransformCommitRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool CancelPreviewIfPending()
    {
        if (!_previewPending)
            return false;

        _previewPending = false;
        TransformCancelRequested?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void Panel_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && CancelPreviewIfPending())
            e.Handled = true;
    }

    private void Visibility_Click(object sender, RoutedEventArgs e)
    {
        if (_updating)
            return;

        CommandRequested?.Invoke(
            this,
            new SheetOverlayPropertiesCommandEventArgs(
                SheetOverlayPropertiesCommand.SetVisibility,
                toggleValue: ChkVisible.IsChecked == true));
    }

    private void CommandButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string commandText } ||
            !Enum.TryParse(commandText, out SheetOverlayPropertiesCommand command))
        {
            return;
        }

        CommandRequested?.Invoke(this, new SheetOverlayPropertiesCommandEventArgs(command));
    }

    private void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string color })
            return;

        CommandRequested?.Invoke(
            this,
            new SheetOverlayPropertiesCommandEventArgs(
                SheetOverlayPropertiesCommand.SetColor,
                color));
    }

    private void UpdateColorSelection(string color)
    {
        foreach (Button button in ColorButtons())
        {
            bool selected = string.Equals(button.Tag as string, color, StringComparison.OrdinalIgnoreCase);
            button.BorderBrush = selected
                ? new SolidColorBrush(Color.FromRgb(0xF4, 0x9B, 0x24))
                : (Brush)FindResource("ControlBorderBrush");
            button.BorderThickness = new Thickness(selected ? 3 : 1);
        }
    }

    private TextBox TextBoxFor(SheetOverlayTransformComponent component) =>
        component switch
        {
            SheetOverlayTransformComponent.OffsetX => TxtOffsetX,
            SheetOverlayTransformComponent.OffsetY => TxtOffsetY,
            SheetOverlayTransformComponent.Scale => TxtScale,
            _ => TxtRotation,
        };

    private static bool TryReadComponent(object? tag, out SheetOverlayTransformComponent component) =>
        Enum.TryParse(tag?.ToString(), out component);

    private static bool TryParse(string text, out double value) =>
        double.TryParse(
            (text ?? "").Trim().Replace(',', '.'),
            NumberStyles.Float,
            Invariant,
            out value);

    private static double NormalizeRotation(double degrees)
    {
        if (double.IsNaN(degrees) || double.IsInfinity(degrees))
            return 0;

        double normalized = degrees % 360;
        if (normalized > 180)
            normalized -= 360;
        if (normalized <= -180)
            normalized += 360;
        return normalized;
    }

    private static string FormatNumber(double value) =>
        value.ToString("0.###", Invariant);
}
