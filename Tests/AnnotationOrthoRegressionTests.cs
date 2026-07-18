using OurPlanCore.Controls;
using SkiaSharp;

internal static class AnnotationOrthoRegressionTests
{
    public static void ApplyOrthoUsesDominantAxisAndHorizontalTie()
    {
        var anchor = new SKPoint(10f, 20f);

        AssertPoint(
            new SKPoint(18f, 20f),
            PdfViewport.ApplyOrtho(anchor, new SKPoint(18f, 23f)),
            "dominant horizontal delta");
        AssertPoint(
            new SKPoint(10f, 29f),
            PdfViewport.ApplyOrtho(anchor, new SKPoint(13f, 29f)),
            "dominant vertical delta");
        AssertPoint(
            new SKPoint(16f, 20f),
            PdfViewport.ApplyOrtho(anchor, new SKPoint(16f, 26f)),
            "equal deltas use the stable horizontal tie-break");
    }

    public static void DimensionScalePrefersPageAndFallsBack()
    {
        AssertClose(
            0.125,
            AnnotationGlyphRenderer.ResolveDimensionScale(0.125, 0.5),
            "current page scale must replace the annotation creation scale");
        AssertClose(
            0.5,
            AnnotationGlyphRenderer.ResolveDimensionScale(0, 0.5),
            "annotation scale remains the fallback when the page has no scale");
        AssertClose(
            0,
            AnnotationGlyphRenderer.ResolveDimensionScale(0, -0.5),
            "invalid stored scale must not become an effective scale");
    }

    public static void DrawLineFinalizesConnectedPolyline()
    {
        string tools = Read("Controls/PdfViewport.Tools.cs");
        int drawLineCase = tools.IndexOf("case ViewerTool.DrawLine:", StringComparison.Ordinal);
        int nextCase = tools.IndexOf("case ViewerTool.DrawArrow:", drawLineCase, StringComparison.Ordinal);
        AssertTrue(drawLineCase >= 0 && nextCase > drawLineCase, "Draw Line tool branch must exist");
        string drawLineBranch = tools[drawLineCase..nextCase];
        AssertTrue(
            drawLineBranch.Contains("_drawPts.Add(pdf);", StringComparison.Ordinal),
            "each Draw Line click must append another connected vertex");
        AssertFalse(
            drawLineBranch.Contains("AddTwoPointAnnotation", StringComparison.Ordinal),
            "Draw Line must not auto-finish after two points");

        string finalize = Read("Controls/PdfViewport.ScaleDrawTools.cs");
        AssertTrue(
            finalize.Contains("FinalizeAnnotation(\"line\", useAllPoints: true);", StringComparison.Ordinal),
            "Draw Line completion must persist all collected vertices");
        AssertTrue(
            finalize.Contains("? CleanFinalizePoints(_drawPts, closeArea: false)", StringComparison.Ordinal),
            "connected line completion must clean an open polyline without closing it");

        string input = Read("Controls/PdfViewport.Input.cs");
        AssertTrue(
            input.Contains("_tool is ViewerTool.Line or ViewerTool.Area or ViewerTool.DrawLine or ViewerTool.DrawArea", StringComparison.Ordinal),
            "double-click and C completion routing must include Draw Line");
    }

    public static void AnnotationClipboardKeepsGroupUndoAndPersistenceWiring()
    {
        string clipboard = Read("Controls/PdfViewport.AnnotationClipboard.cs");
        AssertTrue(
            clipboard.Contains("public bool CopyPageAnnotations(", StringComparison.Ordinal) &&
            clipboard.Contains("public bool PasteCopiedPageAnnotations(", StringComparison.Ordinal),
            "annotation clipboard must expose copy and paste operations");
        AssertTrue(
            clipboard.Contains("PushAddedAnnotationsUndo(pasted", StringComparison.Ordinal) &&
            clipboard.Contains("SetSelectedAnnotations(pasted", StringComparison.Ordinal),
            "a pasted annotation group must be one undoable selected operation");
        AssertTrue(
            clipboard.Contains("PageAnnotationAdded?.Invoke(annotation);", StringComparison.Ordinal),
            "each pasted annotation must flow through the existing persistence callback");
        AssertTrue(
            CountOccurrences(clipboard, "_holeClipboard.Clear();") >= 2,
            "copying measurements or markups must supersede an older cut-region clipboard payload");

        string input = Read("Controls/PdfViewport.Input.cs");
        AssertTrue(
            input.Contains("CopySelectedPageAnnotations()", StringComparison.Ordinal) &&
            input.Contains("IsCutRegionClipboardCurrent", StringComparison.Ordinal) &&
            input.Contains("IsAnnotationClipboardCurrent", StringComparison.Ordinal) &&
            input.Contains("PasteCopiedPageAnnotations(_lastPointerPdf)", StringComparison.Ordinal),
            "viewport Ctrl+C/Ctrl+V routing must honor the current annotation clipboard payload");

        string cutRegions = Read("Controls/PdfViewport.CutRegions.cs");
        AssertTrue(
            cutRegions.Contains("MarkCutRegionClipboardCurrent();", StringComparison.Ordinal),
            "copying a cut region must update the shared last-payload routing");
    }

    public static void ShiftOrthoUsesOrAndCoversAnnotationEditing()
    {
        string digitizer = Read("Controls/PdfViewport.DigitizerSnap.cs");
        AssertTrue(
            digitizer.Contains("return OrthoEnabled || shift;", StringComparison.Ordinal),
            "Shift must force Ortho even when F8 Ortho is already enabled");
        AssertFalse(
            digitizer.Contains("OrthoEnabled ^ shift", StringComparison.Ordinal),
            "Shift must never toggle an enabled Ortho constraint off");

        int resolver = digitizer.IndexOf("private SKPoint ResolveConstrainedPoint(", StringComparison.Ordinal);
        int ortho = digitizer.IndexOf("ApplyOrtho(", resolver, StringComparison.Ordinal);
        int snap = digitizer.IndexOf("TryFindDigitizerSnapPoint(constrained", resolver, StringComparison.Ordinal);
        AssertTrue(
            resolver >= 0 && ortho > resolver && snap > ortho,
            "Ortho projection must happen before snap lookup");
        AssertTrue(
            digitizer.Contains("private static bool IsSelectionModifierActive() =>", StringComparison.Ordinal) &&
            digitizer.Contains("(Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;", StringComparison.Ordinal),
            "plain Shift must remain available for Ortho instead of starting selection toggles");

        string input = Read("Controls/PdfViewport.Input.cs");
        AssertTrue(
            input.Contains("IsAnnotationSelfVertexSnap", StringComparison.Ordinal),
            "annotation vertex editing must use the constrained point resolver");
        AssertTrue(
            CountOccurrences(input, "ConstrainDragDeltaOrtho(ScreenDragDeltaToPdf(pos))") >= 2,
            "whole measurement and annotation moves must both use Shift Ortho");

        string roofGuides = Read("Controls/PdfViewport.RoofGuides.cs");
        AssertTrue(
            roofGuides.Contains("return ResolveConstrainedPoint(rawPdf, anchor, updatePreview);", StringComparison.Ordinal),
            "3D roof guide drawing must share the same snap-aware Ortho resolver");
    }

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int start = 0;
        while ((start = source.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += value.Length;
        }

        return count;
    }

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

    private static string RepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ourplancore.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("OurPlanCore repository root was not found.");
    }

    private static void AssertPoint(SKPoint expected, SKPoint actual, string message)
    {
        AssertClose(expected.X, actual.X, $"{message} X");
        AssertClose(expected.Y, actual.Y, $"{message} Y");
    }

    private static void AssertClose(double expected, double actual, string message)
    {
        if (Math.Abs(expected - actual) > 0.000001)
            throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message) => AssertTrue(!condition, message);
}
