using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SkiaSharp;

namespace OurPlanCore;

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

    private sealed record GuideSection(
        string PageName,
        string Subtitle,
        string? ScreenshotKey,
        IReadOnlyList<GuideBlock> Blocks);

    private sealed record GuideBlock(string Heading, IReadOnlyList<string> Bullets);

    // Each section becomes one imported guide page (named by PageName) and one PDF page that
    // embeds the matching real screenshot from Assets/GuideScreenshots, with a full control map.
    private static readonly IReadOnlyList<GuideSection> Sections =
    [
        new("00 Start Here - Workspace Map",
            "OurPlanCore is a local-first construction takeoff app. This sample project is a guided tour of every surface.",
            "01-main-workspace",
            [
                new("Three-panel shell", [
                    "Left: Pages tree (imported PDF sheets) plus PDF Layers and Bookmarks tabs.",
                    "Center: the PDF viewport - the drawing canvas where measurements and annotations live.",
                    "Right: Takeoffs tree - measurement containers grouped in folders, with totals and exports.",
                    "Bottom: the main toolbar (drawing tools) and the status bar / AI Inbox.",
                ]),
                new("How this sample is built", [
                    "Pages > 00. Guide holds these guide sheets; A101 Sample Plan is a live drawing.",
                    "A101 ships with preloaded line, area, count, joist and roof-guide takeoffs you can select.",
                    "Top tabs (Main / Page / PDF Output / Viewport) are ribbons; the row below them switches workspaces.",
                    "Press Ctrl+Shift+P anywhere to search every command by name.",
                ]),
            ]),

        new("01 Main Ribbon - Job and PDF",
            "The Main ribbon owns opening jobs, importing drawings, and the per-sheet metadata workflow.",
            "10-ribbon-main",
            [
                new("Job group", [
                    "Open / Import: open a job, pick a recent job, create a new job, create this Sample Job, or import PDFs.",
                    "PlanSwift: convert an existing PlanSwift job folder into an OurPlanCore job.",
                ]),
                new("PDF group", [
                    "Export: write the selected or all sheets back out to PDF.",
                    "Name / Scale / Name+Scale: detect a sheet's title and drawing scale (one, the other, or both).",
                    "AI Fill: ask the model to fill missing sheet metadata from the drawing.",
                    "Crop Hints: mark title-block / scale regions to guide detection.",
                ]),
            ]),

        new("02 Page Ribbon - Sheet Image Tools",
            "The Page ribbon edits the raster page image: add, rename, rotate, flip, crop, and set the drawing origin.",
            "11-ribbon-page",
            [
                new("Add and rename", [
                    "Add Pages: import more PDF sheets into the current folder.",
                    "Batch Rename: rename many sheets at once with a pattern.",
                ]),
                new("Rotate and flip", [
                    "Left / Right / 180: rotate the page; Level straightens a skewed scan; Batch Rotate applies to many sheets.",
                    "Vertical / Horizontal: mirror the page image.",
                ]),
                new("Image tools and origin", [
                    "Invert: flip dark/light scans for readability. Crop New Page: crop a region into a brand-new sheet. Copy: copy the rendered page image.",
                    "Set Origin / Offset Origin: define the measurement origin. Close Page: close the active sheet tab.",
                ]),
            ]),

        new("03 PDF Output Ribbon - Export Look",
            "The PDF Output ribbon controls how exported PDFs look - line weights, labels, overlays, and what is included.",
            "12-ribbon-pdf-output",
            [
                new("Lines & Area", [
                    "Line / Point thickness and Edge / Fill opacity for measurements drawn into the exported PDF.",
                ]),
                new("Labels & Overlays", [
                    "All / Line / Area / Joist / Count choose which measurement labels print, with a Size control.",
                    "Legend and Header sliders size the on-sheet legend and page header in the export.",
                ]),
                new("Include", [
                    "Toggle whether Measurements, Markups, and the Legend are baked into the exported PDF.",
                ]),
            ]),

        new("04 Viewport Ribbon - On-screen Display",
            "The Viewport ribbon mirrors the export controls but for the live on-screen view, plus units and theme.",
            "13-ribbon-viewport",
            [
                new("Lines, labels, overlays", [
                    "Line / Point / Edge / Fill set on-screen measurement weight and opacity.",
                    "Labels (All / Line / Area / Joist / Count) and Size control which labels show; w/page scales them with the sheet.",
                    "Legend Show / Size / Pos and Header Size / w/page control on-sheet overlays; Fast pan/zoom trades quality for speed.",
                ]),
                new("Units & view", [
                    "ft / sf toggles Imperial vs metric unit display; Edge sets the unit edge rounding.",
                    "Dark / Light switches the app theme; Paper sets the page background color.",
                ]),
            ]),

        new("05 Pages Tree and PDF Layers",
            "The left panel manages sheets (Pages), vector layer visibility (PDF Layers), and saved views (Bookmarks).",
            "02-pages-and-layers",
            [
                new("Pages panel", [
                    "R reloads Pages from disk; - and + collapse / expand the whole tree.",
                    "Tabs / Detach / Tile M2 open selected sheets in tabs, a floating window, or a 2-up tile.",
                    "Sort A/S and D/Sec/WT reorder sheets; Repair fixes broken links; Name / Scale opens metadata.",
                    "New Folder / Auto Folders build the sheet folder structure.",
                ]),
                new("PDF Layers and Bookmarks", [
                    "Load scans the active page for PDF vector layers; All On / All Off toggle visibility; Clear Hi clears highlights.",
                    "Layer Trace feeds PDF vector geometry into manual tracing (T toggles, Tab cycles, Enter advances).",
                    "Bookmarks tab saves named page + zoom views; BK adds one, Enter opens, Delete removes.",
                ]),
            ]),

        new("06 Takeoffs Panel",
            "The right panel is the takeoff tree: folders, items, the active recording target, totals, and exports.",
            "03-takeoffs-panel",
            [
                new("Active target and tabs", [
                    "The colored header shows the active takeoff; Record (or Space) starts / stops recording into it.",
                    "More opens active-target actions; Takeoffs / Estimating / 3D tabs switch the panel view.",
                ]),
                new("Create, total, export", [
                    "New Folder / New Item build structure; Roof Base starts the roof footprint - 3D handoff.",
                    "Total shows the running quantity; Export and Current Excel write quantities out.",
                    "Auto Tree and From Pages build PlanSwift-style takeoff folders automatically.",
                ]),
            ]),

        new("07 Viewport Toolbar and Tools",
            "The bottom toolbar holds every drawing and editing tool; shortcuts are in parentheses.",
            "04-viewport-toolbar",
            [
                new("Navigate and measure", [
                    "Pan (V), Select (E), Scale (S), Ruler (R), Annotation (D draw line / N note).",
                    "Count (P), Line (L), Area (A), J Area / Joist (J), Beam (B), Openings (O), Cut (X) record takeoffs.",
                ]),
                new("Snap, constrain, view", [
                    "Snap (F3) snaps to existing takeoff points; PDF Snap (Ctrl+F3) snaps to PDF vector points.",
                    "Ortho (F8) locks 90/45; Box (F9) draws by opposite corners; Fit (F) and +/- control zoom.",
                    "Select supports vertex edit, box select, copy (Ctrl+C) / paste (Ctrl+V) / delete, and undo (Ctrl+Z).",
                ]),
            ]),

        new("08 Sheet Manager",
            "A dense review grid for every sheet: detect and apply names, scales, and title-block metadata in bulk.",
            "20-sheet-manager",
            [
                new("Actions", [
                    "Analyze / Auto Name / Auto Scale / Name+Scale detect metadata; AI Fill and Crop Hints assist detection.",
                    "Apply Checked writes the proposed Name / Scale for checked rows back to the sheets.",
                    "PDF / Import PDF(s) / Export PDF, Open / Open Sheet / Open Tabs / Detach / Tile M2 manage sheets.",
                    "Organize, Sort A/S, D/Sec/WT, Repair Links, Auto Folders restructure the sheet tree.",
                ]),
                new("Columns", [
                    "Rename / Scale check-boxes pick which rows Apply Checked touches.",
                    "Current Page, Proposed Name, Scale, Label, Suffix, Title, Source, Confidence, and Why explain each detection.",
                ]),
            ]),

        new("09 Takeoff Manager",
            "A spreadsheet view of every takeoff item: type, totals, units, prices, costs, notes, and folder.",
            "21-takeoff-manager",
            [
                new("Actions", [
                    "Save / Refresh / New Folder / New Item / Tree manage items; Set Active and Properties edit the selected one.",
                    "Open Estimating opens pricing; Auto Tree and From Pages build folder structures.",
                    "Export / Export CSV / TXT / Excel / Current Excel write the takeoff quantities out.",
                ]),
                new("Columns", [
                    "Item, Type, Sections, Total, Unit, Price, Cost, Notes, and Folder are editable inline.",
                    "Cost = Total x Price; edit Price to see live cost roll-ups (try it on the sample items).",
                ]),
            ]),

        new("10 Report Builder",
            "Drives an Excel template (TemplateCom.xlsm) and fills it from your takeoffs for estimate-ready reports.",
            "22-report-builder",
            [
                new("Controls", [
                    "Template picks the workbook; Reload re-reads it; the file name shows the active template.",
                    "Table / Refresh rebuild the preview grid; Walls and Apply Walls push wall takeoffs into the template.",
                ]),
                new("Grid", [
                    "The grid mirrors the spreadsheet cells (columns A, B, C ...) so you can verify the filled report.",
                    "The status bar shows the active template name and row count.",
                ]),
            ]),

        new("11 Materials",
            "Extracts a material list from takeoffs and schedules, with confidence and export options.",
            "23-materials",
            [
                new("Actions", [
                    "Extract runs material extraction; Report Sheet builds a summary sheet; Refresh / Open reload results.",
                    "JSON / Rows CSV / Summary CSV / Folder export the extracted materials.",
                ]),
                new("Columns", [
                    "Category, Family, Item, Size, Qty, Unit, Sheet, Page, Schedule, Conf (confidence), and Flags.",
                    "Empty until you run Extract - it is safe to run on this sample.",
                ]),
            ]),

        new("12 AI Manager",
            "Reviews AI observations and markers. AI is file-based and optional; nothing runs without your action.",
            "24-ai-manager",
            [
                new("Run and review", [
                    "Setup / AI Settings show key and model status (reads OPENAI_API_KEY; never exposes the secret).",
                    "+ Add, Run AI, Batch, Run New, Retry Failed queue requests; Review / Open Details / Go to Page inspect results.",
                ]),
                new("Markers", [
                    "Markers, Create Set, Marker Sets, Export Markers manage AI count-marker training sets.",
                    "Columns: Type, Page, Observation, Quality, Time. The AI Inbox at the bottom of Main View mirrors pending items.",
                ]),
            ]),

        new("13 3D - Roof and Walls",
            "Turns 2D takeoffs into 3D massing: extrude walls and generate a roof from footprint and guide lines.",
            "25-3d",
            [
                new("Build group", [
                    "Auto builds walls from all line takeoffs; Wall builds from selected lines; Roof Base uses selected areas.",
                    "Select Edge tags eave / rake / ridge edges; Generate Roof builds the 3D roof from the footprint and guides.",
                ]),
                new("Viewer group", [
                    "Fit / Iso / Top / Front / Reset frame the 3D camera.",
                    "On A101, select the Front Eave Guide and Left Rake Guide takeoffs, then try Roof Base + Generate Roof.",
                ]),
            ]),

        new("14 Settings",
            "The home for every editable rule and template - safe defaults, saved globally or per job.",
            "26-settings",
            [
                new("Categories", [
                    "Page Folders, Auto Tree, From Pages, Sort A/S, Sort D/Sec/WT, Auto Rename / Scale, and Defaults.",
                    "Each category lists its rules / templates; edit them here instead of hunting in code.",
                ]),
                new("Saving rules", [
                    "Apply creates the structure in the live job; Save global default and Save as this job persist edits.",
                    "Reset to default restores the shipped values. Edits apply everywhere the rule is used.",
                ]),
            ]),

        new("15 Shortcuts and Workflow",
            "A keyboard cheat-sheet and a suggested first-pass workflow for a real bid.",
            null,
            [
                new("Global and tools", [
                    "Ctrl+O Open Job, Ctrl+S Save, Ctrl+Shift+P Command Palette, Space record, T new takeoff.",
                    "V Pan, E Select, S Scale, R Ruler, D line, N note, P Count, L Line, A Area, J Joist, B Beam, O Openings, X Cut.",
                ]),
                new("Viewport", [
                    "F Fit, F3 snap, Ctrl+F3 PDF snap, F8 Ortho, F9 Box, C complete shape, Backspace undo point.",
                    "Ctrl+Z undo, Ctrl+A select all, Ctrl+C copy, Ctrl+V paste, Delete remove selection.",
                ]),
                new("Try this on the sample", [
                    "1. Open A101 Sample Plan and select each preloaded takeoff to see legend, totals, and selection.",
                    "2. Set a scale (Scale tool or presets), then watch Takeoff Manager costs update.",
                    "3. Run Sheet Manager > Analyze, then Materials > Extract, then 3D > Roof Base + Generate Roof.",
                    "4. Use the Takeoffs R button after editing files outside the app to reload safely.",
                ]),
            ]),
    ];

    public static IReadOnlyList<string> PageNames { get; } =
        Sections.Select(section => section.PageName).Append("A101 Sample Plan").ToList();

    public static void WriteGuidePdf(string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        using var stream = File.Open(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        using SKDocument document = SKDocument.CreatePdf(stream);
        using var writer = new GuidePdfWriter(document);
        writer.Write();
    }

    public static void WriteGuideFiles(OurPlanCoreJob job)
    {
        string guideRoot = GuideRoot(job);
        string screenshots = GuideScreenshotsFolder(job);
        Directory.CreateDirectory(screenshots);

        foreach (GuideSection section in Sections)
        {
            if (string.IsNullOrEmpty(section.ScreenshotKey))
                continue;

            string dest = Path.Combine(screenshots, section.ScreenshotKey + ".png");
            byte[]? bytes = ResolveScreenshot(section.ScreenshotKey);
            if (bytes != null)
                File.WriteAllBytes(dest, bytes);
            else
                WriteFallbackScreenshot(dest, section);
        }

        File.WriteAllText(Path.Combine(guideRoot, "README.md"), BuildGuideMarkdown(), Encoding.UTF8);
    }

    private static string GuideRoot(OurPlanCoreJob job) =>
        Path.Combine(job.AIContextRoot, "guide");

    private static string GuideScreenshotsFolder(OurPlanCoreJob job) =>
        Path.Combine(GuideRoot(job), "screenshots");

    private static byte[]? ResolveScreenshot(string key)
    {
        try
        {
            string path = BundledToolPathResolver.ResolveFile(
                Path.Combine("Assets", "GuideScreenshots", key + ".png"));
            return string.IsNullOrEmpty(path) || !File.Exists(path) ? null : File.ReadAllBytes(path);
        }
        catch
        {
            return null;
        }
    }

    private static string BuildGuideMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# OurPlanCore Guide Sample");
        sb.AppendLine();
        sb.AppendLine("Open Pages > 00. Guide for a screen-by-screen tour, then open A101 Sample Plan to see live takeoffs.");
        sb.AppendLine("The screenshots below are real captures of every workspace surface.");
        sb.AppendLine();
        sb.AppendLine("## Screens");
        sb.AppendLine();
        foreach (GuideSection section in Sections)
        {
            sb.AppendLine($"### {section.PageName}");
            sb.AppendLine();
            sb.AppendLine(section.Subtitle);
            sb.AppendLine();
            if (!string.IsNullOrEmpty(section.ScreenshotKey))
            {
                sb.AppendLine($"![{section.PageName}](screenshots/{section.ScreenshotKey}.png)");
                sb.AppendLine();
            }

            foreach (GuideBlock block in section.Blocks)
            {
                sb.AppendLine($"**{block.Heading}**");
                sb.AppendLine();
                foreach (string bullet in block.Bullets)
                    sb.AppendLine($"- {bullet}");
                sb.AppendLine();
            }
        }

        // Kept verbatim for the existing structure test.
        sb.AppendLine("## Button Map");
        sb.AppendLine();
        sb.AppendLine("- Takeoffs panel: R refresh, collapse, expand, active target Record/More/Props, New Folder, New Item, Roof Base, Export, Current Excel, Auto Tree, From Pages.");
        return sb.ToString();
    }

    private static void WriteFallbackScreenshot(string path, GuideSection section)
    {
        using var bitmap = new SKBitmap(ScreenshotWidth, ScreenshotHeight);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        using var titlePaint = Paint(30, true, Ink);
        using var smallPaint = Paint(16, false, Muted);
        canvas.DrawText(section.PageName, 40, 56, titlePaint);
        DrawWrappedText(canvas, section.Subtitle, 40, 92, ScreenshotWidth - 80, smallPaint);

        float y = 150;
        using var headingPaint = Paint(20, true, Accent);
        using var bodyPaint = Paint(15, false, Ink);
        foreach (GuideBlock block in section.Blocks)
        {
            canvas.DrawText(block.Heading, 40, y, headingPaint);
            y += 28;
            foreach (string bullet in block.Bullets)
            {
                foreach (string line in Wrap("- " + bullet, bodyPaint, ScreenshotWidth - 100))
                {
                    canvas.DrawText(line, 56, y, bodyPaint);
                    y += 22;
                }
            }
            y += 14;
        }

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
            _titlePaint = Paint(19, true, Ink);
            _subtitlePaint = Paint(9.8f, false, Muted);
            _sectionPaint = Paint(11.5f, true, Accent);
            _bodyPaint = Paint(8.6f, false, Ink);
        }

        public void Write()
        {
            foreach (GuideSection section in Sections)
                WriteSectionPage(section);

            WritePlanPage();
        }

        public void Dispose()
        {
            _titlePaint.Dispose();
            _subtitlePaint.Dispose();
            _sectionPaint.Dispose();
            _bodyPaint.Dispose();
        }

        private void WriteSectionPage(GuideSection section)
        {
            _canvas = _document.BeginPage(PageWidth, PageHeight);
            _canvas.Clear(SKColors.White);
            _canvas.DrawText(section.PageName, Margin, 44, _titlePaint);
            DrawWrappedText(_canvas, section.Subtitle, Margin, 64, PageWidth - Margin * 2, _subtitlePaint);

            using (var rule = Stroke(Line, 1))
                _canvas.DrawLine(Margin, 78, PageWidth - Margin, 78, rule);

            float contentTop = 88;
            byte[]? shot = string.IsNullOrEmpty(section.ScreenshotKey) ? null : ResolveScreenshot(section.ScreenshotKey);
            if (shot != null)
                contentTop = DrawScreenshot(shot, 88) + 12;

            DrawBlocks(section.Blocks, contentTop);
            _document.EndPage();
            _canvas = null;
        }

        private float DrawScreenshot(byte[] bytes, float top)
        {
            using SKImage? image = SKImage.FromEncodedData(bytes);
            if (image == null)
                return top;

            float maxWidth = PageWidth - Margin * 2;
            float maxHeight = 300f;
            float scale = Math.Min(maxWidth / image.Width, maxHeight / image.Height);
            float width = image.Width * scale;
            float height = image.Height * scale;
            float left = (PageWidth - width) / 2f;
            var dest = new SKRect(left, top, left + width, top + height);

            using var border = Stroke(Line, 1);
            using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
            _canvas!.DrawImage(image, dest, paint);
            _canvas.DrawRect(dest, border);
            return top + height;
        }

        private void DrawBlocks(IReadOnlyList<GuideBlock> blocks, float top)
        {
            // Two columns so dense control maps fit under wide screenshots.
            float columnGap = 24;
            float columnWidth = (PageWidth - Margin * 2 - columnGap) / 2f;
            float leftX = Margin;
            float rightX = Margin + columnWidth + columnGap;

            int half = (int)Math.Ceiling(blocks.Count / 2.0);
            float yLeft = top;
            float yRight = top;
            for (int i = 0; i < blocks.Count; i++)
            {
                bool leftColumn = i < half;
                float x = leftColumn ? leftX : rightX;
                float y = leftColumn ? yLeft : yRight;
                y = DrawBlock(blocks[i], x, y, columnWidth);
                if (leftColumn)
                    yLeft = y + 10;
                else
                    yRight = y + 10;
            }
        }

        private float DrawBlock(GuideBlock block, float x, float y, float width)
        {
            _canvas!.DrawText(block.Heading, x, y, _sectionPaint);
            y += 16;
            foreach (string bullet in block.Bullets)
                y = Bullet(x, y, width, bullet) + 4;
            return y;
        }

        private float Bullet(float x, float y, float width, string text)
        {
            var lines = Wrap(text, _bodyPaint, width - 14);
            for (int i = 0; i < lines.Count; i++)
            {
                string prefix = i == 0 ? "- " : "  ";
                _canvas!.DrawText(prefix + lines[i], x, y, _bodyPaint);
                y += _bodyPaint.TextSize * 1.32f;
            }
            return y;
        }

        private void WritePlanPage()
        {
            _canvas = _document.BeginPage(PageWidth, PageHeight);
            DrawSamplePlan(_canvas!, new SKRect(0, 0, PageWidth, PageHeight));
            _document.EndPage();
            _canvas = null;
        }
    }

    private static void DrawSamplePlan(SKCanvas canvas, SKRect rect)
    {
        canvas.Clear(SKColors.White);
        var g = SamplePlanGeometry.Instance;

        using var title = Paint(20, true, Ink);
        using var subtitle = Paint(10.5f, false, Muted);
        using var wallFill = Fill(new SKColor(45, 52, 64));
        using var clear = Fill(SKColors.White);
        using var openingStroke = Stroke(new SKColor(70, 80, 96), 1.1f);
        using var swingStroke = Stroke(new SKColor(120, 132, 150), 1.0f);
        using var roomPaint = Paint(11, true, new SKColor(60, 70, 86));
        using var roomTag = Paint(8.5f, false, Muted);
        using var dimStroke = Stroke(new SKColor(150, 160, 176), 0.9f);
        using var dimText = Paint(8.5f, false, Muted);

        canvas.DrawText("A101  -  FLOOR PLAN", g.OuterL, g.OuterT - 56, title);
        canvas.DrawText("Single-story residence. Preloaded takeoffs: floor area, walls, doors, windows, joists, roof + eave/rake guides.",
            g.OuterL, g.OuterT - 38, subtitle);

        // 1) Solid wall fills (exterior ring + interior partitions).
        FillRect(canvas, g.OuterL, g.OuterT, g.OuterR, g.OuterT + g.Wall, wallFill);     // top
        FillRect(canvas, g.OuterL, g.OuterB - g.Wall, g.OuterR, g.OuterB, wallFill);     // bottom
        FillRect(canvas, g.OuterL, g.OuterT, g.OuterL + g.Wall, g.OuterB, wallFill);     // left
        FillRect(canvas, g.OuterR - g.Wall, g.OuterT, g.OuterR, g.OuterB, wallFill);     // right
        FillRect(canvas, g.InnerL, g.MidY - g.Part / 2, g.InnerR, g.MidY + g.Part / 2, wallFill);                 // mid partition
        FillRect(canvas, g.TopVx1 - g.Part / 2, g.InnerT, g.TopVx1 + g.Part / 2, g.MidY, wallFill);               // bedroom|bath
        FillRect(canvas, g.TopVx2 - g.Part / 2, g.InnerT, g.TopVx2 + g.Part / 2, g.MidY, wallFill);               // bath|kitchen
        FillRect(canvas, g.BotVx - g.Part / 2, g.MidY, g.BotVx + g.Part / 2, g.InnerB, wallFill);                 // living|dining

        // 2) Punch openings (clear the wall) then draw the door/window symbol.
        foreach (SamplePlanGeometry.Opening o in g.Openings)
        {
            ClearOpening(canvas, o, clear);
            if (o.IsDoor)
                DrawDoor(canvas, o, swingStroke, openingStroke);
            else
                DrawWindow(canvas, o, openingStroke);
        }

        // 3) Room labels.
        foreach (SamplePlanGeometry.Room room in g.Rooms)
        {
            float w = roomPaint.MeasureText(room.Name);
            canvas.DrawText(room.Name, room.Cx - w / 2, room.Cy, roomPaint);
            float tagW = roomTag.MeasureText(room.Tag);
            canvas.DrawText(room.Tag, room.Cx - tagW / 2, room.Cy + 14, roomTag);
        }

        // 4) Overall dimension strings (top width, right height).
        DrawDimH(canvas, g.OuterL, g.OuterR, g.OuterT - 16, "47'-2\"", dimStroke, dimText);
        DrawDimV(canvas, g.OuterT, g.OuterB, g.OuterR + 18, "33'-2\"", dimStroke, dimText);

        // 5) North arrow + scale note.
        DrawNorthArrow(canvas, g.OuterR + 40, g.OuterT + 6);
        using var small = Paint(9.5f, false, Muted);
        canvas.DrawText("SCALE: 1/8\" = 1'-0\"     Try Count, Line, Area, J Area, Beam, Openings, Roof Base, and exports on this sheet.",
            g.OuterL, g.OuterB + 34, small);
    }

    private static void FillRect(SKCanvas canvas, float l, float t, float r, float b, SKPaint paint) =>
        canvas.DrawRect(new SKRect(l, t, r, b), paint);

    private static void ClearOpening(SKCanvas canvas, SamplePlanGeometry.Opening o, SKPaint clear)
    {
        float half = o.Width / 2f;
        SKRect band = o.Horizontal
            ? new SKRect(o.Center.X - half, o.WallNear, o.Center.X + half, o.WallFar)
            : new SKRect(o.WallNear, o.Center.Y - half, o.WallFar, o.Center.Y + half);
        canvas.DrawRect(band, clear);
    }

    private static void DrawWindow(SKCanvas canvas, SamplePlanGeometry.Opening o, SKPaint stroke)
    {
        float half = o.Width / 2f;
        if (o.Horizontal)
        {
            float mid = (o.WallNear + o.WallFar) / 2f;
            canvas.DrawLine(o.Center.X - half, o.WallNear, o.Center.X + half, o.WallNear, stroke);
            canvas.DrawLine(o.Center.X - half, o.WallFar, o.Center.X + half, o.WallFar, stroke);
            canvas.DrawLine(o.Center.X - half, mid, o.Center.X + half, mid, stroke);
            canvas.DrawLine(o.Center.X - half, o.WallNear, o.Center.X - half, o.WallFar, stroke);
            canvas.DrawLine(o.Center.X + half, o.WallNear, o.Center.X + half, o.WallFar, stroke);
        }
        else
        {
            float mid = (o.WallNear + o.WallFar) / 2f;
            canvas.DrawLine(o.WallNear, o.Center.Y - half, o.WallNear, o.Center.Y + half, stroke);
            canvas.DrawLine(o.WallFar, o.Center.Y - half, o.WallFar, o.Center.Y + half, stroke);
            canvas.DrawLine(mid, o.Center.Y - half, mid, o.Center.Y + half, stroke);
            canvas.DrawLine(o.WallNear, o.Center.Y - half, o.WallFar, o.Center.Y - half, stroke);
            canvas.DrawLine(o.WallNear, o.Center.Y + half, o.WallFar, o.Center.Y + half, stroke);
        }
    }

    private static void DrawDoor(SKCanvas canvas, SamplePlanGeometry.Opening o, SKPaint swing, SKPaint jamb)
    {
        float w = o.Width;
        float half = w / 2f;
        float wallMid = (o.WallNear + o.WallFar) / 2f;
        if (o.Horizontal)
        {
            float hingeX = o.Center.X - half;
            float leafY = wallMid + o.Swing * w;
            canvas.DrawLine(hingeX, wallMid, hingeX, leafY, swing);
            var oval = new SKRect(hingeX - w, wallMid - w, hingeX + w, wallMid + w);
            canvas.DrawArc(oval, 0, 90 * o.Swing, false, swing);
            canvas.DrawLine(o.Center.X - half, o.WallNear, o.Center.X - half, o.WallFar, jamb);
            canvas.DrawLine(o.Center.X + half, o.WallNear, o.Center.X + half, o.WallFar, jamb);
        }
        else
        {
            float hingeY = o.Center.Y - half;
            float leafX = wallMid + o.Swing * w;
            canvas.DrawLine(wallMid, hingeY, leafX, hingeY, swing);
            var oval = new SKRect(wallMid - w, hingeY - w, wallMid + w, hingeY + w);
            canvas.DrawArc(oval, 90, -90 * o.Swing, false, swing);
            canvas.DrawLine(o.WallNear, o.Center.Y - half, o.WallFar, o.Center.Y - half, jamb);
            canvas.DrawLine(o.WallNear, o.Center.Y + half, o.WallFar, o.Center.Y + half, jamb);
        }
    }

    private static void DrawDimH(SKCanvas canvas, float x1, float x2, float y, string label, SKPaint line, SKPaint text)
    {
        canvas.DrawLine(x1, y, x2, y, line);
        canvas.DrawLine(x1, y - 4, x1, y + 4, line);
        canvas.DrawLine(x2, y - 4, x2, y + 4, line);
        float w = text.MeasureText(label);
        using var bg = Fill(SKColors.White);
        canvas.DrawRect(new SKRect((x1 + x2) / 2 - w / 2 - 3, y - 7, (x1 + x2) / 2 + w / 2 + 3, y + 5), bg);
        canvas.DrawText(label, (x1 + x2) / 2 - w / 2, y + 3, text);
    }

    private static void DrawDimV(SKCanvas canvas, float y1, float y2, float x, string label, SKPaint line, SKPaint text)
    {
        canvas.DrawLine(x, y1, x, y2, line);
        canvas.DrawLine(x - 4, y1, x + 4, y1, line);
        canvas.DrawLine(x - 4, y2, x + 4, y2, line);
        float w = text.MeasureText(label);
        canvas.Save();
        canvas.RotateDegrees(-90, x, (y1 + y2) / 2);
        using var bg = Fill(SKColors.White);
        canvas.DrawRect(new SKRect(x - w / 2 - 3, (y1 + y2) / 2 - 7, x + w / 2 + 3, (y1 + y2) / 2 + 5), bg);
        canvas.DrawText(label, x - w / 2, (y1 + y2) / 2 + 3, text);
        canvas.Restore();
    }

    private static void DrawNorthArrow(SKCanvas canvas, float cx, float top)
    {
        using var fill = Fill(Ink);
        using var stroke = Stroke(Ink, 1);
        using var label = Paint(8f, true, Ink);
        using var path = new SKPath();
        path.MoveTo(cx, top);
        path.LineTo(cx - 5, top + 16);
        path.LineTo(cx, top + 12);
        path.LineTo(cx + 5, top + 16);
        path.Close();
        canvas.DrawPath(path, fill);
        float w = label.MeasureText("N");
        canvas.DrawText("N", cx - w / 2, top + 30, label);
    }

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
