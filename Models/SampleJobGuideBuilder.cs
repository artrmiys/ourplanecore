using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SkiaSharp;

namespace OurPlaneCore;

internal static class SampleJobGuideBuilder
{
    public const string GuideFolderName = "00. Guide";

    private const float PageWidth = 792f;
    private const float PageHeight = 612f;
    private const float Margin = 42f;
    private const int ScreenshotWidth = 1280;
    private const int ScreenshotHeight = 720;

    private static readonly SKColor Ink = new(31, 41, 55);
    private static readonly SKColor Muted = new(91, 103, 125);
    private static readonly SKColor Line = new(148, 163, 184);
    private static readonly SKColor Surface = new(248, 250, 252);
    private static readonly SKColor Panel = new(235, 241, 247);
    private static readonly SKColor Accent = new(37, 99, 235);
    private static readonly SKColor Purple = new(126, 87, 194);

    public static IReadOnlyList<string> PageNames { get; } =
    [
        "00 Start Here - Workspace Map",
        "01 Job and PDF Workflow",
        "02 Pages Tree and PDF Layers",
        "03 Takeoffs Tree and Recording",
        "04 Viewport Tools and Editing",
        "05 Managers Export AI Settings",
        "A101 Sample Plan"
    ];

    public static void WriteGuidePdf(string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        using var stream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        using SKDocument document = SKDocument.CreatePdf(stream);
        using var writer = new GuidePdfWriter(document);
        writer.Write();
    }

    public static void WriteGuideFiles(OurPlaneCoreJob job)
    {
        string guideRoot = GuideRoot(job);
        string screenshots = GuideScreenshotsFolder(job);
        Directory.CreateDirectory(screenshots);

        WriteScreenshot(Path.Combine(screenshots, "01-main-workspace.png"), "Main workspace map", DrawWorkspaceMap);
        WriteScreenshot(Path.Combine(screenshots, "02-pages-and-layers.png"), "Pages tree and PDF layers", DrawPagesLayersMap);
        WriteScreenshot(Path.Combine(screenshots, "03-takeoffs-and-tools.png"), "Takeoffs tree and recording", DrawTakeoffsMap);
        WriteScreenshot(Path.Combine(screenshots, "04-viewport-tools.png"), "Viewport tools and editing", DrawViewportToolsMap);
        WriteScreenshot(Path.Combine(screenshots, "05-managers-settings-ai-3d.png"), "Managers, export, AI, 3D, settings", DrawManagersMap);

        File.WriteAllText(Path.Combine(guideRoot, "README.md"), BuildGuideMarkdown(), Encoding.UTF8);
    }

    private static string GuideRoot(OurPlaneCoreJob job) =>
        Path.Combine(job.AIContextRoot, "guide");

    private static string GuideScreenshotsFolder(OurPlaneCoreJob job) =>
        Path.Combine(GuideRoot(job), "screenshots");

    private static string BuildGuideMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# OurPlaneCore Guide Sample");
        sb.AppendLine();
        sb.AppendLine("Open Pages > 00. Guide first. The PDF pages are a quick tour; A101 Sample Plan has preloaded line, area, count, joist, and roof-guide takeoffs.");
        sb.AppendLine();
        sb.AppendLine("## Screenshots");
        sb.AppendLine();
        sb.AppendLine("![Main workspace](screenshots/01-main-workspace.png)");
        sb.AppendLine("![Pages and layers](screenshots/02-pages-and-layers.png)");
        sb.AppendLine("![Takeoffs and tools](screenshots/03-takeoffs-and-tools.png)");
        sb.AppendLine("![Viewport tools](screenshots/04-viewport-tools.png)");
        sb.AppendLine("![Managers and settings](screenshots/05-managers-settings-ai-3d.png)");
        sb.AppendLine();
        sb.AppendLine("## Button Map");
        sb.AppendLine();
        sb.AppendLine("- Main tab: Open / Import, PlanSwift, Export, Name, Scale, Name+Scale, AI Fill, Crop Hints.");
        sb.AppendLine("- Page tab: Add Pages, Batch Rename, rotate, flip, invert, crop new page, copy rendered page, set/offset origin, close page.");
        sb.AppendLine("- Viewport tab: line/point/area display, labels, legend, header, units, page background, dark theme.");
        sb.AppendLine("- Main toolbar: Pan, Select, Scale, Ruler, Annotation, Count, Line, Area, J Area, Beam, Openings, Cut, Snap, PDF Snap, Ortho, Box, Fit, zoom, transform, scale presets, AI Settings.");
        sb.AppendLine("- Pages panel: R refresh, collapse, expand, tabs, detach, Tile M2, Sort A/S, D/Sec/WT, Repair, Name / Scale, New Folder, Auto Folders, search, PDF Layers.");
        sb.AppendLine("- Takeoffs panel: R refresh, collapse, expand, active target Record/More/Props/Find/Sheet/previous/next, New Folder, New Item, Roof Base, Export, Current Excel, Auto Tree, From Pages, search.");
        sb.AppendLine("- Workspace tabs: Main View, Sheet Manager, Takeoff Manager, Report Builder, Materials, AI Manager, 3D, Settings.");
        sb.AppendLine();
        sb.AppendLine("## Try This");
        sb.AppendLine();
        sb.AppendLine("1. Open A101 Sample Plan and select each sample takeoff to see sheet legend, totals, and viewport selection.");
        sb.AppendLine("2. Use the new Takeoffs R button after editing files or moving folders outside the app.");
        sb.AppendLine("3. Open Takeoff Manager and Estimating/Export to verify quantities and prices.");
        sb.AppendLine("4. Try Ctrl+Shift+P and search for commands by name instead of hunting for buttons.");
        return sb.ToString();
    }

    private static void WriteScreenshot(string path, string title, Action<SKCanvas, SKRect> draw)
    {
        using var bitmap = new SKBitmap(ScreenshotWidth, ScreenshotHeight);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        using var titlePaint = Paint(28, true, Ink);
        using var smallPaint = Paint(16, false, Muted);
        canvas.DrawText(title, 34, 44, titlePaint);
        canvas.DrawText("Generated guide screen for the OurPlaneCore sample project.", 34, 70, smallPaint);

        var content = new SKRect(34, 96, ScreenshotWidth - 34, ScreenshotHeight - 34);
        draw(canvas, content);

        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 95);
        using var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        data.SaveTo(stream);
    }

    private sealed class GuidePdfWriter : IDisposable
    {
        private readonly SKDocument _document;
        private readonly SKPaint _titlePaint;
        private readonly SKPaint _subtitlePaint;
        private readonly SKPaint _sectionPaint;
        private readonly SKPaint _bodyPaint;
        private SKCanvas? _canvas;

        public GuidePdfWriter(SKDocument document)
        {
            _document = document;
            _titlePaint = Paint(21, true, Ink);
            _subtitlePaint = Paint(10.5f, false, Muted);
            _sectionPaint = Paint(12.5f, true, Ink);
            _bodyPaint = Paint(9.3f, false, Ink);
        }

        public void Write()
        {
            WriteStartPage();
            WriteJobPdfPage();
            WritePagesLayersPage();
            WriteTakeoffsPage();
            WriteViewportPage();
            WriteManagersPage();
            WritePlanPage();
        }

        public void Dispose()
        {
            _titlePaint.Dispose();
            _subtitlePaint.Dispose();
            _sectionPaint.Dispose();
            _bodyPaint.Dispose();
        }

        private void WriteStartPage()
        {
            BeginPage(PageNames[0], "Use this sample project as a guided tour before working on a real bid.");
            DrawWorkspaceMap(_canvas!, new SKRect(Margin, 104, 372, 532));
            SectionList(402, 112, 326, "Start here", [
                "Pages > 00. Guide contains these guide cards.",
                "A101 Sample Plan contains preloaded measurements and prices.",
                "The sample takeoffs cover line, area, count, joist area, eave, and rake workflows.",
                "Use Ctrl+Shift+P to search every major command by name."
            ]);
            SectionList(402, 300, 326, "What to inspect", [
                "Left panel: Pages tree, PDF Layers, page setup, sheet sorting.",
                "Center: PDF viewport, drawing tools, snap, ortho, selection editing.",
                "Right panel: Takeoffs tree, active target, exports, totals, managers.",
                "Bottom: AI Inbox status and observations."
            ]);
            EndPage();
        }

        private void WriteJobPdfPage()
        {
            BeginPage(PageNames[1], "Top tabs own job setup, PDF metadata, page image tools, and display controls.");
            DrawJobPdfMap(_canvas!, new SKRect(Margin, 104, 744, 268));
            SectionList(Margin, 302, 320, "Main tab", [
                "Open / Import: open jobs, recent jobs, create jobs, sample job, import PDFs.",
                "PlanSwift: convert a PlanSwift job into an OurPlaneCore job.",
                "Export: write selected/all sheets to PDF.",
                "Name, Scale, Name+Scale, AI Fill, Crop Hints: sheet metadata workflow."
            ]);
            SectionList(406, 302, 330, "Page and Viewport tabs", [
                "Page: add pages, batch rename, rotate, flip, invert, crop, copy page PNG.",
                "Viewport: line/point/area thickness, labels, legend, page header, units, dark theme.",
                "Status bar should confirm the current job, page, and last operation."
            ]);
            EndPage();
        }

        private void WritePagesLayersPage()
        {
            BeginPage(PageNames[2], "Pages tree controls the sheet list; PDF Layers controls layer visibility and tracing.");
            DrawPagesLayersMap(_canvas!, new SKRect(Margin, 98, 365, 532));
            SectionList(402, 112, 326, "Pages panel buttons", [
                "R reloads Pages from disk, preserving the selected sheet where possible.",
                "- and + collapse or expand the full Pages tree.",
                "Tabs, Detach, Tile M2 open selected sheets in tabs or separate windows.",
                "Sort A/S, D/Sec/WT, Repair, Name / Scale, New Folder, Auto Folders live below the tree."
            ]);
            SectionList(402, 322, 326, "PDF Layers tab", [
                "Load scans the active page for PDF layers.",
                "All On / All Off toggles layer visibility.",
                "Layer Trace lets PDF geometry feed manual tracing.",
                "Clear Hi removes layer highlight overlays."
            ]);
            EndPage();
        }

        private void WriteTakeoffsPage()
        {
            BeginPage(PageNames[3], "Takeoffs tree owns takeoff folders, items, sections, active target, totals, and exports.");
            DrawTakeoffsMap(_canvas!, new SKRect(Margin, 98, 365, 532));
            SectionList(402, 112, 326, "Takeoffs panel buttons", [
                "R reloads Takeoffs from disk using the same safe tree rebuild path as job open.",
                "- and + collapse or expand the Takeoffs tree.",
                "Record toggles active takeoff recording; More opens active target actions.",
                "Props, Find, Sheet, <, > are active-target navigation and edit controls."
            ]);
            SectionList(402, 328, 326, "Create and export", [
                "New Folder and New Item create the selected/root takeoff structure.",
                "Roof Base starts the roof footprint/3D handoff path.",
                "Export and Current Excel write quantities out of the right panel.",
                "Auto Tree and From Pages build PlanSwift-style takeoff folder structures."
            ]);
            EndPage();
        }

        private void WriteViewportPage()
        {
            BeginPage(PageNames[4], "The center viewport is where measurements, annotations, snaps, and edits happen.");
            DrawViewportToolsMap(_canvas!, new SKRect(Margin, 96, 744, 290));
            SectionList(Margin, 326, 320, "Drawing tools", [
                "Pan, Select, Scale, Ruler, Annotation, Count, Line, Area, J Area, Beam, Openings, Cut.",
                "Snap uses existing takeoff points; PDF Snap uses PDF vector points.",
                "Ortho locks 90/45 degree drawing; Box draws by opposite corners.",
                "Fit, +, - control page view."
            ]);
            SectionList(406, 326, 330, "Editing selected geometry", [
                "Select supports body move, vertex edit, box select, copy, paste, delete, undo.",
                "Flip H, Flip V, rotate slider, and scale slider transform selected geometry.",
                "Scale presets and Set define sheet scale for real quantities.",
                "AI Settings shows key/model status without exposing secrets."
            ]);
            EndPage();
        }

        private void WriteManagersPage()
        {
            BeginPage(PageNames[5], "Manager tabs expose dense review tables and editable production settings.");
            DrawManagersMap(_canvas!, new SKRect(Margin, 100, 744, 286));
            SectionList(Margin, 322, 324, "Manager tabs", [
                "Sheet Manager: rename, scale, suffix/title/source/confidence review.",
                "Takeoff Manager: type, sections, total, unit, price, cost, notes, folder.",
                "Report Builder and Materials: Excel/report/material extraction outputs.",
                "AI Manager and 3D: observations, markers, model review, roof/3D workflows."
            ]);
            SectionList(406, 322, 330, "Settings tab", [
                "Editable rules belong in Settings instead of hidden code constants.",
                "Categories include Page Folders, Auto Tree, From Pages, Sort A/S, Sort D/Sec/WT, Auto Rename / Scale, Defaults.",
                "Use Reset, Save global default, Save as this job, and Apply where available."
            ]);
            EndPage();
        }

        private void WritePlanPage()
        {
            _canvas = _document.BeginPage(PageWidth, PageHeight);
            DrawSamplePlan(_canvas!, new SKRect(0, 0, PageWidth, PageHeight));
            _document.EndPage();
            _canvas = null;
        }

        private void BeginPage(string title, string subtitle)
        {
            _canvas = _document.BeginPage(PageWidth, PageHeight);
            _canvas.Clear(SKColors.White);
            _canvas.DrawText(title, Margin, 46, _titlePaint);
            _canvas.DrawText(subtitle, Margin, 68, _subtitlePaint);
            using var line = Stroke(Line, 1);
            _canvas.DrawLine(Margin, 82, PageWidth - Margin, 82, line);
        }

        private void EndPage()
        {
            if (_canvas == null)
                return;

            _document.EndPage();
            _canvas = null;
        }

        private void SectionList(float x, float y, float width, string title, IReadOnlyList<string> bullets)
        {
            _canvas!.DrawText(title, x, y, _sectionPaint);
            y += 19;
            foreach (string bullet in bullets)
                y = Bullet(x, y, width, bullet) + 7;
        }

        private float Bullet(float x, float y, float width, string text)
        {
            var lines = Wrap(text, _bodyPaint, width - 16);
            for (int i = 0; i < lines.Count; i++)
            {
                string prefix = i == 0 ? "- " : "  ";
                _canvas!.DrawText(prefix + lines[i], x, y, _bodyPaint);
                y += _bodyPaint.TextSize * 1.35f;
            }
            return y;
        }
    }

    private static void DrawWorkspaceMap(SKCanvas canvas, SKRect rect)
    {
        DrawShell(canvas, rect);
        float toolbar = rect.Top + rect.Height * 0.15f;
        float bodyTop = rect.Top + rect.Height * 0.23f;
        float statusTop = rect.Bottom - rect.Height * 0.08f;

        DrawLabeledRect(canvas, Rect(rect, 0.02f, 0.02f, 0.96f, 0.1f), Panel, Line, "Top tabs: Main / Page / Viewport", 9, true);
        DrawLabeledRect(canvas, Rect(rect, 0.02f, 0.13f, 0.96f, 0.08f), new SKColor(243, 246, 250), Line, "Toolbar: Pan Select Scale Ruler Count Line Area Snap Ortho Fit", 8.5f, false);
        DrawLabeledRect(canvas, new SKRect(rect.Left + 8, bodyTop, rect.Left + rect.Width * 0.26f, statusTop - 8), new SKColor(226, 232, 240), Line, "Pages\nPDF Layers", 10, true);
        DrawLabeledRect(canvas, new SKRect(rect.Left + rect.Width * 0.28f, bodyTop, rect.Right - rect.Width * 0.28f, statusTop - 8), SKColors.White, Line, "PDF viewport\nmeasurements\nannotations", 11, true);
        DrawLabeledRect(canvas, new SKRect(rect.Right - rect.Width * 0.26f, bodyTop, rect.Right - 8, statusTop - 8), new SKColor(226, 232, 240), Line, "Takeoffs\nActive target\nExport", 10, true);
        DrawLabeledRect(canvas, new SKRect(rect.Left + 8, statusTop, rect.Right - 8, rect.Bottom - 8), new SKColor(241, 245, 249), Line, "Status bar and AI Inbox", 8.5f, false);

        using var accent = Stroke(Accent, 3);
        canvas.DrawLine(rect.Right - rect.Width * 0.25f, toolbar, rect.Right - rect.Width * 0.08f, toolbar, accent);
    }

    private static void DrawJobPdfMap(SKCanvas canvas, SKRect rect)
    {
        DrawShell(canvas, rect);
        DrawLabeledRect(canvas, Rect(rect, 0.03f, 0.1f, 0.94f, 0.23f), Panel, Line, "Main tab", 10, true);
        DrawButtonRow(canvas, Rect(rect, 0.06f, 0.17f, 0.88f, 0.1f), ["Open / Import", "PlanSwift", "Export", "Name", "Scale", "Name+Scale", "AI Fill", "Crop Hints"]);
        DrawLabeledRect(canvas, Rect(rect, 0.03f, 0.42f, 0.94f, 0.23f), new SKColor(237, 242, 247), Line, "Page tab", 10, true);
        DrawButtonRow(canvas, Rect(rect, 0.06f, 0.49f, 0.88f, 0.1f), ["Add Pages", "Batch Rename", "Left", "Right", "180", "Invert", "Crop New", "Copy"]);
        DrawLabeledRect(canvas, Rect(rect, 0.03f, 0.74f, 0.94f, 0.16f), new SKColor(245, 247, 250), Line, "Viewport tab: display, labels, legend, units, dark theme", 9, false);
    }

    private static void DrawPagesLayersMap(SKCanvas canvas, SKRect rect)
    {
        DrawShell(canvas, rect);
        DrawLabeledRect(canvas, Rect(rect, 0.04f, 0.04f, 0.92f, 0.1f), Panel, Line, "Pages", 11, true);
        DrawButtonRow(canvas, Rect(rect, 0.63f, 0.06f, 0.28f, 0.06f), ["R", "-", "+"]);
        DrawButtonRow(canvas, Rect(rect, 0.07f, 0.18f, 0.86f, 0.08f), ["Tabs", "Detach", "Tile M2"]);
        DrawButtonRow(canvas, Rect(rect, 0.07f, 0.31f, 0.86f, 0.08f), ["Sort A/S", "D/Sec/WT", "Repair"]);
        DrawButtonRow(canvas, Rect(rect, 0.07f, 0.41f, 0.86f, 0.08f), ["Name / Scale", "New Folder", "Auto Folders"]);
        DrawLabeledRect(canvas, Rect(rect, 0.07f, 0.55f, 0.86f, 0.34f), SKColors.White, Line, "Pages tree\n00. Guide\nA101 Sample Plan\nPDF Layers tab\nLoad / All On / All Off / Layer Trace", 8.5f, false);
    }

    private static void DrawTakeoffsMap(SKCanvas canvas, SKRect rect)
    {
        DrawShell(canvas, rect);
        DrawLabeledRect(canvas, Rect(rect, 0.04f, 0.04f, 0.92f, 0.1f), Panel, Line, "Takeoffs", 11, true);
        DrawButtonRow(canvas, Rect(rect, 0.61f, 0.06f, 0.3f, 0.06f), ["R", "-", "+"]);
        DrawLabeledRect(canvas, Rect(rect, 0.06f, 0.17f, 0.88f, 0.14f), new SKColor(219, 234, 254), Accent, "Active target: Record / More / Props / Find / Sheet / < / >", 8.5f, false);
        DrawButtonRow(canvas, Rect(rect, 0.07f, 0.38f, 0.86f, 0.08f), ["New Folder", "New Item", "Roof Base", "Export"]);
        DrawButtonRow(canvas, Rect(rect, 0.07f, 0.48f, 0.86f, 0.08f), ["Current Excel", "Auto Tree", "From Pages"]);
        DrawLabeledRect(canvas, Rect(rect, 0.07f, 0.64f, 0.86f, 0.27f), SKColors.White, Line, "Takeoffs tree\nsqfts / walls / openings / rf / joists\nsections below measured items\nsearch box above tree", 8.5f, false);
    }

    private static void DrawViewportToolsMap(SKCanvas canvas, SKRect rect)
    {
        DrawShell(canvas, rect);
        DrawLabeledRect(canvas, Rect(rect, 0.03f, 0.08f, 0.94f, 0.16f), new SKColor(241, 245, 249), Line, "Main toolbar", 10, true);
        DrawButtonRow(canvas, Rect(rect, 0.05f, 0.13f, 0.9f, 0.06f), ["Pan", "Select", "Scale", "Ruler", "Annotation", "Count", "Line", "Area", "J Area", "Beam", "Openings", "Cut"]);
        DrawButtonRow(canvas, Rect(rect, 0.08f, 0.29f, 0.84f, 0.06f), ["Snap", "PDF Snap", "Ortho", "Box", "Fit", "+", "-", "Flip H", "Flip V", "Rotate", "Scale"]);
        DrawLabeledRect(canvas, Rect(rect, 0.05f, 0.43f, 0.9f, 0.43f), SKColors.White, Line, "Viewport canvas\nselect, edit vertices, copy/paste, delete, undo\nscale presets and AI Settings live on the toolbar", 12, true);
    }

    private static void DrawManagersMap(SKCanvas canvas, SKRect rect)
    {
        DrawShell(canvas, rect);
        DrawButtonRow(canvas, Rect(rect, 0.04f, 0.07f, 0.92f, 0.08f), ["Main View", "Sheet Manager", "Takeoff Manager", "Report Builder", "Materials", "AI Manager", "3D", "Settings"]);
        DrawLabeledRect(canvas, Rect(rect, 0.05f, 0.22f, 0.42f, 0.58f), SKColors.White, Line, "Manager grids\nRename / Scale\nTotals / Price / Cost\nMaterials / AI rows", 10, false);
        DrawLabeledRect(canvas, Rect(rect, 0.53f, 0.22f, 0.42f, 0.58f), new SKColor(245, 243, 255), Purple, "Settings\nPage Folders\nAuto Tree\nFrom Pages\nSort rules\nDefaults", 10, false);
    }

    private static void DrawSamplePlan(SKCanvas canvas, SKRect rect)
    {
        canvas.Clear(SKColors.White);
        using var title = Paint(22, true, Ink);
        using var subtitle = Paint(11, false, Muted);
        using var wall = Stroke(new SKColor(32, 37, 46), 2.2f);
        using var thin = Stroke(new SKColor(135, 145, 160), 1.2f);
        using var door = Stroke(Accent, 4);
        using var text = Paint(11, false, Ink);
        using var small = Paint(9.5f, false, Muted);

        canvas.DrawText("A101 Sample Plan", 72, 54, title);
        canvas.DrawText("Preloaded takeoffs: walls, sqft, doors, windows, joists, eave and rake guides.", 72, 78, subtitle);

        canvas.DrawRect(new SKRect(120, 140, 672, 470), wall);
        canvas.DrawLine(120, 305, 672, 305, thin);
        canvas.DrawLine(396, 140, 396, 470, thin);
        canvas.DrawLine(258, 305, 258, 470, thin);
        canvas.DrawLine(534, 140, 534, 305, thin);
        canvas.DrawLine(392, 140, 430, 140, door);
        canvas.DrawLine(672, 305, 672, 345, door);
        canvas.DrawLine(396, 470, 438, 470, door);
        canvas.DrawText("Office", 138, 288, text);
        canvas.DrawText("Open Area", 414, 288, text);
        canvas.DrawText("Storage", 138, 486, text);
        canvas.DrawText("Shop", 414, 486, text);
        canvas.DrawText("Scale: 1/8 in = 1 ft. Use this sheet to test Count, Line, Area, J Area, Beam, Openings, Roof Base, and exports.", 72, 558, small);
    }

    private static void DrawShell(SKCanvas canvas, SKRect rect)
    {
        using var fill = Fill(Surface);
        using var stroke = Stroke(Line, 1.2f);
        canvas.DrawRoundRect(rect, 8, 8, fill);
        canvas.DrawRoundRect(rect, 8, 8, stroke);
    }

    private static void DrawButtonRow(SKCanvas canvas, SKRect rect, IReadOnlyList<string> labels)
    {
        if (labels.Count == 0)
            return;

        float gap = 5;
        float width = (rect.Width - gap * (labels.Count - 1)) / labels.Count;
        for (int i = 0; i < labels.Count; i++)
        {
            var button = new SKRect(rect.Left + i * (width + gap), rect.Top, rect.Left + i * (width + gap) + width, rect.Bottom);
            SKColor fill = labels[i] == "R" ? new SKColor(219, 234, 254) : SKColors.White;
            SKColor stroke = labels[i] == "R" ? Accent : Line;
            DrawLabeledRect(canvas, button, fill, stroke, labels[i], 8.5f, true);
        }
    }

    private static void DrawLabeledRect(
        SKCanvas canvas,
        SKRect rect,
        SKColor fillColor,
        SKColor strokeColor,
        string label,
        float size,
        bool bold)
    {
        using var fill = Fill(fillColor);
        using var stroke = Stroke(strokeColor, 1);
        canvas.DrawRoundRect(rect, 5, 5, fill);
        canvas.DrawRoundRect(rect, 5, 5, stroke);
        using var paint = Paint(size, bold, Ink);
        DrawWrappedText(canvas, label, rect.Left + 6, rect.Top + size + 6, rect.Width - 12, paint);
    }

    private static SKRect Rect(SKRect parent, float x, float y, float w, float h) =>
        new(
            parent.Left + parent.Width * x,
            parent.Top + parent.Height * y,
            parent.Left + parent.Width * (x + w),
            parent.Top + parent.Height * (y + h));

    private static void DrawWrappedText(SKCanvas canvas, string text, float x, float y, float width, SKPaint paint)
    {
        foreach (string sourceLine in (text ?? "").Split('\n'))
        {
            foreach (string line in Wrap(sourceLine, paint, width))
            {
                canvas.DrawText(line, x, y, paint);
                y += paint.TextSize * 1.28f;
            }
        }
    }

    private static IReadOnlyList<string> Wrap(string text, SKPaint paint, float maxWidth)
    {
        var lines = new List<string>();
        string current = "";
        foreach (string word in (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = current.Length == 0 ? word : $"{current} {word}";
            if (paint.MeasureText(candidate) <= maxWidth || current.Length == 0)
            {
                current = candidate;
                continue;
            }

            lines.Add(current);
            current = word;
        }

        if (current.Length > 0 || lines.Count == 0)
            lines.Add(current);
        return lines;
    }

    private static SKPaint Fill(SKColor color) => new()
    {
        Color = color,
        IsAntialias = true,
        Style = SKPaintStyle.Fill,
    };

    private static SKPaint Stroke(SKColor color, float width) => new()
    {
        Color = color,
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = width,
    };

    private static SKPaint Paint(float size, bool bold, SKColor color)
    {
        SKTypeface typeface = SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default;
        return new SKPaint
        {
            Color = color,
            IsAntialias = true,
            TextSize = size,
            Typeface = typeface,
            FakeBoldText = bold,
        };
    }
}
