using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OurPlanCore;

public partial class MainWindow
{
    // Opt-in visual evidence for the existing page stress harness; disabled in normal use.
    private void CaptureViewportSmokeImage(PageInfo page, string stage)
    {
        string? folder = Environment.GetEnvironmentVariable("OURPLANCORE_VIEWPORT_SMOKE_IMAGES");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        string name = new(page.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).Take(100).ToArray());
        string path = Path.Combine(folder, name + "-" + stage + ".png");
        if (File.Exists(path)) return;
        int width = (int)ViewportSurfaceHost.ActualWidth, height = (int)ViewportSurfaceHost.ActualHeight;
        if (width < 2 || height < 2) return;
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(ViewportSurfaceHost);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
}
