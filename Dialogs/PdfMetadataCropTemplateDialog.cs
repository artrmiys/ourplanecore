using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace OurPlanCore.Controls;

public sealed class PdfMetadataCropTemplateDialog : Window
{
    private enum CropRole
    {
        SheetNumber,
        Scale,
    }

    private readonly PageInfo _page;
    private readonly PdfLayerRenderResult _render;
    private readonly BitmapSource _bitmap;
    private readonly Canvas _canvas;
    private readonly ScrollViewer _scrollViewer;
    private readonly WpfRectangle _sheetNumberOutline;
    private readonly WpfRectangle _scaleOutline;
    private readonly WpfRectangle _dragOutline;
    private TextBlock _status = null!;
    private CropRole _activeRole = CropRole.SheetNumber;
    private Point? _dragStart;
    private Point? _panStart;
    private double _panHorizontalStart;
    private double _panVerticalStart;
    private SKRect? _sheetNumberRect;
    private SKRect? _scaleRect;

    public PdfSheetMetadataCropTemplate CropTemplate { get; private set; } = new();

    public PdfMetadataCropTemplateDialog(
        PageInfo page,
        PdfLayerRenderResult render,
        PdfSheetMetadataCropTemplate? existingTemplate)
    {
        _page = page;
        _render = render;
        _bitmap = LoadBitmap(render.ImageBytes);

        if (existingTemplate != null)
        {
            if (HasRegion(existingTemplate.SheetNumberRect))
                _sheetNumberRect = PdfSheetMetadataCropService.RectFromRegion(existingTemplate.SheetNumberRect);
            if (HasRegion(existingTemplate.ScaleRect))
                _scaleRect = PdfSheetMetadataCropService.RectFromRegion(existingTemplate.ScaleRect);
        }

        Title = "AI Fill Crop Hints";
        Width = 1120;
        Height = 780;
        MinWidth = 760;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;

        var root = new DockPanel { Margin = new Thickness(12) };
        Content = root;

        var top = BuildTopPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var buttons = BuildBottomButtons();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var host = new Grid
        {
            Width = _bitmap.PixelWidth,
            Height = _bitmap.PixelHeight,
            Background = Brushes.White,
        };
        host.Children.Add(new Image
        {
            Source = _bitmap,
            Width = _bitmap.PixelWidth,
            Height = _bitmap.PixelHeight,
            Stretch = Stretch.Fill,
        });

        _canvas = new Canvas
        {
            Width = _bitmap.PixelWidth,
            Height = _bitmap.PixelHeight,
            Background = Brushes.Transparent,
        };
        host.Children.Add(_canvas);

        _sheetNumberOutline = BuildOutline(Brushes.DeepSkyBlue);
        _scaleOutline = BuildOutline(Brushes.Orange);
        _dragOutline = BuildOutline(Brushes.LimeGreen);
        _dragOutline.Visibility = Visibility.Collapsed;
        _canvas.Children.Add(_sheetNumberOutline);
        _canvas.Children.Add(_scaleOutline);
        _canvas.Children.Add(_dragOutline);

        _canvas.MouseLeftButtonDown += OnMouseLeftButtonDown;
        _canvas.MouseMove += OnMouseMove;
        _canvas.MouseLeftButtonUp += OnMouseLeftButtonUp;
        _canvas.MouseLeave += OnMouseLeave;

        _scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = host,
        };
        _scrollViewer.PreviewMouseDown += OnPanPreviewMouseDown;
        _scrollViewer.PreviewMouseMove += OnPanPreviewMouseMove;
        _scrollViewer.PreviewMouseUp += OnPanPreviewMouseUp;
        _scrollViewer.PreviewMouseWheel += OnPreviewMouseWheel;
        root.Children.Add(_scrollViewer);

        Loaded += (_, _) =>
        {
            RenderOutlines();
            UpdateStatus();
        };
    }

    private StackPanel BuildTopPanel()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(new TextBlock
        {
            Text = "Draw crop boxes on the representative sheet. Left-drag draws a crop; right-drag or middle-drag moves the sheet.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        });

        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        var sheet = new RadioButton
        {
            Content = "Sheet #",
            IsChecked = true,
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        sheet.Checked += (_, _) =>
        {
            _activeRole = CropRole.SheetNumber;
            UpdateStatus();
        };
        tools.Children.Add(sheet);

        var scale = new RadioButton
        {
            Content = "Scale",
            Margin = new Thickness(0, 0, 18, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        scale.Checked += (_, _) =>
        {
            _activeRole = CropRole.Scale;
            UpdateStatus();
        };
        tools.Children.Add(scale);

        _status = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        tools.Children.Add(_status);
        panel.Children.Add(tools);
        return panel;
    }

    private StackPanel BuildBottomButtons()
    {
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };

        var clearCurrent = new Button { Content = "Clear Current", MinWidth = 96, Margin = new Thickness(0, 0, 8, 0) };
        clearCurrent.Click += (_, _) =>
        {
            if (_activeRole == CropRole.SheetNumber)
                _sheetNumberRect = null;
            else
                _scaleRect = null;

            RenderOutlines();
            UpdateStatus();
        };
        buttons.Children.Add(clearCurrent);

        var clearAll = new Button { Content = "Clear All", MinWidth = 78, Margin = new Thickness(0, 0, 18, 0) };
        clearAll.Click += (_, _) =>
        {
            _sheetNumberRect = null;
            _scaleRect = null;
            RenderOutlines();
            UpdateStatus();
        };
        buttons.Children.Add(clearAll);

        var save = new Button { Content = "Save", MinWidth = 78, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        save.Click += (_, _) => Accept();
        buttons.Children.Add(save);

        buttons.Children.Add(new Button { Content = "Cancel", MinWidth = 78, IsCancel = true });
        return buttons;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_panStart != null)
            return;

        _dragStart = ClampPoint(e.GetPosition(_canvas));
        PositionOutline(_dragOutline, RectFromPoints(_dragStart.Value, _dragStart.Value));
        _dragOutline.Visibility = Visibility.Visible;
        _canvas.CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_panStart != null)
        {
            UpdatePan(e);
            e.Handled = true;
            return;
        }

        if (_dragStart == null)
            return;

        Point current = ClampPoint(e.GetPosition(_canvas));
        PositionOutline(_dragOutline, RectFromPoints(_dragStart.Value, current));
        e.Handled = true;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart == null)
            return;

        Point end = ClampPoint(e.GetPosition(_canvas));
        Rect pixelRect = RectFromPoints(_dragStart.Value, end);
        _dragStart = null;
        _dragOutline.Visibility = Visibility.Collapsed;
        _canvas.ReleaseMouseCapture();
        e.Handled = true;

        if (pixelRect.Width < 6 || pixelRect.Height < 6)
        {
            _status.Text = "Crop is too small. Drag a larger box.";
            return;
        }

        SKRect pdfRect = PixelRectToPdfRect(pixelRect);
        if (_activeRole == CropRole.SheetNumber)
            _sheetNumberRect = pdfRect;
        else
            _scaleRect = pdfRect;

        RenderOutlines();
        UpdateStatus();
    }

    private void OnPanPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is MouseButton.Right or MouseButton.Middle)
            BeginPan(e);
    }

    private void OnPanPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_panStart == null)
            return;

        if (e.RightButton != MouseButtonState.Pressed &&
            e.MiddleButton != MouseButtonState.Pressed)
        {
            EndPan(e);
            return;
        }

        UpdatePan(e);
        e.Handled = true;
    }

    private void OnPanPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is MouseButton.Right or MouseButton.Middle)
            EndPan(e);
    }

    private void BeginPan(MouseButtonEventArgs e)
    {
        if (_dragStart != null)
            return;

        _panStart = e.GetPosition(_scrollViewer);
        _panHorizontalStart = _scrollViewer.HorizontalOffset;
        _panVerticalStart = _scrollViewer.VerticalOffset;
        _canvas.Cursor = Cursors.ScrollAll;
        _scrollViewer.Cursor = Cursors.ScrollAll;
        _scrollViewer.CaptureMouse();
        e.Handled = true;
    }

    private void UpdatePan(MouseEventArgs e)
    {
        if (_panStart == null)
            return;

        Point panCurrent = e.GetPosition(_scrollViewer);
        _scrollViewer.ScrollToHorizontalOffset(_panHorizontalStart - (panCurrent.X - _panStart.Value.X));
        _scrollViewer.ScrollToVerticalOffset(_panVerticalStart - (panCurrent.Y - _panStart.Value.Y));
    }

    private void EndPan(MouseEventArgs e)
    {
        if (_panStart == null)
            return;

        _panStart = null;
        _canvas.Cursor = null;
        _scrollViewer.Cursor = null;
        if (_scrollViewer.IsMouseCaptured)
            _scrollViewer.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
            return;

        _scrollViewer.ScrollToHorizontalOffset(_scrollViewer.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (_panStart != null)
            return;

        if (_dragStart == null || e.LeftButton == MouseButtonState.Pressed)
            return;

        _dragStart = null;
        _dragOutline.Visibility = Visibility.Collapsed;
        _canvas.ReleaseMouseCapture();
    }

    private void Accept()
    {
        if (_sheetNumberRect == null && _scaleRect == null)
        {
            MessageBox.Show(this, "Draw at least one crop box before saving.", "AI Fill Crop Hints",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        CropTemplate = new PdfSheetMetadataCropTemplate
        {
            SourcePageName = _page.Name,
            SourcePageFolder = _page.FolderPath,
            PageWidthPt = _render.WidthPt,
            PageHeightPt = _render.HeightPt,
            SheetNumberRect = _sheetNumberRect == null
                ? new PdfSheetMetadataCropRegion()
                : PdfSheetMetadataCropService.RegionFromRect(_sheetNumberRect.Value),
            ScaleRect = _scaleRect == null
                ? new PdfSheetMetadataCropRegion()
                : PdfSheetMetadataCropService.RegionFromRect(_scaleRect.Value),
        };
        DialogResult = true;
    }

    private void RenderOutlines()
    {
        RenderOutline(_sheetNumberOutline, _sheetNumberRect);
        RenderOutline(_scaleOutline, _scaleRect);
    }

    private void RenderOutline(WpfRectangle outline, SKRect? rect)
    {
        if (rect == null)
        {
            outline.Visibility = Visibility.Collapsed;
            return;
        }

        outline.Visibility = Visibility.Visible;
        PositionOutline(outline, PdfRectToPixelRect(rect.Value));
    }

    private void UpdateStatus()
    {
        string active = _activeRole == CropRole.SheetNumber ? "sheet number" : "scale";
        string sheet = _sheetNumberRect == null ? "sheet # missing" : "sheet # set";
        string scale = _scaleRect == null ? "scale missing" : "scale set";
        _status.Text = $"Active: {active}. {sheet}; {scale}. Left-drag draws; right/middle-drag pans; Shift+wheel scrolls sideways.";
    }

    private SKRect PixelRectToPdfRect(Rect pixel)
    {
        float left = (float)(pixel.Left / _bitmap.PixelWidth * _render.WidthPt);
        float top = (float)(pixel.Top / _bitmap.PixelHeight * _render.HeightPt);
        float right = (float)(pixel.Right / _bitmap.PixelWidth * _render.WidthPt);
        float bottom = (float)(pixel.Bottom / _bitmap.PixelHeight * _render.HeightPt);
        return new SKRect(left, top, right, bottom);
    }

    private Rect PdfRectToPixelRect(SKRect pdf)
    {
        double left = Math.Clamp(pdf.Left, 0, _render.WidthPt) / _render.WidthPt * _bitmap.PixelWidth;
        double top = Math.Clamp(pdf.Top, 0, _render.HeightPt) / _render.HeightPt * _bitmap.PixelHeight;
        double right = Math.Clamp(pdf.Right, 0, _render.WidthPt) / _render.WidthPt * _bitmap.PixelWidth;
        double bottom = Math.Clamp(pdf.Bottom, 0, _render.HeightPt) / _render.HeightPt * _bitmap.PixelHeight;
        return new Rect(new Point(left, top), new Point(right, bottom));
    }

    private Point ClampPoint(Point point) =>
        new(
            Math.Clamp(point.X, 0, _bitmap.PixelWidth),
            Math.Clamp(point.Y, 0, _bitmap.PixelHeight));

    private static Rect RectFromPoints(Point a, Point b) =>
        new(a, b);

    private static void PositionOutline(WpfRectangle outline, Rect rect)
    {
        Canvas.SetLeft(outline, rect.Left);
        Canvas.SetTop(outline, rect.Top);
        outline.Width = Math.Max(0, rect.Width);
        outline.Height = Math.Max(0, rect.Height);
    }

    private static WpfRectangle BuildOutline(Brush stroke) =>
        new()
        {
            Stroke = stroke,
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(28, 0, 160, 255)),
            IsHitTestVisible = false,
        };

    private static BitmapSource LoadBitmap(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static bool HasRegion(PdfSheetMetadataCropRegion region) =>
        Math.Abs(region.Right - region.Left) >= 1 &&
        Math.Abs(region.Bottom - region.Top) >= 1;
}
