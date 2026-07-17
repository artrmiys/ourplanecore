using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OurPlanCore;

internal sealed class PageBookmarkCropPreviewDialog : Window
{
    public PageBookmarkCropPreviewDialog(string bookmarkName, string imagePath)
    {
        BitmapSource image = LoadImage(imagePath);

        Title = $"Crop Preview - {bookmarkName}";
        Width = 760;
        Height = 560;
        MinWidth = 380;
        MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;

        var root = new Grid
        {
            Margin = new Thickness(8),
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = root;

        var heading = new TextBlock
        {
            Text = bookmarkName,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 0, 2, 6),
        };
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var imageHost = new Border
        {
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            Child = new Image
            {
                Source = image,
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.Both,
                SnapsToDevicePixels = true,
            },
        };
        imageHost.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        imageHost.SetResourceReference(Border.BackgroundProperty, "PanelBackgroundBrush");
        Grid.SetRow(imageHost, 1);
        root.Children.Add(imageHost);

        var footer = new Grid
        {
            Margin = new Thickness(2, 7, 2, 0),
        };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        var details = new TextBlock
        {
            Text = $"{image.PixelWidth} x {image.PixelHeight} px  |  {Path.GetFileName(imagePath)}",
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 12, 0),
        };
        details.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryForegroundBrush");
        Grid.SetColumn(details, 0);
        footer.Children.Add(details);

        var close = new Button
        {
            Content = "Close",
            Width = 72,
            Height = 25,
            IsDefault = true,
            IsCancel = true,
        };
        Grid.SetColumn(close, 1);
        footer.Children.Add(close);

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        };
    }

    private static BitmapSource LoadImage(string imagePath)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        image.UriSource = new Uri(Path.GetFullPath(imagePath), UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }
}
