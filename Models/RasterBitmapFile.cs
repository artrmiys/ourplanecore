using System.IO;
using SkiaSharp;

namespace OurPlanCore;

internal static class RasterBitmapFile
{
    public static SKBitmap? Decode(string path)
    {
        try
        {
            // The native filename overload fails beyond MAX_PATH on Windows.
            // Managed file I/O supports long project paths without copying the encoded file.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            return SKBitmap.Decode(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
