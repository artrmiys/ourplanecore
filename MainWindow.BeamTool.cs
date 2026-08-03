using System;
using System.Windows;
using System.Windows.Controls;
using OurPlanCore.Controls;
using SkiaSharp;

namespace OurPlanCore;

public partial class MainWindow
{
    private const float BeamOpeningSimilarPaddingMinPdf = 6f;
    private const float BeamOpeningSimilarPaddingMaxPdf = 24f;
    private const float BeamOpeningSimilarPaddingRatio = 0.08f;
    private const float OpeningSimilarPaddingMinPdf = 18f;
    private const float OpeningSimilarPaddingMaxPdf = 96f;
    private const float OpeningSimilarPaddingRatio = 0.35f;
    private const float BeamOpeningSimilarTextPaddingMinPdf = 24f;
    private const float BeamOpeningSimilarTextPaddingMaxPdf = 144f;
    private const float BeamOpeningSimilarTextPaddingRatio = 0.65f;

    private void OnBeamMeasurementCompleted(BeamMeasurementRequest request)
    {
        if (!EnsureCurrentJobWritable("create a Beam takeoff"))
            return;

        if (_currentJob == null || _currentPage == null)
        {
            TxtStatus.Text = "Beam ruler was placed, but no job/page is open for the Count item.";
            return;
        }

        if (!IsSamePageFolder(request.PageFolder, _currentPage.FolderPath))
        {
            TxtStatus.Text = "Beam ruler was placed, but the active sheet changed before the Count item was created.";
            return;
        }

        string parentFolder = NewTakeoffItemParentFolderForUserCreate();
        string defaultColor = RandomTakeoffColor(_activeItem?.Color ?? _viewport.ActiveColor);
        string defaultName = BeamTakeoffService.BuildDefaultCountName(
            ResolveTakeoffFolderDefaultNamePrefix(parentFolder),
            request.OrderLengthText,
            out int editablePrefixLength);

        var dialog = new NewItemDialog(
            "point",
            defaultName,
            lockType: true,
            defaultColor: defaultColor,
            defaultCountSymbol: _newCountSymbol,
            initialNameSelectionLength: editablePrefixLength,
            showSimilarReviewOption: true,
            similarReviewOptionText: "Review similar Beam marks on this sheet",
            showBeamAnnotationOption: true,
            defaultKeepBeamAnnotationLine: _beamAnnotationConfig.KeepLineAnnotation,
            defaultBeamAnnotationLineColor: _beamAnnotationConfig.LineColor)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
        {
            TxtStatus.Text = $"Beam ruler kept: {request.LengthFeet:0.##} ft. Count item cancelled.";
            return;
        }

        RememberNewCountSymbol(dialog.ItemCountSymbol);
        bool beamLineAdded = dialog.KeepBeamAnnotationLine &&
                             _viewport.AddBeamAnnotationLine(
                                 request.StartPdf,
                                 request.EndPdf,
                                 dialog.BeamAnnotationLineColor) != null;

        TakeoffItem item = CreateUniqueTakeoffItem(dialog.ItemName, dialog.ItemColor, "point", parentFolder);
        ApplyTakeoffFolderDefaultsToNewItem(item, parentFolder);
        ApplyNewCountSymbolToItemIfNeeded(item, "point");
        _takeoffItems.Add(item);

        var treeParent = FindTakeoffTreeItemByFolder(parentFolder) ?? (ItemsControl)TakeoffsTree;
        TreeViewItem tvi = AddTakeoffTreeItem(item, treeParent);
        if (treeParent is TreeViewItem parentTvi)
            parentTvi.IsExpanded = true;

        _activeItem = item;
        _activeTakeoffParentFolder = parentFolder;
        _viewport.ActiveColor = item.Color;
        _viewport.ActiveTakeoffFolder = item.FolderPath;
        _viewport.ActiveCountSymbol = item.CountSymbol;
        tvi.IsSelected = true;

        SetTool("point");
        _viewport.AddCountMeasurementAt(request.CountPointPdf);
        UpdateToolStatus();
        RefreshActiveTakeoffVisuals();
        UpdateTotalDisplay();
        string lineStatus = beamLineAdded
            ? $" Companion line {dialog.BeamAnnotationLineColor} added."
            : "";
        TxtStatus.Text = $"Beam Count created: {item.Name}. Ruler {request.LengthFeet:0.##} ft, order size {request.OrderLengthText}.{lineStatus} Use Similar to add reviewed matches to this Beam item.";
        if (dialog.MarkSimilarOnCurrentSheet)
            StartSimilarCountReview(BuildBeamSimilarCountRequest(request));
    }

    private void OnOpeningMeasurementCompleted(OpeningMeasurementRequest request)
    {
        if (!EnsureCurrentJobWritable("create an Openings takeoff"))
            return;

        if (_currentJob == null || _currentPage == null)
        {
            TxtStatus.Text = "Opening dimensions were placed, but no job/page is open for the Count item.";
            return;
        }

        if (!IsSamePageFolder(request.PageFolder, _currentPage.FolderPath))
        {
            TxtStatus.Text = "Opening dimensions were placed, but the active sheet changed before the Count item was created.";
            return;
        }

        string parentFolder = NewTakeoffItemParentFolderForUserCreate();
        string defaultColor = RandomTakeoffColor(_activeItem?.Color ?? _viewport.ActiveColor);
        string defaultName = OpeningTakeoffService.BuildDefaultCountName(request.SizeText);

        var dialog = new NewItemDialog(
            "point",
            defaultName,
            lockType: true,
            defaultColor: defaultColor,
            defaultCountSymbol: _newCountSymbol,
            initialNameCaretIndex: defaultName.Length,
            showSimilarReviewOption: true,
            similarReviewOptionText: "Review similar Opening marks on this sheet")
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
        {
            TxtStatus.Text = $"Opening dimensions kept: {request.SizeText}. Count item cancelled.";
            return;
        }

        RememberNewCountSymbol(dialog.ItemCountSymbol);

        TakeoffItem item = CreateUniqueTakeoffItem(dialog.ItemName, dialog.ItemColor, "point", parentFolder);
        ApplyTakeoffFolderDefaultsToNewItem(item, parentFolder);
        ApplyNewCountSymbolToItemIfNeeded(item, "point");
        _takeoffItems.Add(item);

        var treeParent = FindTakeoffTreeItemByFolder(parentFolder) ?? (ItemsControl)TakeoffsTree;
        TreeViewItem tvi = AddTakeoffTreeItem(item, treeParent);
        if (treeParent is TreeViewItem parentTvi)
            parentTvi.IsExpanded = true;

        _activeItem = item;
        _activeTakeoffParentFolder = parentFolder;
        _viewport.ActiveColor = item.Color;
        _viewport.ActiveTakeoffFolder = item.FolderPath;
        _viewport.ActiveCountSymbol = item.CountSymbol;
        tvi.IsSelected = true;

        SetTool("point");
        _viewport.AddCountMeasurementAt(request.CountPointPdf);
        UpdateToolStatus();
        RefreshActiveTakeoffVisuals();
        UpdateTotalDisplay();
        TxtStatus.Text = $"Opening Count created: {item.Name}. Dimensions {request.SizeText}. Use Similar to add reviewed matches to this Opening item.";
        if (dialog.MarkSimilarOnCurrentSheet)
            StartSimilarCountReview(BuildOpeningSimilarCountRequest(request));
    }

    private ViewportSimilarCountRequest BuildBeamSimilarCountRequest(BeamMeasurementRequest request)
    {
        PageInfo page = _currentPage ?? throw new InvalidOperationException("Current page is required for Beam Similar.");
        SKRect segmentRect = MeasurementGeometry.NormalizeRect(request.StartPdf, request.EndPdf);
        float segmentLength = MeasurementGeometry.Distance(request.StartPdf, request.EndPdf);
        float padding = SimilarBeamOpeningPadding(Math.Max(
            segmentLength,
            Math.Max(segmentRect.Width, segmentRect.Height)));
        SKRect templateRect = PadRect(segmentRect, padding);
        SKPoint templateCenter = Midpoint(request.StartPdf, request.EndPdf);
        float textPadding = SimilarBeamOpeningTextPadding(segmentLength);
        return new ViewportSimilarCountRequest(
            templateRect,
            request.PageFolder,
            _viewport.ScaleMetersPerPt,
            [request.CountPointPdf],
            PdfPath: page.PdfPath,
            PdfPageIndex: page.PdfPage,
            AllowExactTextMatches: false,
            UseTextCandidateRasterMatches: true,
            IncludeTextCandidatesByDefault: true, // trust the repeated beam mark: pre-include tag-found candidates for review
            TemplateAnchorPdf: templateCenter,
            MarkerCenterPdf: request.CountPointPdf,
            TextSearchRectPdf: PadRect(segmentRect, textPadding),
            DestinationTakeoffFolderPath: _activeItem?.FolderPath ?? "",
            DefaultDestinationName: _activeItem?.Name ?? "",
            PreferNearestRepeatedText: true,
            TextCandidateSearchRadiusPdf: textPadding,
            InitialIncludeMirrored: true);
    }

    private ViewportSimilarCountRequest BuildOpeningSimilarCountRequest(OpeningMeasurementRequest request)
    {
        PageInfo page = _currentPage ?? throw new InvalidOperationException("Current page is required for Opening Similar.");
        SKRect openingRect = MeasurementGeometry.NormalizeRect(request.FirstCornerPdf, request.OppositeCornerPdf);
        float openingSize = Math.Max(openingRect.Width, openingRect.Height);
        float padding = SimilarOpeningPadding(openingSize);
        float textPadding = SimilarBeamOpeningTextPadding(openingSize);
        return new ViewportSimilarCountRequest(
            PadRect(openingRect, padding),
            request.PageFolder,
            _viewport.ScaleMetersPerPt,
            [request.CountPointPdf],
            PdfPath: page.PdfPath,
            PdfPageIndex: page.PdfPage,
            AllowExactTextMatches: false,
            UseTextCandidateRasterMatches: true,
            IncludeTextCandidatesByDefault: true, // openings almost always carry a text mark (D1/W3…): make it the primary signal, pre-included for review
            TemplateAnchorPdf: request.CountPointPdf,
            MarkerCenterPdf: request.CountPointPdf,
            TextSearchRectPdf: PadRect(openingRect, textPadding),
            DestinationTakeoffFolderPath: _activeItem?.FolderPath ?? "",
            DefaultDestinationName: _activeItem?.Name ?? "",
            PreferNearestRepeatedText: true,
            TextCandidateSearchRadiusPdf: textPadding,
            InitialIncludeMirrored: true);
    }

    private static float SimilarBeamOpeningPadding(float sizePdf)
    {
        if (!float.IsFinite(sizePdf) || sizePdf <= 0f)
            return BeamOpeningSimilarPaddingMinPdf;

        return Math.Clamp(
            sizePdf * BeamOpeningSimilarPaddingRatio,
            BeamOpeningSimilarPaddingMinPdf,
            BeamOpeningSimilarPaddingMaxPdf);
    }

    private static float SimilarBeamOpeningTextPadding(float sizePdf)
    {
        if (!float.IsFinite(sizePdf) || sizePdf <= 0f)
            return BeamOpeningSimilarTextPaddingMinPdf;

        return Math.Clamp(
            sizePdf * BeamOpeningSimilarTextPaddingRatio,
            BeamOpeningSimilarTextPaddingMinPdf,
            BeamOpeningSimilarTextPaddingMaxPdf);
    }

    private static float SimilarOpeningPadding(float sizePdf)
    {
        if (!float.IsFinite(sizePdf) || sizePdf <= 0f)
            return OpeningSimilarPaddingMinPdf;

        return Math.Clamp(
            sizePdf * OpeningSimilarPaddingRatio,
            OpeningSimilarPaddingMinPdf,
            OpeningSimilarPaddingMaxPdf);
    }

    private static SKRect PadRect(SKRect rect, float padding) =>
        new(rect.Left - padding, rect.Top - padding, rect.Right + padding, rect.Bottom + padding);

    private static SKPoint Midpoint(SKPoint left, SKPoint right) =>
        new((left.X + right.X) / 2f, (left.Y + right.Y) / 2f);
}
