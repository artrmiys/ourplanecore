using System;

namespace OurPlaneCore;

public sealed record SheetOverlayTransformValues(
    double OffsetXPt,
    double OffsetYPt,
    double OverlayScale,
    double OverlayRotationDegrees);

public static class SheetOverlayReciprocalService
{
    public static bool TrySync(PageInfo page, out string reciprocalPageFolder)
    {
        reciprocalPageFolder = "";
        if (string.IsNullOrWhiteSpace(page.OverlayPageFolder))
            return false;

        PageInfo? overlayPage = OurPlaneCoreJobStore.TryReadPage(page.OverlayPageFolder);
        if (overlayPage == null || !ShouldWriteReciprocal(overlayPage, page.FolderPath))
            return false;

        SheetOverlayTransformValues reciprocal = Invert(
            page.OverlayOffsetXPt,
            page.OverlayOffsetYPt,
            page.OverlayScale,
            page.OverlayRotationDegrees);

        OurPlaneCoreJobStore.SavePageOverlay(
            overlayPage.FolderPath,
            page.FolderPath,
            page.OverlayColor,
            page.OverlayOpacity);
        OurPlaneCoreJobStore.SavePageOverlayVisibility(overlayPage.FolderPath, page.OverlayVisible);
        OurPlaneCoreJobStore.SavePageOverlayTransform(
            overlayPage.FolderPath,
            reciprocal.OffsetXPt,
            reciprocal.OffsetYPt,
            reciprocal.OverlayScale,
            reciprocal.OverlayRotationDegrees);

        reciprocalPageFolder = overlayPage.FolderPath;
        return true;
    }

    public static bool TryClear(PageInfo page, out string reciprocalPageFolder)
    {
        reciprocalPageFolder = "";
        if (string.IsNullOrWhiteSpace(page.OverlayPageFolder))
            return false;

        PageInfo? overlayPage = OurPlaneCoreJobStore.TryReadPage(page.OverlayPageFolder);
        if (overlayPage == null || !IsReciprocalOf(overlayPage, page.FolderPath))
            return false;

        OurPlaneCoreJobStore.ClearPageOverlay(overlayPage.FolderPath);
        reciprocalPageFolder = overlayPage.FolderPath;
        return true;
    }

    public static SheetOverlayTransformValues Invert(
        double offsetXPt,
        double offsetYPt,
        double overlayScale,
        double overlayRotationDegrees)
    {
        double scale = NormalizeScale(overlayScale);
        double inverseScale = NormalizeScale(1.0 / scale);
        double inverseRotation = NormalizeRotation(-overlayRotationDegrees);
        double radians = inverseRotation * Math.PI / 180.0;
        double cos = Math.Cos(radians);
        double sin = Math.Sin(radians);
        double inverseOffsetX = -(offsetXPt * cos - offsetYPt * sin) / scale;
        double inverseOffsetY = -(offsetXPt * sin + offsetYPt * cos) / scale;

        return new SheetOverlayTransformValues(
            NormalizeOffset(inverseOffsetX),
            NormalizeOffset(inverseOffsetY),
            inverseScale,
            inverseRotation);
    }

    public static bool ShouldWriteReciprocal(PageInfo reciprocalPage, string basePageFolder)
    {
        if (string.IsNullOrWhiteSpace(reciprocalPage.OverlayPageFolder))
            return true;

        return SameFolder(reciprocalPage.OverlayPageFolder, basePageFolder);
    }

    public static bool IsReciprocalOf(PageInfo page, string overlayPageFolder) =>
        !string.IsNullOrWhiteSpace(page.OverlayPageFolder) &&
        SameFolder(page.OverlayPageFolder, overlayPageFolder);

    public static bool SameFolder(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;

        return string.Equals(
            NormalizePath(a),
            NormalizePath(b),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return System.IO.Path.GetFullPath(path)
                .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        }
    }

    private static double NormalizeOffset(double offset) =>
        double.IsNaN(offset) || double.IsInfinity(offset)
            ? 0
            : Math.Clamp(offset, -100000, 100000);

    private static double NormalizeScale(double scale) =>
        double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0
            ? 1.0
            : Math.Clamp(scale, 0.05, 20.0);

    private static double NormalizeRotation(double degrees)
    {
        if (double.IsNaN(degrees) || double.IsInfinity(degrees))
            return 0;

        double normalized = degrees % 360.0;
        if (normalized > 180.0)
            normalized -= 360.0;
        if (normalized <= -180.0)
            normalized += 360.0;
        return Math.Round(normalized, 6);
    }
}
