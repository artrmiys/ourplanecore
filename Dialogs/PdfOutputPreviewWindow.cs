using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OurPlanCore;

public sealed class PdfOutputPreviewWindow : Window
{
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly ScrollViewer _scroll = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Background = new SolidColorBrush(Color.FromRgb(65, 65, 65)),
    };
    private readonly TextBlock _status = new() { Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap };
    private readonly Button _save = new() { Content = "Save PDF...", Margin = new Thickness(4), IsEnabled = false };
    private double _zoom = 1;
    private bool _fit = true;

    public event Action? SaveRequested;
    public byte[]? PdfBytes { get; private set; }

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
        AddButton(toolbar, "-", () => Zoom(0.8));
        AddButton(toolbar, "+", () => Zoom(1.25));
        AddButton(toolbar, "100%", () => { _fit = false; _zoom = 1; ResizeImage(); });
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
        _scroll.Content = _image;
        root.Children.Add(_scroll);
        Content = root;
        _scroll.SizeChanged += (_, _) => { if (_fit) ResizeImage(); };
        _scroll.PreviewMouseWheel += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            Zoom(e.Delta > 0 ? 1.25 : 0.8);
            e.Handled = true;
        };
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
            ? "Preview ready. Change PDF Output settings to update it. Ctrl + wheel to zoom."
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

    private void Zoom(double factor)
    {
        _fit = false;
        _zoom = Math.Clamp(_zoom * factor, 0.1, 5);
        ResizeImage();
    }

    private void ResizeImage()
    {
        if (_image.Source is not { Width: > 0, Height: > 0 } source) return;
        if (_fit)
            _zoom = Math.Min(Math.Max(100, _scroll.ActualWidth - 22) / source.Width,
                Math.Max(100, _scroll.ActualHeight - 22) / source.Height);
        _image.Width = source.Width * _zoom;
        _image.Height = source.Height * _zoom;
    }
}
