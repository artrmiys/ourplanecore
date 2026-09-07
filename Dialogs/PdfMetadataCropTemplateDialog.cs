using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SkiaSharp;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace OurPlanCore.Controls;

public sealed class PdfMetadataCropTemplateDialog : Window
{
    private enum CropRole
    {
        SheetNumber,
        SheetTitle,
        Scale,
    }

    private readonly PageInfo _page;
    private readonly PdfLayerRenderResult _render;
    private readonly BitmapSource _bitmap;
    private readonly PdfSheetMetadataCropProfile _profile;
    private readonly bool _guidedNameAndScale;
    private bool _guidedProfileChosen;
    private readonly Canvas _canvas;
    private readonly ScrollViewer _scrollViewer;
    private readonly WpfRectangle _sheetNumberOutline;
    private readonly WpfRectangle _sheetTitleOutline;
    private readonly WpfRectangle _scaleOutline;
    private readonly WpfRectangle _dragOutline;
    private RadioButton _scaleChoice = null!;
    private TextBlock _status = null!;
    private CropRole _activeRole = CropRole.SheetNumber;
    private Point? _dragStart;
    private Point? _panStart;
    private double _panHorizontalStart;
    private double _panVerticalStart;
    private SKRect? _sheetNumberRect;
    private SKRect? _sheetTitleRect;
    private SKRect? _scaleRect;

    public PdfSheetMetadataCropTemplate CropTemplate { get; private set; } = new();
    public PdfSheetMetadataCropProfile SelectedProfile { get; private set; }

    public PdfMetadataCropTemplateDialog(
        PageInfo page,
        PdfLayerRenderResult render,
        PdfSheetMetadataCropTemplate? existingTemplate)
        : this(
            page,
            render,
            existingTemplate,
            PdfSheetMetadataCropProfile.Default,
            guidedNameAndScale: false)
    {
    }

    public PdfMetadataCropTemplateDialog(
        PageInfo page,
        PdfLayerRenderResult render,
        PdfSheetMetadataCropTemplate? existingTemplate,
        PdfSheetMetadataCropProfile profile,
        bool guidedNameAndScale)
    {
        _page = page;
        _render = render;
        _bitmap = LoadBitmap(render.ImageBytes);
        _profile = profile;
        _guidedNameAndScale = guidedNameAndScale;
        SelectedProfile = profile;
        _guidedProfileChosen = profile != PdfSheetMetadataCropProfile.Default;

        if (existingTemplate != null)
        {
            if (HasRegion(existingTemplate.SheetNumberRect))
                _sheetNumberRect = PdfSheetMetadataCropService.RectFromRegion(existingTemplate.SheetNumberRect);
            if (HasRegion(existingTemplate.SheetTitleRect))
                _sheetTitleRect = PdfSheetMetadataCropService.RectFromRegion(existingTemplate.SheetTitleRect);
            if (HasRegion(existingTemplate.ScaleRect))
                _scaleRect = PdfSheetMetadataCropService.RectFromRegion(existingTemplate.ScaleRect);
        }

        Title = guidedNameAndScale
            ? profile == PdfSheetMetadataCropProfile.Default
                ? "Sheet Metadata Layout — Choose A / S / Other"
                : $"Sheet Metadata Layout — {PdfSheetMetadataCropService.ProfileDisplayName(profile)}"
            : "Sheet Metadata Layout Regions";
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
        _sheetTitleOutline = BuildOutline(Brushes.MediumPurple);
        _scaleOutline = BuildOutline(Brushes.Orange);
        _dragOutline = BuildOutline(Brushes.LimeGreen);
        _dragOutline.Visibility = Visibility.Collapsed;
        _canvas.Children.Add(_sheetNumberOutline);
        _canvas.Children.Add(_sheetTitleOutline);
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
            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(ScrollTowardTitleBlock));
        };
    }

    private StackPanel BuildTopPanel()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(new TextBlock
        {
            Text = _guidedNameAndScale
                ? "Two quick steps: first draw one box around the complete sheet title and drawing number (for example, FOURTH LEVEL FLOOR PLAN + A1.04), then draw the scale. Left-drag draws; right/middle-drag pans."
                : "Draw reusable Sheet #, Sheet Title, and Scale regions on a representative sheet. Left-drag draws; right/middle-drag pans.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        });

        if (_guidedNameAndScale)
            panel.Children.Add(BuildGuidedProfilePanel());

        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        var sheetNumberChoice = new RadioButton
        {
            Content = _guidedNameAndScale ? "1. Sheet title + number" : "Sheet #",
            IsChecked = true,
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        sheetNumberChoice.Checked += (_, _) =>
        {
            _activeRole = CropRole.SheetNumber;
            UpdateStatus();
        };
        tools.Children.Add(sheetNumberChoice);

        if (!_guidedNameAndScale)
        {
            var title = new RadioButton
            {
                Content = "Sheet Title",
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            title.Checked += (_, _) =>
            {
                _activeRole = CropRole.SheetTitle;
                UpdateStatus();
            };
            tools.Children.Add(title);
        }

        _scaleChoice = new RadioButton
        {
            Content = _guidedNameAndScale ? "2. Scale" : "Scale",
            Margin = new Thickness(0, 0, 18, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _scaleChoice.Checked += (_, _) =>
        {
            _activeRole = CropRole.Scale;
            UpdateStatus();
        };
        tools.Children.Add(_scaleChoice);

        _status = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        tools.Children.Add(_status);
        panel.Children.Add(tools);
        return panel;
    }

    private FrameworkElement BuildGuidedProfilePanel()
    {
        if (_profile != PdfSheetMetadataCropProfile.Default)
        {
            return new TextBlock
            {
                Text = $"Layout profile: {PdfSheetMetadataCropService.ProfileDisplayName(_profile)}",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6),
            };
        }

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 6),
        };
        panel.Children.Add(new TextBlock
        {
            Text = "This sheet is:",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        });
        AddChoice("Architectural (A)", PdfSheetMetadataCropProfile.Architectural);
        AddChoice("Structural (S)", PdfSheetMetadataCropProfile.Structural);
        AddChoice("Other / general", PdfSheetMetadataCropProfile.Default);
        return panel;

        void AddChoice(string label, PdfSheetMetadataCropProfile profile)
        {
            var choice = new RadioButton
            {
                Content = label,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            choice.Checked += (_, _) =>
            {
                SelectedProfile = profile;
                _guidedProfileChosen = true;
                UpdateStatus();
            };
            panel.Children.Add(choice);
        }
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
            else if (_activeRole == CropRole.SheetTitle)
                _sheetTitleRect = null;
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
            if (!_guidedNameAndScale)
                _sheetTitleRect = null;
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
        {
            _sheetNumberRect = pdfRect;
            if (_guidedNameAndScale)
                _scaleChoice.IsChecked = true;
        }
        else if (_activeRole == CropRole.SheetTitle)
            _sheetTitleRect = pdfRect;
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
        if (_guidedNameAndScale && !_guidedProfileChosen)
        {
            MessageBox.Show(
                this,
                "Choose whether this sheet is Architectural (A), Structural (S), or Other / general.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (_guidedNameAndScale && (_sheetNumberRect == null || _scaleRect == null))
        {
            MessageBox.Show(
                this,
                "Complete both steps before saving: draw the sheet title + number region and the scale region.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (_sheetNumberRect == null && _sheetTitleRect == null && _scaleRect == null)
        {
            MessageBox.Show(this, "Draw at least one crop box before saving.", "Sheet Metadata Layout Regions",
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
            SheetTitleRect = _guidedNameAndScale
                ? PdfSheetMetadataCropService.RegionFromRect(_sheetNumberRect!.Value)
                : _sheetTitleRect == null
                    ? new PdfSheetMetadataCropRegion()
                    : PdfSheetMetadataCropService.RegionFromRect(_sheetTitleRect.Value),
            ScaleRect = _scaleRect == null
                ? new PdfSheetMetadataCropRegion()
                : PdfSheetMetadataCropService.RegionFromRect(_scaleRect.Value),
        };
        DialogResult = true;
    }

    private void RenderOutlines()
    {
        RenderOutline(_sheetNumberOutline, _sheetNumberRect);
        if (_guidedNameAndScale)
            _sheetTitleOutline.Visibility = Visibility.Collapsed;
        else
            RenderOutline(_sheetTitleOutline, _sheetTitleRect);
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
        if (_guidedNameAndScale)
        {
            string sheetStep = _sheetNumberRect == null ? "step 1 waiting" : "step 1 set";
            string scaleStep = _scaleRect == null ? "step 2 waiting" : "step 2 set";
            string activeStep = _activeRole == CropRole.SheetNumber ? "sheet title + number" : "scale";
            _status.Text = $"Active: {activeStep}. {sheetStep}; {scaleStep}.";
            return;
        }

        string active = _activeRole switch
        {
            CropRole.SheetNumber => "sheet number",
            CropRole.SheetTitle => "sheet title",
            _ => "scale",
        };
        string sheet = _sheetNumberRect == null ? "sheet # missing" : "sheet # set";
        string title = _sheetTitleRect == null ? "title missing" : "title set";
        string scale = _scaleRect == null ? "scale missing" : "scale set";
        _status.Text = $"Active: {active}. {sheet}; {title}; {scale}. Left-drag draws; right/middle-drag pans; Shift+wheel scrolls sideways.";
    }

    private void ScrollTowardTitleBlock()
    {
        _scrollViewer.UpdateLayout();
        _scrollViewer.ScrollToHorizontalOffset(_scrollViewer.ScrollableWidth);
        _scrollViewer.ScrollToVerticalOffset(_scrollViewer.ScrollableHeight);
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
