using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OurPlanCore;

public sealed class PdfOutputPreviewWindow : Window
{
    private readonly Image _image = new() { Stretch = Stretch.Fill };
    private readonly MatrixTransform _transform = new();
    private readonly Canvas _surface = new()
    {
        ClipToBounds = true,
        Focusable = true,
        Background = new SolidColorBrush(Color.FromRgb(65, 65, 65)),
    };
    private readonly TextBlock _status = new() { Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap };
    private readonly Button _save = new() { Content = "Save PDF...", Margin = new Thickness(4), IsEnabled = false };
    private double _zoom = 1;
    private Vector _pan;
    private bool _fit = true;
    private bool _panning;
    private Point _lastPanPoint;

    public event Action? SaveRequested;
    public byte[]? PdfBytes { get; private set; }
    public double ZoomWheelFactor { get; set; } = 2.0;

    public PdfOutputPreviewWindow(string pageName)
    {
        Title = $"PDF Preview - {pageName}";
        Width = 1050;
        Height = 780;
        MinWidth = 500;
        MinHeight = 350;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel();
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
        AddButton(toolbar, "Fit", () => { _fit = true; ResizeImage(); });
        AddButton(toolbar, "-", () => Zoom(0.8, SurfaceCenter));
        AddButton(toolbar, "+", () => Zoom(1.25, SurfaceCenter));
        AddButton(toolbar, "100%", () => Zoom(1 / _zoom, SurfaceCenter));
        _save.Click += (_, _) => SaveRequested?.Invoke();
        toolbar.Children.Add(_save);
        toolbar.Children.Add(new TextBlock
        {
            Text = "Live PDF Output settings", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
        });
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);
        _image.RenderTransform = _transform;
        _surface.Children.Add(_image);
        root.Children.Add(_surface);
        Content = root;
        _surface.SizeChanged += (_, _) => { if (_fit) ResizeImage(); };
        WirePreviewInput();
    }

    private void WirePreviewInput()
    {
        _surface.PreviewMouseWheel += (_, e) =>
        {
            double step = Math.Clamp(ZoomWheelFactor, 1.01, 4);
            Zoom(e.Delta > 0 ? step : 1 / step, e.GetPosition(_surface));
            e.Handled = true;
        };
        _surface.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (_image.Source == null) return;
            _surface.Focus();
            if (_surface.CaptureMouse()) BeginPan(e.GetPosition(_surface));
            e.Handled = true;
        };
        _surface.PreviewMouseMove += (_, e) =>
        {
            if (!_panning) return;
            if (e.RightButton != MouseButtonState.Pressed) EndPan();
            else MovePan(e.GetPosition(_surface));
            e.Handled = true;
        };
        _surface.PreviewMouseRightButtonUp += (_, e) =>
        {
            if (!_panning) return;
            EndPan();
            e.Handled = true;
        };
        _surface.LostMouseCapture += (_, _) => EndPan();
        _surface.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || !_panning) return;
            EndPan();
            e.Handled = true;
        };
        Deactivated += (_, _) => EndPan();
        Closed += (_, _) => EndPan();
    }

    public void SetUpdating()
    {
        _save.IsEnabled = false;
        _status.Text = "Updating preview...";
    }

    public void SetFrame(PdfExporter.PreviewFrame frame, bool current)
    {
        PdfBytes = frame.PdfBytes;
        _image.Source = frame.Image;
        _save.IsEnabled = current;
        _status.Text = !current ? "Updating preview..." : string.IsNullOrWhiteSpace(frame.Warning)
            ? "Preview ready. Wheel: zoom. Right-drag: pan. PDF Output settings update live."
            : frame.Warning;
        ResizeImage();
    }

    public void SetError(string error)
    {
        _save.IsEnabled = false;
        _status.Text = error;
    }

    private static void AddButton(Panel panel, string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(4), MinWidth = 42 };
        button.Click += (_, _) => action();
        panel.Children.Add(button);
    }

    private Point SurfaceCenter => new(_surface.ActualWidth / 2, _surface.ActualHeight / 2);

    private void Zoom(double factor, Point anchor)
    {
        if (_image.Source == null || !double.IsFinite(factor) || factor <= 0) return;
        _fit = false;
        double next = Math.Clamp(_zoom * factor, 0.02, 20);
        double ratio = next / _zoom;
        _pan = (Vector)anchor - ((Vector)anchor - _pan) * ratio;
        _zoom = next;
        ResizeImage();
    }

    private void BeginPan(Point position)
    {
        if (_image.Source == null) return;
        _panning = true;
        _lastPanPoint = position;
        _surface.Cursor = Cursors.Hand;
    }

    private void MovePan(Point position)
    {
        if (!_panning) return;
        Vector delta = position - _lastPanPoint;
        if (delta.LengthSquared == 0) return;
        _fit = false;
        _pan += delta;
        _lastPanPoint = position;
        ResizeImage();
    }

    private void EndPan()
    {
        _panning = false;
        _surface.Cursor = null;
        if (_surface.IsMouseCaptured) _surface.ReleaseMouseCapture();
    }

    private void ResizeImage()
    {
        if (_image.Source is not { Width: > 0, Height: > 0 } source) return;
        if (_fit)
        {
            _zoom = Math.Min(Math.Max(1, _surface.ActualWidth - 24) / source.Width,
                Math.Max(1, _surface.ActualHeight - 24) / source.Height);
            _pan = new Vector((_surface.ActualWidth - source.Width * _zoom) / 2,
                (_surface.ActualHeight - source.Height * _zoom) / 2);
        }
        _image.Width = source.Width;
        _image.Height = source.Height;
        _transform.Matrix = new Matrix(_zoom, 0, 0, _zoom, _pan.X, _pan.Y);
    }
}
