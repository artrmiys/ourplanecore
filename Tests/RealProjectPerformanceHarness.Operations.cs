using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using OurPlanCore;
using Docnet.Core;
using Docnet.Core.Models;
using SkiaSharp;

internal static partial class RealProjectPerformanceHarness
{
    private static async Task ProbeTrees(MainWindow main)
    {
        // The rendering probe opens tabs directly. Establish the ordinary tree
        // selection first, so reload is measured with the same selected sheet.
        Call(main, "SelectPageTreeNodeSilently", Get<PageInfo>(main, "_currentPage").FolderPath);
        if (((TreeView)main.FindName("TakeoffsTree")).SelectedItem is TreeViewItem selectedTakeoff)
            Call(main, "ExpandTreeItemAndAncestorsWithoutTracking", selectedTakeoff);
        foreach (var (name, stateField, pathMethod) in new[] {
            ("PagesTree", "_expandedPageTreePaths", "GetPagesNodePath"),
            ("TakeoffsTree", "_expandedTakeoffTreePaths", "GetTakeoffNodePath") })
        {
            var tree = (TreeView)main.FindName(name);
            var branch = TreeNodes(tree.Items).FirstOrDefault(node => node.HasItems && !node.IsExpanded)
                ?? throw new InvalidOperationException("A real collapsed branch is required: " + name);
            branch.IsExpanded = true;
            await main.Dispatcher.InvokeAsync(() => tree.UpdateLayout(), DispatcherPriority.Render);
            string? path = (string?)typeof(MainWindow).GetMethod(pathMethod, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [branch]);
            Check(Get<TreeExpansionState>(main, stateField).Contains(path), "User expansion was not tracked: " + name);
        }
        string page = Get<PageInfo>(main, "_currentPage").FolderPath;
        var expandedBefore = Get<TreeExpansionState>(main, "_expandedPageTreePaths").Snapshot().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visualExpandedBefore = ExpandedNodes((TreeView)main.FindName("PagesTree"), "GetPagesNodePath");
        var takeoffExpandedBefore = ExpandedNodes((TreeView)main.FindName("TakeoffsTree"), "GetTakeoffNodePath");
        var takeoffTrackedBefore = Get<TreeExpansionState>(main, "_expandedTakeoffTreePaths").Snapshot().ToHashSet(StringComparer.OrdinalIgnoreCase);
        string? activeTakeoff = Get<TakeoffItem?>(main, "_activeItem")?.FolderPath;
        for (int repeat = 0; repeat < 3; repeat++)
        {
            var watch = Stopwatch.StartNew();
            Call(main, "ReloadPagesTree", page, true);
            await main.Dispatcher.InvokeAsync(() => main.UpdateLayout(), DispatcherPriority.Render);
            watch.Stop();
            Steps.Add(new { Operation = "PagesTreeRebuildAndLayout", Repeat = repeat, Ms = watch.Elapsed.TotalMilliseconds });
            Check(Get<PageInfo>(main, "_currentPage").FolderPath == page, "Tree rebuild switched the current sheet");
            Check(Get<TreeExpansionState>(main, "_expandedPageTreePaths").Snapshot().ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(expandedBefore), "Tree rebuild lost expanded state");
            var visualExpandedAfter = ExpandedNodes((TreeView)main.FindName("PagesTree"), "GetPagesNodePath");
            if (!visualExpandedAfter.SetEquals(visualExpandedBefore))
            {
                Steps.Add(new { Operation = "PagesExpansionDifference", Repeat = repeat,
                    Lost = visualExpandedBefore.Except(visualExpandedAfter).ToArray(),
                    Added = visualExpandedAfter.Except(visualExpandedBefore).ToArray() });
                // Navigation deliberately expands ancestors without remembering them.
                // Baseline reload restores user expansion plus the current selection.
                Check(!visualExpandedBefore.Except(visualExpandedAfter).Any(expandedBefore.Contains) &&
                    !visualExpandedAfter.Except(visualExpandedBefore).Any(), "Rebuilt Pages tree lost user-expanded nodes or expanded a different branch");
            }

            watch.Restart();
            Call(main, "LoadTakeoffsForJob");
            await main.Dispatcher.InvokeAsync(() => main.UpdateLayout(), DispatcherPriority.Render);
            watch.Stop();
            Steps.Add(new { Operation = "TakeoffsTreeReloadAndLayout", Repeat = repeat, Ms = watch.Elapsed.TotalMilliseconds });
            Check(Get<TakeoffItem?>(main, "_activeItem")?.FolderPath == activeTakeoff, "Takeoffs reload changed the active selection");
            var takeoffExpandedAfter = ExpandedNodes((TreeView)main.FindName("TakeoffsTree"), "GetTakeoffNodePath");
            if (!takeoffExpandedAfter.SetEquals(takeoffExpandedBefore))
            {
                Steps.Add(new { Operation = "TakeoffsExpansionDifference", Repeat = repeat,
                    Lost = takeoffExpandedBefore.Except(takeoffExpandedAfter).ToArray(),
                    Added = takeoffExpandedAfter.Except(takeoffExpandedBefore).ToArray() });
                Check(!takeoffExpandedBefore.Except(takeoffExpandedAfter).Any(takeoffTrackedBefore.Contains) &&
                    !takeoffExpandedAfter.Except(takeoffExpandedBefore).Any(), "Takeoffs reload lost user-expanded nodes or expanded a different branch");
            }
        }
        foreach (string name in new[] { "PagesTree", "TakeoffsTree" })
        {
            var tree = (TreeView)main.FindName(name);
            var nodes = TreeNodes(tree.Items).ToArray();
            var expansion = nodes.ToDictionary(node => node, node => node.IsExpanded);
            var expansionWatch = Stopwatch.StartNew();
            foreach (var node in nodes) node.IsExpanded = true;
            await main.Dispatcher.InvokeAsync(() => tree.UpdateLayout(), DispatcherPriority.Render);
            expansionWatch.Stop();
            Steps.Add(new { Operation = "TreeExpandAllAndLayout", Tree = name, Nodes = nodes.Length,
                Ms = expansionWatch.Elapsed.TotalMilliseconds });
            var scroll = FindScroll(tree) ?? throw new InvalidOperationException("Tree scroll surface missing: " + name);
            Check(scroll.ScrollableHeight > 0, "Tree must contain a real scrollable work scope: " + name);
            double original = scroll.VerticalOffset;
            var offsets = new List<double>();
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 20; i++)
            {
                scroll.ScrollToVerticalOffset(scroll.ScrollableHeight * (i % 2 == 0 ? 1 : 0));
                await main.Dispatcher.InvokeAsync(() => tree.UpdateLayout(), DispatcherPriority.Render);
                offsets.Add(scroll.VerticalOffset);
            }
            watch.Stop();
            Steps.Add(new { Operation = "TreeScroll20", Tree = name, Ms = watch.Elapsed.TotalMilliseconds,
                scroll.ExtentHeight, scroll.ViewportHeight, Offsets = offsets });
            Check(offsets.Distinct().Count() > 1, "Scroll test did not move the actual tree: " + name);
            foreach (var (node, expanded) in expansion) node.IsExpanded = expanded;
            scroll.ScrollToVerticalOffset(original);
        }
    }

    private static async Task ProbeSaveAndExport(MainWindow main, IReadOnlyList<PageInfo> pages)
    {
        await WaitStorage();
        Console.WriteLine("PERF manual Save including portable package");
        var session = Get<OurPlanPackageSession>(main, "_currentPackageSession");
        session.HasUnpackagedChanges = true;
        var watch = Stopwatch.StartNew();
        Call(main, "BtnSave_Click", main, new RoutedEventArgs());
        await WaitStorage();
        watch.Stop();
        Check(!session.HasUnpackagedChanges, "Save left unpackaged changes");
        Steps.Add(new { Operation = "ManualSaveIncludingPackage", Ms = watch.Elapsed.TotalMilliseconds,
            Bytes = new FileInfo(session.PackagePath).Length });
        Console.WriteLine($"PERF Save completed: {watch.ElapsedMilliseconds} ms; verifying package objects");
        await Task.Run(() => OurPlanPackageArchive.ReadManifest(session.PackagePath, verifyObjects: true));
        Steps.Add(new { Operation = "SavedPackageVerified", SHA256 = Hash(session.PackagePath) });

        var options = (PdfExportOptions)Call(main, "BuildPdfExportOptions", true, true, true, 1d)!;
        IReadOnlyList<PdfExportPageInput> inputs;
        using ((IDisposable)Call(main, "UsePageMeasurementLookup")!)
            inputs = (IReadOnlyList<PdfExportPageInput>)Call(main, "BuildPdfExportPages", pages)!;
        int measurements = inputs.Sum(p => p.Takeoffs.Sum(t => t.Measurements.Count));
        Check(measurements > 0, "PDF export must include existing real measurements");
        var method = typeof(MainWindow).GetMethod("DrawPdfExportSheetOverlay", Private)!;
        var overlays = method.CreateDelegate<PdfSheetOverlayExportRenderer>(main);
        string output = Path.Combine(_root, "measured-sheets.pdf");
        watch.Restart();
        Console.WriteLine("PERF PDF export with actual takeoffs");
        var exported = await Task.Run(() => PdfExporter.TryExport(inputs, output, options, overlays));
        watch.Stop();
        Check(exported.Ok && string.IsNullOrWhiteSpace(exported.Error), "PDF export failed or warned: " + exported.Error);
        Check(new FileInfo(output).Length > 10000, "Exported PDF is unexpectedly empty");
        Steps.Add(new { Operation = "PdfExportMeasuredSheets", Ms = watch.Elapsed.TotalMilliseconds,
            Pages = pages.Select(p => p.Name).ToArray(), Measurements = measurements,
            Bytes = new FileInfo(output).Length, SHA256 = Hash(output) });
        VerifyExport(output, pages.Count);
    }

    private static ScrollViewer? FindScroll(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer scroll) return scroll;
            if (FindScroll(child) is { } nested) return nested;
        }
        return null;
    }

    private static HashSet<string> ExpandedNodes(TreeView tree, string pathMethod)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var getPath = typeof(MainWindow).GetMethod(pathMethod, BindingFlags.Static | BindingFlags.NonPublic)!;
        void Walk(ItemCollection items)
        {
            foreach (TreeViewItem item in items.OfType<TreeViewItem>())
            {
                if (item.IsExpanded && getPath.Invoke(null, [item]) is string path) paths.Add(path);
                Walk(item.Items);
            }
        }
        Walk(tree.Items);
        return paths;
    }

    private static IEnumerable<TreeViewItem> TreeNodes(ItemCollection items)
    {
        foreach (TreeViewItem node in items.OfType<TreeViewItem>())
        {
            yield return node;
            foreach (TreeViewItem child in TreeNodes(node.Items)) yield return child;
        }
    }

    private static void VerifyExport(string path, int expectedPages)
    {
        using var document = DocLib.Instance.GetDocReader(path, new PageDimensions(900, 900));
        Check(document.GetPageCount() == expectedPages, "Exported PDF page count differs from selection");
        for (int index = 0; index < expectedPages; index++)
        {
            using var page = document.GetPageReader(index);
            byte[] pixels = page.GetImage();
            int inkPixels = 0;
            for (int p = 0; p < pixels.Length; p += 4)
                if (pixels[p + 3] > 0 && (pixels[p] < 242 || pixels[p + 1] < 242 || pixels[p + 2] < 242)) inkPixels++;
            Check(inkPixels > 1000, "Actual exported PDF page rendered empty: " + index);
            using var bitmap = new SKBitmap(page.GetPageWidth(), page.GetPageHeight(), SKColorType.Bgra8888, SKAlphaType.Premul);
            Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(Path.Combine(_root, $"export-{index + 1}.png"));
            data.SaveTo(file);
            Steps.Add(new { Operation = "ExportedPdfPageRendered", Page = index + 1, InkPixels = inkPixels,
                Width = bitmap.Width, Height = bitmap.Height });
        }
    }
}
