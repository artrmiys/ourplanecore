using System.Globalization;
using System.IO;
using System.Windows.Controls;
using SkiaSharp;

namespace OurPlaneCore;

public partial class MainWindow
{
    // Push the generated roof into the takeoff tree as reviewable items: a
    // line item per eave/ridge/hip/valley (real sheet geometry, lengths flow to
    // estimating) plus a plan-area item chained from the roof base edges. The
    // sloped/plan area summary is written into each item's Notes.
    private void CreateRoofTakeoffFromGenerated()
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "3D Roof Takeoff: open a job first.";
            return;
        }

        string groupId = ActiveThreeDRoofGroupId();
        ThreeDRoofQuantities q = ThreeDRoofQuantities.Compute(
            _threeDRoofPlanes.Where(plane => SameRoofGroup(plane.RoofGroupId, groupId)),
            _threeDRoofGuides.Where(guide => SameRoofGroup(guide.RoofGroupId, groupId)));
        if (!q.HasRoof || !_threeDRoofGuides.Any(guide => SameRoofGroup(guide.RoofGroupId, groupId)))
        {
            TxtStatus.Text = "3D Roof Takeoff: generate a roof first (Roof Base -> set eave pitch).";
            return;
        }

        string pageFolder = MostCommonRoofGuidePageFolder();
        if (string.IsNullOrWhiteSpace(pageFolder))
        {
            TxtStatus.Text = "3D Roof Takeoff: roof edges are not linked to a sheet, cannot create a takeoff.";
            return;
        }

        double scale = RoofTakeoffScaleForPage(pageFolder);
        string summary = ThreeDRoofQuantitiesText(groupId);
        string notes = RoofTakeoffNotes(q);
        string parent = NewTakeoffItemParentFolder();
        int created = 0;

        created += TryCreateRoofLineItem("Roof Eave", ThreeDRoofGuideKinds.Eave, pageFolder, scale, notes, parent,
            _threeDRoofGuides.Where(g => SameRoofGroup(g.RoofGroupId, groupId) && g.DefinesSlope && IsSamePageFolder(g.PageFolder, pageFolder)));
        created += TryCreateRoofLineItem("Roof Ridge", ThreeDRoofGuideKinds.Ridge, pageFolder, scale, notes, parent,
            GeneratedSeamGuides(ThreeDRoofGuideKinds.Ridge, pageFolder, groupId));
        created += TryCreateRoofLineItem("Roof Hip", ThreeDRoofGuideKinds.Hip, pageFolder, scale, notes, parent,
            GeneratedSeamGuides(ThreeDRoofGuideKinds.Hip, pageFolder, groupId));
        created += TryCreateRoofLineItem("Roof Valley", ThreeDRoofGuideKinds.Valley, pageFolder, scale, notes, parent,
            GeneratedSeamGuides(ThreeDRoofGuideKinds.Valley, pageFolder, groupId));
        created += TryCreateRoofAreaItem(pageFolder, scale, notes, parent);

        if (created == 0)
        {
            TxtStatus.Text = "3D Roof Takeoff: no usable roof edges found on the linked sheet.";
            return;
        }

        TxtStatus.Text = $"3D Roof Takeoff: created {created} reviewable takeoff item(s). {summary}";
        LogThreeD($"Roof takeoff created: {created} item(s). {summary}");
    }

    private IEnumerable<ThreeDRoofGuide> GeneratedSeamGuides(string kind, string pageFolder, string groupId) =>
        _threeDRoofGuides.Where(g =>
            SameRoofGroup(g.RoofGroupId, groupId) &&
            string.Equals(g.Status, ThreeDRoofPreviewBuilder.GeneratedSeamStatus, StringComparison.OrdinalIgnoreCase) &&
            ThreeDRoofGuideKinds.Normalize(g.Kind) == kind &&
            IsSamePageFolder(g.PageFolder, pageFolder));

    private int TryCreateRoofLineItem(
        string name,
        string kind,
        string pageFolder,
        double scale,
        string summary,
        string parent,
        IEnumerable<ThreeDRoofGuide> guides)
    {
        List<ThreeDRoofGuide> list = guides.Where(g => g.Points.Count >= 2).ToList();
        if (list.Count == 0)
            return 0;

        string color = ThreeDRoofGuideKinds.Color(kind);
        TakeoffItem item = CreateUniqueTakeoffItem(name, color, "line", parent);
        item.Notes = summary;
        foreach (ThreeDRoofGuide guide in list)
        {
            item.Measurements.Add(new Measurement
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = guide.Label,
                MType = "line",
                Color = color,
                PageFolder = pageFolder,
                TakeoffFolder = item.FolderPath,
                ScaleMetersPerPt = scale,
                Points = guide.Points.Select(p => new SKPoint((float)p.PdfX, (float)p.PdfY)).ToList(),
            });
        }

        OurPlaneCoreJobStore.SaveTakeoffItem(item);
        _takeoffItems.Add(item);
        AddCreatedRoofTakeoffToTree(item);
        return 1;
    }

    private int TryCreateRoofAreaItem(string pageFolder, double scale, string summary, string parent)
    {
        List<SKPoint> loop = ChainRoofBaseLoop(pageFolder);
        if (loop.Count < 3)
            return 0;

        const string color = "#0EA5E9";
        TakeoffItem item = CreateUniqueTakeoffItem("Roof Area (plan)", color, "area", parent);
        item.Notes = summary;
        item.Measurements.Add(new Measurement
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Roof footprint",
            MType = "area",
            Color = color,
            PageFolder = pageFolder,
            TakeoffFolder = item.FolderPath,
            ScaleMetersPerPt = scale,
            Points = loop,
        });
        OurPlaneCoreJobStore.SaveTakeoffItem(item);
        _takeoffItems.Add(item);
        AddCreatedRoofTakeoffToTree(item);
        return 1;
    }

    private void AddCreatedRoofTakeoffToTree(TakeoffItem item)
    {
        ItemsControl parentNode =
            FindTakeoffTreeItemByFolder(Path.GetDirectoryName(item.FolderPath) ?? "") ?? (ItemsControl)TakeoffsTree;
        TreeViewItem tvi = AddTakeoffTreeItem(item, parentNode);
        if (parentNode is TreeViewItem parentTvi)
            parentTvi.IsExpanded = true;
        tvi.IsSelected = true;
    }

    private string MostCommonRoofGuidePageFolder() =>
        _threeDRoofGuides
            .Where(g => SameRoofGroup(g.RoofGroupId, ActiveThreeDRoofGroupId()))
            .Where(g => !string.IsNullOrWhiteSpace(g.PageFolder) && g.Points.Count >= 2)
            .GroupBy(g => g.PageFolder, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault() ?? "";

    private double RoofTakeoffScaleForPage(string pageFolder)
    {
        double pageScale = OurPlaneCoreJobStore.TryReadPage(pageFolder)?.ScaleMetersPerPt ?? 0;
        if (pageScale > 0)
            return pageScale;
        if (_currentPage?.ScaleMetersPerPt > 0)
            return _currentPage.ScaleMetersPerPt;
        return _viewport.ScaleMetersPerPt > 0 ? _viewport.ScaleMetersPerPt : 0.3048;
    }

    // Chain the roof base edges (eave + rake, not generated seams) into the
    // footprint polygon by matching segment endpoints in PDF coordinates.
    private List<SKPoint> ChainRoofBaseLoop(string pageFolder)
    {
        var segments = _threeDRoofGuides
            .Where(g => SameRoofGroup(g.RoofGroupId, ActiveThreeDRoofGroupId()))
            .Where(IsSelectableThreeDRoofBaseGuide)
            .Where(g => g.Points.Count >= 2 && IsSamePageFolder(g.PageFolder, pageFolder))
            .Select(g => (
                A: new SKPoint((float)g.Points[0].PdfX, (float)g.Points[0].PdfY),
                B: new SKPoint((float)g.Points[^1].PdfX, (float)g.Points[^1].PdfY)))
            .Where(s => Distance(s.A.X, s.A.Y, s.B.X, s.B.Y) > 0.5)
            .ToList();
        if (segments.Count < 3)
            return [];

        const double tol = 2.5;
        var used = new bool[segments.Count];
        var loop = new List<SKPoint> { segments[0].A, segments[0].B };
        used[0] = true;
        SKPoint current = segments[0].B;

        for (int step = 0; step < segments.Count + 2; step++)
        {
            int next = -1;
            bool reversed = false;
            for (int i = 0; i < segments.Count; i++)
            {
                if (used[i])
                    continue;
                if (Distance(current.X, current.Y, segments[i].A.X, segments[i].A.Y) <= tol)
                {
                    next = i;
                    reversed = false;
                    break;
                }
                if (Distance(current.X, current.Y, segments[i].B.X, segments[i].B.Y) <= tol)
                {
                    next = i;
                    reversed = true;
                    break;
                }
            }

            if (next < 0)
                break;

            used[next] = true;
            current = reversed ? segments[next].A : segments[next].B;
            if (Distance(current.X, current.Y, loop[0].X, loop[0].Y) <= tol)
                break;
            loop.Add(current);
        }

        // Only trust a near-complete ring; a partial chain is not a footprint.
        return used.Count(u => u) >= segments.Count - 1 && loop.Count >= 3 ? loop : [];
    }

    private static string RoofTakeoffNotes(ThreeDRoofQuantities q) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "Generated from 3D roof. Sloped {0:F0} SF, plan {1:F0} SF, ridge {2:F1} ft, hip {3:F1} ft, valley {4:F1} ft, eave {5:F1} ft.",
            q.SlopedAreaSqFt, q.PlanAreaSqFt, q.RidgeLengthFeet, q.HipLengthFeet, q.ValleyLengthFeet, q.EaveLengthFeet);
}
