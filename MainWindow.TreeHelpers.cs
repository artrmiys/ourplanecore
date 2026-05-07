using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OurPlaneCore.Controls;

namespace OurPlaneCore;

public partial class MainWindow
{
    // ── Tree helpers ──────────────────────────────────────────────────────────

    private void RefreshTreeItem(TakeoffItem item)
    {
        if (FindTakeoffTreeItem(item) is { } tvi)
        {
            SetTreeItemHeader(tvi, item);
            RefreshTakeoffSectionNodes(tvi, item);
        }
    }

    private void RefreshActiveTakeoffVisuals()
    {
        foreach (TreeViewItem tvi in EnumerateTakeoffTreeItems(TakeoffsTree))
        {
            if (tvi.Tag is TakeoffItem item)
                SetTreeItemHeader(tvi, item);
        }

        UpdateActiveTakeoffTargetBar();
        ApplyTakeoffPageHighlights();
    }

    private void RefreshAllTotals()
    {
        RefreshTotalsRecursive(TakeoffsTree);
        RefreshPagesTakeoffIndicators();
        ApplyTakeoffPageHighlights();
        RefreshSheetLegend();
        UpdateTotalDisplay();
    }

    private void RefreshSheetLegend()
    {
        if (_currentPage == null || !_settings.ShowSheetLegend)
        {
            _viewport.SetSheetLegend([]);
            return;
        }

        var entries = VisibleOrderedTakeoffsForPage(_currentPage)
            .Select(item =>
            {
                var pageMeasurements = MeasurementsForTakeoffOnPage(item, _currentPage.FolderPath).ToList();
                return pageMeasurements.Count == 0
                    ? null
                    : new SheetLegendEntry(
                        item.Color,
                        item.Name,
                        SheetLegendQuantityText(item, pageMeasurements),
                        SheetLegendTypeTitle(item),
                        SheetLegendTypeSign(item),
                        []);
            })
            .Where(entry => entry != null)
            .Cast<SheetLegendEntry>()
            .ToList();

        _viewport.SetSheetLegend(entries);
    }

    private string SheetLegendQuantityText(TakeoffItem item, IReadOnlyList<Measurement> measurements)
    {
        string measurementType = OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
        double fallbackScale = _currentPage?.ScaleMetersPerPt > 0
            ? _currentPage.ScaleMetersPerPt
            : _viewport.ScaleMetersPerPt;

        if (measurementType == "point")
            return Units.FormatCount(measurements.Sum(measurement => measurement.Points.Count));

        bool hasScale = fallbackScale > 0 || measurements.Any(measurement => measurement.ScaleMetersPerPt > 0);
        if (item.IsJoistArea)
        {
            return hasScale
                ? Units.FormatArea(measurements.Sum(measurement => measurement.AreaValue(fallbackScale)), _viewport.UnitMode)
                : $"{measurements.Sum(measurement => measurement.Points.Count)} pts";
        }

        if (!hasScale)
        {
            if (measurementType == "line")
                return $"{measurements.Sum(measurement => Math.Max(0, measurement.Points.Count - 1))} seg";
            if (measurementType == "area")
                return $"{measurements.Sum(measurement => measurement.Points.Count)} pts";
        }

        double total = measurements.Sum(measurement => measurement.Value(fallbackScale));
        return measurementType switch
        {
            "line" => Units.FormatLength(total, _viewport.UnitMode),
            "area" => Units.FormatArea(total, _viewport.UnitMode),
            _ => Units.FormatCount(total),
        };
    }

    private void RefreshTotalsRecursive(ItemsControl parent)
    {
        foreach (TreeViewItem tvi in parent.Items.OfType<TreeViewItem>())
        {
            if (tvi.Tag is TakeoffItem item)
            {
                SetTreeItemHeader(tvi, item);
                RefreshTakeoffSectionNodes(tvi, item);
            }
            else
            {
                RefreshTotalsRecursive(tvi);
            }
        }
    }

    private void ApplyTakeoffPageHighlights()
    {
        Brush? brushOrNull(string key) => Application.Current.Resources[key] as Brush;
        Brush dropOk      = brushOrNull("RowDropOkBrush")      ?? new SolidColorBrush(Color.FromRgb(204, 245, 218));
        Brush dropBad     = brushOrNull("RowDropBadBrush")     ?? new SolidColorBrush(Color.FromRgb(255, 214, 214));
        Brush multiSel    = brushOrNull("RowMultiSelectBrush") ?? new SolidColorBrush(Color.FromRgb(205, 226, 255));
        Brush onPageBg    = brushOrNull("RowOnPageBrush")      ?? new SolidColorBrush(Color.FromRgb(214, 245, 222));
        Brush rowFg       = brushOrNull("RowFlagForegroundBrush") ?? Brushes.Black;
        Brush activeAccent = brushOrNull("RowActiveAccentBrush")  ?? new SolidColorBrush(Color.FromRgb(31, 82, 166));

        foreach (TreeViewItem item in EnumerateTakeoffTreeItems(TakeoffsTree))
        {
            item.ClearValue(Control.BorderBrushProperty);
            item.ClearValue(Control.BorderThicknessProperty);
            item.ClearValue(Control.FontWeightProperty);

            string? path = GetTakeoffNodePath(item);
            string? sectionKey = GetTakeoffSectionSelectionKey(item);
            bool sectionSelected = sectionKey != null && _takeoffSectionMultiSelection.Contains(sectionKey);
            bool takeoffSelected = path != null && _takeoffsMultiSelection.Contains(path);
            bool isActiveTakeoff = item.Tag is TakeoffItem activeTakeoff && IsActiveTakeoffItem(activeTakeoff);
            bool isMeasuredOnPage = item.Tag is TakeoffItem takeoff && IsTakeoffMeasuredOnCurrentPage(takeoff);
            if (ReferenceEquals(item, _takeoffSectionDropTarget))
            {
                item.Background = _takeoffSectionDropAllowed ? dropOk : dropBad;
                item.Foreground = rowFg;
                item.FontWeight = FontWeights.Normal;
                item.BorderBrush = _takeoffSectionDropAllowed ? Brushes.SeaGreen : Brushes.IndianRed;
                item.BorderThickness = new Thickness(0, 0, 0, 2);
            }
            else if (ReferenceEquals(item, _takeoffPositionDropTarget))
            {
                item.Background = _takeoffPositionDropAllowed ? dropOk : dropBad;
                item.Foreground = rowFg;
                item.FontWeight = FontWeights.Normal;
                item.BorderBrush = _takeoffPositionDropAllowed ? Brushes.SeaGreen : Brushes.IndianRed;
                item.BorderThickness = _takeoffPositionDropAfter
                    ? new Thickness(0, 0, 0, 2)
                    : new Thickness(0, 2, 0, 0);
            }
            else
            {
                if (sectionSelected || takeoffSelected)
                {
                    item.Background = multiSel;
                    item.Foreground = rowFg;
                }
                else if (isMeasuredOnPage)
                {
                    item.Background = onPageBg;
                    item.Foreground = rowFg;
                }
                else
                {
                    item.ClearValue(Control.BackgroundProperty);
                    item.ClearValue(Control.ForegroundProperty);
                }

                if (isActiveTakeoff)
                {
                    item.Foreground = rowFg;
                    item.FontWeight = FontWeights.Normal;
                    item.BorderBrush = activeAccent;
                    item.BorderThickness = new Thickness(3, 0, 0, 0);
                }
            }
        }
    }

    private bool IsTakeoffMeasuredOnCurrentPage(TakeoffItem takeoff) =>
        _currentPage != null &&
        takeoff.Measurements.Any(m =>
            IsSamePageFolder(m.PageFolder, _currentPage.FolderPath));

    private static IEnumerable<TreeViewItem> EnumerateTakeoffTreeItems(ItemsControl parent)
    {
        foreach (TreeViewItem child in parent.Items.OfType<TreeViewItem>())
        {
            yield return child;
            foreach (TreeViewItem nested in EnumerateTakeoffTreeItems(child))
                yield return nested;
        }
    }

    private bool IsActiveTakeoffItem(TakeoffItem item)
    {
        if (_activeItem == null)
            return false;

        if (ReferenceEquals(_activeItem, item))
            return true;

        return !string.IsNullOrWhiteSpace(_activeItem.FolderPath) &&
               !string.IsNullOrWhiteSpace(item.FolderPath) &&
               string.Equals(_activeItem.FolderPath, item.FolderPath, StringComparison.OrdinalIgnoreCase);
    }

    private void SetTreeItemHeader(TreeViewItem tvi, TakeoffItem item)
    {
        bool isActive = IsActiveTakeoffItem(item);
        Brush swatchBrush = BrushFromHex(item.Color, Brushes.Gray);
        var secondaryBrush = (Brush)Application.Current.Resources["SecondaryForegroundBrush"]
            ?? new SolidColorBrush(Color.FromRgb(128, 128, 128));

        // Filled glyph in the takeoff color (no separate color square).
        var swatchHost = BuildTakeoffSwatchGlyph(item, swatchBrush, isActive ? 18 : 16);

        // Quantity goes to the right via DockPanel for ledger-style alignment.
        var dock = new DockPanel { LastChildFill = true, HorizontalAlignment = HorizontalAlignment.Stretch };

        string total = item.TotalLabel(_viewport.ScaleMetersPerPt, _viewport.UnitMode);
        var totalText = new TextBlock
        {
            Text              = total,
            Foreground        = secondaryBrush,
            FontSize          = 10,
            FontFamily        = new FontFamily("Consolas, Cascadia Mono, Segoe UI"),
            Margin            = new Thickness(8, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment     = TextAlignment.Right,
            MinWidth          = 56,
        };
        DockPanel.SetDock(totalText, Dock.Right);
        dock.Children.Add(totalText);

        if (item.Measurements.Count > 0)
        {
            var sectionsText = new TextBlock
            {
                Text              = SectionCountLabel(item),
                Foreground        = secondaryBrush,
                FontSize          = 10,
                Margin            = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(sectionsText, Dock.Right);
            dock.Children.Add(sectionsText);
        }

        var nameRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        swatchHost.Margin = new Thickness(0, 0, 6, 0);
        nameRow.Children.Add(swatchHost);
        nameRow.Children.Add(new TextBlock
        {
            Text              = item.Name,
            FontWeight        = FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming      = TextTrimming.CharacterEllipsis,
        });
        dock.Children.Add(nameRow);

        tvi.Header = dock;
        tvi.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        tvi.ToolTip = TakeoffItemTooltip(item, isActive);
    }

    private static FrameworkElement BuildTakeoffSwatchGlyph(TakeoffItem item, Brush swatchBrush, double size)
    {
        // Glyph drawn in the takeoff color with a darker stroke — no separate
        // colored square, the glyph itself carries the color identity.
        return Controls.MeasurementGlyph.CreateWpf(
            Controls.MeasurementGlyph.Parse(
                OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType),
                joist: item.IsJoistArea),
            swatchBrush,
            size,
            new Thickness(0));
    }

    private void SetFolderTreeItemHeader(TreeViewItem tvi, TakeoffFolderNode folder)
    {
        TakeoffFolderProperties properties = TakeoffFolderPropertiesStore.Load(folder.FolderPath);
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = folder.Name,
            FontWeight = FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "  folder",
            Foreground = Brushes.Gray,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
        });
        string defaultSummary = TakeoffFolderDefaultSummary(properties);
        if (!string.IsNullOrWhiteSpace(defaultSummary))
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"  default: {defaultSummary}",
                Foreground = Brushes.Gray,
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
        tvi.Header = panel;
        tvi.ToolTip = TakeoffFolderTooltip(folder, properties);
    }

    private string TakeoffFolderStatusText(TakeoffFolderNode folder)
    {
        TakeoffFolderProperties properties = TakeoffFolderPropertiesStore.Load(folder.FolderPath);
        var parts = new List<string> { $"Folder: {folder.Name}" };
        string defaultSummary = TakeoffFolderDefaultSummary(properties);
        if (!string.IsNullOrWhiteSpace(defaultSummary))
            parts.Add($"default {defaultSummary}");
        if (!string.IsNullOrWhiteSpace(properties.Notes))
            parts.Add($"notes: {OneLinePreview(properties.Notes, 90)}");
        return string.Join(" | ", parts);
    }

    private static string? TakeoffFolderTooltip(TakeoffFolderNode folder, TakeoffFolderProperties properties)
    {
        var lines = new List<string> { folder.Name };
        string defaultSummary = TakeoffFolderDefaultSummary(properties);
        if (!string.IsNullOrWhiteSpace(defaultSummary))
            lines.Add($"Default: {defaultSummary}");
        if (!string.IsNullOrWhiteSpace(properties.Notes))
            lines.Add($"Notes: {properties.Notes}");
        return lines.Count <= 1 ? null : string.Join(Environment.NewLine, lines);
    }

    private static string TakeoffFolderDefaultSummary(TakeoffFolderProperties properties)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(properties.DefaultMeasurementType))
            parts.Add(MeasurementTypeTitle(properties.DefaultMeasurementType));
        if (!string.IsNullOrWhiteSpace(properties.DefaultColor))
            parts.Add(properties.DefaultColor);
        if (properties.DefaultUnitPrice is >= 0)
            parts.Add($"price {properties.DefaultUnitPrice.Value:G}");
        if (!string.IsNullOrWhiteSpace(properties.DefaultNamePrefix))
            parts.Add($"prefix {properties.DefaultNamePrefix}");
        if (!string.IsNullOrWhiteSpace(properties.DefaultItemNotes))
            parts.Add($"item notes: {OneLinePreview(properties.DefaultItemNotes, 32)}");
        return string.Join(", ", parts);
    }

    private static string OneLinePreview(string value, int maxLength)
    {
        string text = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= maxLength ? text : text[..Math.Max(0, maxLength - 3)] + "...";
    }

    private string CurrentTakeoffParentFolder()
    {
        string? selectedFolder = TakeoffsTree.SelectedItem is TreeViewItem { Tag: TakeoffFolderNode folder }
            ? folder.FolderPath
            : null;
        string? selectedItemParentFolder = TakeoffsTree.SelectedItem is TreeViewItem { Tag: TakeoffItem item } &&
            !string.IsNullOrWhiteSpace(item.FolderPath)
                ? Path.GetDirectoryName(item.FolderPath) ?? _currentJob?.TakeoffsRoot
                : null;
        return TakeoffCreationPolicy.NewFolderParentFolder(
            _currentJob,
            selectedFolder,
            selectedItemParentFolder,
            _activeTakeoffParentFolder,
            Directory.Exists);
    }

    private string NewTakeoffItemParentFolder() =>
        TakeoffCreationPolicy.NewItemParentFolder(_currentJob);

    private string ResolveTakeoffFolderDefaultMeasurementType(string folderPath, string fallback)
    {
        string fallbackType = OurPlaneCoreJobStore.NormalizeMeasurementType(fallback);
        foreach (TakeoffFolderProperties properties in EnumerateTakeoffFolderProperties(folderPath))
        {
            if (!string.IsNullOrWhiteSpace(properties.DefaultMeasurementType))
                return OurPlaneCoreJobStore.NormalizeMeasurementType(properties.DefaultMeasurementType);
        }

        return fallbackType;
    }

    private string ResolveTakeoffFolderDefaultColor(string folderPath, string fallback)
    {
        foreach (TakeoffFolderProperties properties in EnumerateTakeoffFolderProperties(folderPath))
        {
            if (!string.IsNullOrWhiteSpace(properties.DefaultColor) &&
                IsValidWpfColor(properties.DefaultColor))
            {
                return properties.DefaultColor;
            }
        }

        return IsValidWpfColor(fallback) ? fallback : "#FF4444";
    }

    private double? ResolveTakeoffFolderDefaultUnitPrice(string folderPath)
    {
        foreach (TakeoffFolderProperties properties in EnumerateTakeoffFolderProperties(folderPath))
        {
            if (properties.DefaultUnitPrice is >= 0)
                return properties.DefaultUnitPrice.Value;
        }

        return null;
    }

    private string ResolveTakeoffFolderDefaultItemNotes(string folderPath)
    {
        foreach (TakeoffFolderProperties properties in EnumerateTakeoffFolderProperties(folderPath))
        {
            if (!string.IsNullOrWhiteSpace(properties.DefaultItemNotes))
                return properties.DefaultItemNotes;
        }

        return "";
    }

    private string ResolveTakeoffFolderDefaultNamePrefix(string folderPath)
    {
        foreach (TakeoffFolderProperties properties in EnumerateTakeoffFolderProperties(folderPath))
        {
            if (!string.IsNullOrWhiteSpace(properties.DefaultNamePrefix))
                return properties.DefaultNamePrefix;
        }

        return "";
    }

    private void ApplyTakeoffFolderDefaultsToNewItem(TakeoffItem item, string parentFolder)
    {
        bool changed = false;
        if (ResolveTakeoffFolderDefaultUnitPrice(parentFolder) is { } unitPrice)
        {
            item.UnitPrice = unitPrice;
            changed = true;
        }

        string defaultNotes = ResolveTakeoffFolderDefaultItemNotes(parentFolder);
        if (!string.IsNullOrWhiteSpace(defaultNotes))
        {
            item.Notes = defaultNotes;
            changed = true;
        }

        if (changed)
            OurPlaneCoreJobStore.SaveTakeoffItem(item);
    }

    private IEnumerable<TakeoffFolderProperties> EnumerateTakeoffFolderProperties(string folderPath)
    {
        if (_currentJob == null || string.IsNullOrWhiteSpace(folderPath))
            yield break;

        string? current = folderPath;
        while (!string.IsNullOrWhiteSpace(current) &&
               Directory.Exists(current) &&
               OurPlaneCoreJobStore.IsSameOrDescendant(_currentJob.TakeoffsRoot, current))
        {
            if (TakeoffFolderPropertiesStore.TryLoad(current) != null)
                yield return TakeoffFolderPropertiesStore.Load(current);

            if (string.Equals(current, _currentJob.TakeoffsRoot, StringComparison.OrdinalIgnoreCase))
                yield break;
            current = Path.GetDirectoryName(current);
        }
    }

    private string CurrentToolMeasurementType() =>
        _activeTool is "point" or "area" ? _activeTool : "line";

    private void UpdateToolStatus()
    {
        string title = _activeTool switch
        {
            "point" => MeasurementTypeDisplay("point"),
            "line" => MeasurementTypeDisplay("line"),
            "area" => MeasurementTypeDisplay("area"),
            "select" => "Select",
            "scale" => "Scale",
            "ruler" => "Ruler",
            "drawline" => "Draw Line",
            "drawarrow" => "Arrow",
            "drawrect" => "Box",
            "areacut" => "Area Cut",
            _ => "Pan",
        };
        bool recording = _activeTool is "point" or "line" or "area";
        string item = recording && _activeItem != null
            ? $"  |  Item: {_activeItem.Name}"
            : "";
        TxtTool.Text =
            $"  Tool: {title}  |  Record: {(recording ? "On" : "Off")}" +
            $"  |  Snap: {(_viewport.SnapEnabled ? "On" : "Off")}" +
            $"  |  Ortho: {(_viewport.OrthoEnabled ? "On" : "Off")}" +
            $"  |  Box: {(_viewport.BoxModeEnabled ? "On" : "Off")}{item}";
        UpdateActiveTakeoffTargetBar();
    }

    private void UpdateActiveTakeoffTargetBar()
    {
        if (ActiveTakeoffTargetBar == null)
            return;

        ActiveTakeoffTargetBar.Visibility = Visibility.Visible;
        if (_activeItem == null)
        {
            TxtActiveTakeoffTarget.Text = "";
            TxtActiveTakeoffTargetMeta.Text = "";
            ActiveTakeoffTargetGlyphHost.Child = null;
            BtnActiveTakeoffRecord.Content = "Record";
            BtnActiveTakeoffRecord.ToolTip = "Select a takeoff item before recording (Space)";
            BtnActiveTakeoffMore.ToolTip = "Select a takeoff item for actions";
            BtnActiveTakeoffSheetNext.ToolTip = "No active takeoff item";
            BtnActiveTakeoffRecord.IsEnabled = false;
            BtnActiveTakeoffMore.IsEnabled = false;
            BtnActiveTakeoffFind.IsEnabled = false;
            BtnActiveTakeoffProperties.IsEnabled = false;
            BtnActiveTakeoffPrevious.IsEnabled = false;
            BtnActiveTakeoffNext.IsEnabled = false;
            BtnActiveTakeoffSheetNext.IsEnabled = false;
            return;
        }

        string measurementType = OurPlaneCoreJobStore.NormalizeMeasurementType(_activeItem.MeasurementType);
        string typeTitle = TakeoffTypeDisplay(_activeItem);
        string total = _activeItem.Measurements.Count == 0
            ? "no measurements"
            : _activeItem.TotalLabel(_viewport.ScaleMetersPerPt, _viewport.UnitMode);
        string sheetTotal = ActiveTakeoffSheetTotalText(_activeItem);
        bool recordingThis = _activeTool == measurementType;

        TxtActiveTakeoffTarget.Text = _activeItem.Name;
        TxtActiveTakeoffTargetMeta.Text = $"{TakeoffTypeTitle(_activeItem)} | total: {total}{sheetTotal}";
        ActiveTakeoffTargetGlyphHost.Child = BuildTakeoffSwatchGlyph(
            _activeItem, BrushFromHex(_activeItem.Color, Brushes.Gray), 18);
        BtnActiveTakeoffRecord.Content = recordingThis ? $"Recording {typeTitle}" : $"Record {typeTitle}";
        BtnActiveTakeoffRecord.IsEnabled = _currentPage != null;
        BtnActiveTakeoffMore.IsEnabled = true;
        BtnActiveTakeoffRecord.ToolTip = _currentPage == null
            ? "Select a sheet before recording"
            : recordingThis
                ? $"Recording {typeTitle} into {_activeItem.Name}. Click or press Space to stop."
                : $"Start recording {typeTitle} into {_activeItem.Name} (Space)";
        bool hasTreeItem = FindTakeoffTreeItem(_activeItem) != null;
        BtnActiveTakeoffFind.IsEnabled = hasTreeItem;
        BtnActiveTakeoffProperties.IsEnabled = hasTreeItem;
        bool canCycle = ActiveTakeoffTargetCycleItems().Count > 1;
        BtnActiveTakeoffPrevious.IsEnabled = canCycle;
        BtnActiveTakeoffNext.IsEnabled = canCycle;
        int sheetTargetCount = ActiveSheetTakeoffTargetCycleItems().Count;
        BtnActiveTakeoffSheetNext.IsEnabled = sheetTargetCount > 0;
        BtnActiveTakeoffSheetNext.ToolTip = sheetTargetCount > 0
            ? $"Switch through {sheetTargetCount} takeoff item(s) measured on this sheet"
            : "No takeoff items are measured on this sheet yet";
    }

    private string ActiveTakeoffSheetTotalText(TakeoffItem item)
    {
        if (_currentPage == null)
            return "";

        var pageMeasurements = MeasurementsForTakeoffOnPage(item, _currentPage.FolderPath).ToList();
        string pageQuantity = pageMeasurements.Count == 0
            ? "none on sheet"
            : SheetLegendQuantityText(item, pageMeasurements);
        return $" | sheet: {pageQuantity}";
    }

    private string DefaultTakeoffName(string measurementType)
    {
        string title = MeasurementTypeTitle(measurementType);
        if (_activeItem != null &&
            OurPlaneCoreJobStore.NormalizeMeasurementType(_activeItem.MeasurementType) != measurementType)
            return $"{_activeItem.Name} - {title}";
        if (_currentPage != null)
            return $"{_currentPage.Name} {title}";
        return $"{title} Item";
    }

    private string DefaultTakeoffNameForFolder(string measurementType, string parentFolder)
    {
        string baseName = DefaultTakeoffName(measurementType);
        string prefix = ResolveTakeoffFolderDefaultNamePrefix(parentFolder);
        if (string.IsNullOrWhiteSpace(prefix) ||
            baseName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return baseName;
        }

        return prefix.EndsWith(" ", StringComparison.Ordinal) ||
               prefix.EndsWith("-", StringComparison.Ordinal) ||
               prefix.EndsWith("_", StringComparison.Ordinal)
            ? prefix + baseName
            : $"{prefix} {baseName}";
    }

    private static string MeasurementTypeTitle(string measurementType) =>
        OurPlaneCoreJobStore.NormalizeMeasurementType(measurementType) switch
        {
            "point" => "Count",
            "area" => "Area",
            _ => "Line",
        };

    private static string TakeoffTypeTitle(TakeoffItem item) =>
        item.IsJoistArea ? "Joist" : MeasurementTypeTitle(item.MeasurementType);

    private static string MeasurementTypeSign(string measurementType) =>
        OurPlaneCoreJobStore.NormalizeMeasurementType(measurementType) switch
        {
            "point" => "○",
            "area" => "□",
            _ => "╱",
        };

    private static string TakeoffTypeSign(TakeoffItem item) =>
        item.IsJoistArea ? "□╱" : MeasurementTypeSign(item.MeasurementType);

    private static string MeasurementTypeDisplay(string measurementType) =>
        $"{MeasurementTypeSign(measurementType)} {MeasurementTypeTitle(measurementType)}";

    private static string TakeoffTypeDisplay(TakeoffItem item) =>
        $"{TakeoffTypeSign(item)} {TakeoffTypeTitle(item)}";

    private static string SheetLegendTypeTitle(TakeoffItem item) =>
        item.IsJoistArea ? "Area" : TakeoffTypeTitle(item);

    private static string SheetLegendTypeSign(TakeoffItem item) =>
        item.IsJoistArea ? MeasurementTypeSign("area") : TakeoffTypeSign(item);

    private static FrameworkElement CreateTakeoffTypeIcon(TakeoffItem item, double size, Thickness margin) =>
        Controls.MeasurementGlyph.CreateWpf(
            Controls.MeasurementGlyph.Parse(
                OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType),
                joist: item.IsJoistArea),
            BrushFromHex(item.Color, Brushes.Gray),
            size,
            margin);

    private static FrameworkElement CreateMeasurementTypeIcon(string kind, Brush brush, double size, Thickness margin) =>
        Controls.MeasurementGlyph.CreateWpf(
            Controls.MeasurementGlyph.Parse(OurPlaneCoreJobStore.NormalizeMeasurementType(kind),
                joist: kind.Equals("joist", StringComparison.OrdinalIgnoreCase)),
            brush,
            size,
            margin);

    private string TakeoffUnitText(TakeoffItem item) =>
        item.IsJoistArea ? UnitText("line") : UnitText(item.MeasurementType);

    private string MeasurementUnitText(Measurement measurement) =>
        measurement.JoistEnabled ? UnitText("line") : UnitText(measurement.MType);

    private static string CsvMeasurementType(TakeoffItem item) =>
        item.IsJoistArea ? "joist" : OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType);

    private TreeViewItem? FindFirstTakeoffTreeItem(ItemsControl parent)
    {
        foreach (TreeViewItem tvi in parent.Items.OfType<TreeViewItem>())
        {
            if (tvi.Tag is TakeoffItem)
                return tvi;

            if (FindFirstTakeoffTreeItem(tvi) is { } found)
                return found;
        }

        return null;
    }

    private bool SelectFirstTakeoffItem()
    {
        if (FindFirstTakeoffTreeItem(TakeoffsTree) is not { } first)
        {
            _activeItem = null;
            _viewport.ActiveColor = "#FF4444";
            _viewport.ActiveTakeoffFolder = "";
            RefreshActiveTakeoffVisuals();
            return false;
        }

        first.IsSelected = true;
        first.BringIntoView();
        return true;
    }

    private void SelectTakeoffItem(TakeoffItem item)
    {
        if (FindTakeoffTreeItem(item) is { } tvi)
        {
            _takeoffsMultiSelection.Clear();
            _takeoffSectionMultiSelection.Clear();
            if (!string.IsNullOrWhiteSpace(item.FolderPath))
                _takeoffsMultiSelection.Add(item.FolderPath);
            ApplyTakeoffPageHighlights();
            tvi.IsSelected = true;
            tvi.BringIntoView();
        }
    }

    private void SelectTakeoffItemSilently(TakeoffItem item)
    {
        if (FindTakeoffTreeItem(item) is not { } tvi)
            return;

        _syncingTakeoffTreeSelection = true;
        try
        {
            _takeoffsMultiSelection.Clear();
            _takeoffSectionMultiSelection.Clear();
            if (!string.IsNullOrWhiteSpace(item.FolderPath))
                _takeoffsMultiSelection.Add(item.FolderPath);
            tvi.IsSelected = true;
            tvi.BringIntoView();
        }
        finally
        {
            _syncingTakeoffTreeSelection = false;
        }
    }

    private void ActivateTakeoffItem(TakeoffItem item)
    {
        item.MeasurementType = OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
        _activeItem = item;
        _activeTakeoffParentFolder = Path.GetDirectoryName(item.FolderPath) ?? _currentJob?.TakeoffsRoot ?? "";
        _viewport.ActiveColor = item.Color;
        _viewport.ActiveTakeoffFolder = item.FolderPath;
        if (_activeTool is "point" or "line" or "area" && _activeTool != item.MeasurementType)
            ApplyToolSelection(item.MeasurementType);
        else
            UpdateToolStatus();
    }

    private void SelectFirstTakeoffItemSilently(IReadOnlyList<TakeoffItem> items)
    {
        TreeViewItem? first = items
            .Select(FindTakeoffTreeItem)
            .FirstOrDefault(item => item != null);
        if (first == null)
            return;

        _syncingTakeoffTreeSelection = true;
        try
        {
            ExpandTakeoffFolderAncestorsWithoutTracking(first);
            first.IsSelected = true;
            first.BringIntoView();
        }
        finally
        {
            _syncingTakeoffTreeSelection = false;
        }
    }

    private IReadOnlyList<TakeoffItem> TakeoffItemsForSelection(TreeViewItem? anchor)
    {
        IReadOnlyList<TakeoffsClipboardEntry> entries = anchor == null
            ? []
            : GetSelectedTakeoffEntries(anchor);

        if (entries.Count == 0)
        {
            return anchor?.Tag switch
            {
                TakeoffItem item => [item],
                TakeoffMeasurementNode node => [node.Item],
                TakeoffFolderNode folder => TakeoffItemsInsideFolder(folder.FolderPath),
                _ => [],
            };
        }

        return _takeoffItems
            .Where(item => entries.Any(entry => OurPlaneCoreJobStore.IsSameOrDescendant(entry.SourcePath, item.FolderPath)))
            .GroupBy(item => NormalizePath(item.FolderPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private IReadOnlyList<TakeoffItem> TakeoffItemsInsideFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return [];

        return _takeoffItems
            .Where(item => OurPlaneCoreJobStore.IsSameOrDescendant(folderPath, item.FolderPath))
            .GroupBy(item => NormalizePath(item.FolderPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private void RevealPagesForTakeoffSelection(TreeViewItem? anchor, string? preferredPageFolder = null)
    {
        RevealPagesForTakeoffItems(TakeoffItemsForSelection(anchor), preferredPageFolder ?? _currentPage?.FolderPath);
    }

    private void RevealPagesForTakeoffItems(IReadOnlyList<TakeoffItem> items, string? preferredPageFolder = null)
    {
        _pageTakeoffMultiSelection.Clear();
        _pagesMultiSelection.Clear();

        var selectedFolders = items
            .Select(item => item.FolderPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedFolders.Count == 0)
        {
            ApplyPagesMultiSelectionVisuals();
            return;
        }

        TreeViewItem? preferredLinked = null;
        TreeViewItem? firstLinked = null;
        foreach (TreeViewItem pageItem in EnumeratePageTreeItems())
        {
            if (pageItem.Tag is not PageInfo page)
                continue;

            var matchedTakeoffs = items
                .Where(item => selectedFolders.Contains(item.FolderPath) &&
                               item.Measurements.Any(measurement => IsSamePageFolder(measurement.PageFolder, page.FolderPath)))
                .ToList();
            if (matchedTakeoffs.Count == 0)
                continue;

            ExpandTreeItemAndAncestorsWithoutTracking(pageItem);
            pageItem.IsExpanded = true;
            bool isPreferredPage = !string.IsNullOrWhiteSpace(preferredPageFolder) &&
                                   IsSamePageFolder(page.FolderPath, preferredPageFolder);

            foreach (TakeoffItem takeoff in matchedTakeoffs)
            {
                _pageTakeoffMultiSelection.Add(PageTakeoffSelectionKey(new PageTakeoffNode(page, takeoff)));
                TreeViewItem? linked = FindPageTakeoffTreeItem(page.FolderPath, takeoff.FolderPath);
                firstLinked ??= linked;
                if (isPreferredPage)
                    preferredLinked ??= linked;
            }
        }

        ApplyPagesMultiSelectionVisuals();
        preferredLinked ??= firstLinked;

        if (preferredLinked == null)
        {
            if (_currentPage != null)
                SelectPageTreeNodeSilently(_currentPage.FolderPath);
            return;
        }

        _syncingPageTreeSelection = true;
        try
        {
            ExpandTreeItemAndAncestorsWithoutTracking(preferredLinked);
            BringPageTreeItemIntoCenteredView(preferredLinked);
        }
        finally
        {
            _syncingPageTreeSelection = false;
        }
    }

    private void SelectTakeoffSectionNode(Measurement measurement)
    {
        if (FindTakeoffSectionTreeItem(TakeoffsTree, measurement) is not { } tvi)
            return;

        _syncingTakeoffTreeSelection = true;
        try
        {
            TreeViewItem visibleTarget = TakeoffVisibleSelectionTarget(tvi);
            ExpandTakeoffFolderAncestorsWithoutTracking(visibleTarget);
            visibleTarget.IsSelected = true;
            visibleTarget.BringIntoView();
        }
        finally
        {
            _syncingTakeoffTreeSelection = false;
        }
    }

    private void ExpandTakeoffFolderAncestorsWithoutTracking(TreeViewItem item)
    {
        WithTreeExpansionTrackingSuppressed(() =>
        {
            ItemsControl? parent = ItemsControl.ItemsControlFromItemContainer(item);
            while (parent is TreeViewItem parentItem)
            {
                if (parentItem.Tag is not TakeoffItem)
                    parentItem.IsExpanded = true;
                parent = ItemsControl.ItemsControlFromItemContainer(parentItem);
            }
        });
    }

    private static TreeViewItem TakeoffVisibleSelectionTarget(TreeViewItem item)
    {
        if (item.Tag is TakeoffMeasurementNode &&
            ItemsControl.ItemsControlFromItemContainer(item) is TreeViewItem parentTakeoff &&
            !parentTakeoff.IsExpanded)
        {
            return parentTakeoff;
        }

        return item;
    }

    private TreeViewItem? FindTakeoffTreeItem(TakeoffItem item) =>
        FindTakeoffTreeItem(TakeoffsTree, item);

    private TreeViewItem? FindTakeoffTreeItem(ItemsControl parent, TakeoffItem item)
    {
        foreach (TreeViewItem tvi in parent.Items.OfType<TreeViewItem>())
        {
            if (tvi.Tag is TakeoffItem candidate && ReferenceEquals(candidate, item))
                return tvi;

            if (FindTakeoffTreeItem(tvi, item) is { } found)
                return found;
        }

        return null;
    }

    private TreeViewItem? FindTakeoffSectionTreeItem(ItemsControl parent, Measurement measurement)
    {
        foreach (TreeViewItem tvi in parent.Items.OfType<TreeViewItem>())
        {
            if (tvi.Tag is TakeoffMeasurementNode node && ReferenceEquals(node.Measurement, measurement))
                return tvi;

            if (FindTakeoffSectionTreeItem(tvi, measurement) is { } found)
                return found;
        }

        return null;
    }

    private TreeViewItem? FindTakeoffTreeItemByFolder(string folderPath) =>
        FindTakeoffTreeItemByFolder(TakeoffsTree, folderPath);

    private void SelectTakeoffNodeByFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return;

        if (FindTakeoffTreeItemByFolder(folderPath) is { } item)
        {
            item.IsSelected = true;
            item.IsExpanded = true;
            item.BringIntoView();
        }
    }

    private TreeViewItem? FindTakeoffTreeItemByFolder(ItemsControl parent, string folderPath)
    {
        foreach (TreeViewItem tvi in parent.Items.OfType<TreeViewItem>())
        {
            string? current = tvi.Tag switch
            {
                TakeoffItem item => item.FolderPath,
                TakeoffFolderNode folder => folder.FolderPath,
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(current) &&
                string.Equals(current, folderPath, StringComparison.OrdinalIgnoreCase))
            {
                return tvi;
            }

            if (FindTakeoffTreeItemByFolder(tvi, folderPath) is { } found)
                return found;
        }

        return null;
    }

    private static void RemoveTreeItem(TreeViewItem tvi)
    {
        ItemsControl? parent = ItemsControl.ItemsControlFromItemContainer(tvi);
        parent?.Items.Remove(tvi);
    }

    private void UpdateTotalDisplay()
    {
        RefreshEstimateTable();
        UpdateActiveTakeoffTargetBar();
        if (_activeItem == null || _activeItem.Measurements.Count == 0)
        {
            TxtTotal.Text = "Total: —";
            return;
        }
        TxtTotal.Text = $"Total: {_activeItem.TotalLabel(_viewport.ScaleMetersPerPt, _viewport.UnitMode)}";
    }

    private List<EstimateDisplayRow> BuildEstimateDisplayRows(string filter, bool currentSheetOnly)
    {
        var rows = new List<EstimateDisplayRow>();
        foreach (var item in _takeoffItems.Where(i => i.Measurements.Count > 0))
        {
            var scopedMeasurements = currentSheetOnly
                ? item.Measurements
                    .Where(measurement => _currentPage != null && IsSamePageFolder(measurement.PageFolder, _currentPage.FolderPath))
                    .ToList()
                : item.Measurements.ToList();
            if (scopedMeasurements.Count == 0)
                continue;

            bool itemMatches = EstimateItemMatchesFilter(item, filter);
            var visibleMeasurements = scopedMeasurements
                .Where(m => itemMatches || EstimateMeasurementMatchesFilter(item, m, filter))
                .ToList();
            if (!itemMatches && visibleMeasurements.Count == 0)
                continue;

            rows.Add(new EstimateDisplayRow(
                item.Name,
                currentSheetOnly ? $"{TakeoffTypeDisplay(item)} / {_currentPage?.Name}" : TakeoffTypeDisplay(item),
                scopedMeasurements.Count.ToString(CultureInfo.InvariantCulture),
                currentSheetOnly ? SheetLegendQuantityText(item, scopedMeasurements) : QuantityText(item),
                TakeoffUnitText(item),
                UnitPriceText(item),
                currentSheetOnly ? CostText(item, scopedMeasurements) : CostText(item),
                "",
                item,
                null));
            for (int i = 0; i < item.Measurements.Count; i++)
            {
                Measurement measurement = item.Measurements[i];
                if (!scopedMeasurements.Contains(measurement) || !visibleMeasurements.Contains(measurement))
                    continue;

                rows.Add(new EstimateDisplayRow(
                    $"  {SectionDisplayName(item, measurement, i)}",
                    $"{(measurement.JoistEnabled ? "Joist" : MeasurementTypeSign(measurement.MType))} {SectionPageName(measurement)}".Trim(),
                    "",
                    QuantityText(measurement),
                    MeasurementUnitText(measurement),
                    "",
                    "",
                    measurement.Notes,
                    item,
                    measurement));
            }
        }

        return rows;
    }

    private void RefreshEstimateTable()
    {
        if (_estimateList == null)
            return;

        Measurement? selectedMeasurement = (_estimateList.SelectedItem as EstimateDisplayRow)?.Measurement;
        _syncingEstimateSelection = true;
        try
        {
            string filter = _estimateFilterBox?.Text.Trim() ?? "";
            bool currentSheetOnly = _estimateCurrentSheetOnlyBox?.IsChecked == true && _currentPage != null;
            _estimateList.Items.Clear();
            EstimateDisplayRow? selectedRow = null;
            int itemRows = 0;
            int detailRows = 0;
            double visibleCost = 0;
            foreach (var item in _takeoffItems.Where(i => i.Measurements.Count > 0))
            {
                var scopedMeasurements = currentSheetOnly
                    ? item.Measurements
                        .Where(measurement => _currentPage != null && IsSamePageFolder(measurement.PageFolder, _currentPage.FolderPath))
                        .ToList()
                    : item.Measurements.ToList();
                if (scopedMeasurements.Count == 0)
                    continue;

                bool itemMatches = EstimateItemMatchesFilter(item, filter);
                var visibleMeasurements = scopedMeasurements
                    .Where(m => itemMatches || EstimateMeasurementMatchesFilter(item, m, filter))
                    .ToList();
                if (!itemMatches && visibleMeasurements.Count == 0)
                    continue;

                string costText = currentSheetOnly ? CostText(item, scopedMeasurements) : CostText(item);
                if (double.TryParse(costText, NumberStyles.Float, CultureInfo.InvariantCulture, out double cost))
                    visibleCost += cost;

                _estimateList.Items.Add(new EstimateDisplayRow(
                    item.Name,
                    currentSheetOnly ? $"{TakeoffTypeDisplay(item)} / {_currentPage?.Name}" : TakeoffTypeDisplay(item),
                    scopedMeasurements.Count.ToString(CultureInfo.InvariantCulture),
                    currentSheetOnly ? SheetLegendQuantityText(item, scopedMeasurements) : QuantityText(item),
                    TakeoffUnitText(item),
                    UnitPriceText(item),
                    costText,
                    "",
                    item,
                    null));
                itemRows++;
                for (int i = 0; i < item.Measurements.Count; i++)
                {
                    Measurement measurement = item.Measurements[i];
                    if (!scopedMeasurements.Contains(measurement) || !visibleMeasurements.Contains(measurement))
                        continue;

                    var row = new EstimateDisplayRow(
                        $"  {SectionDisplayName(item, measurement, i)}",
                        $"{(measurement.JoistEnabled ? "□╱" : MeasurementTypeSign(measurement.MType))} {SectionPageName(measurement)}".Trim(),
                        "",
                        QuantityText(measurement),
                        MeasurementUnitText(measurement),
                        "",
                        "",
                        measurement.Notes,
                        item,
                        measurement);
                    _estimateList.Items.Add(row);
                    detailRows++;
                    if (selectedMeasurement != null && ReferenceEquals(selectedMeasurement, measurement))
                        selectedRow = row;
                }
            }

            UpdateEstimateSummaryText(itemRows, detailRows, visibleCost, currentSheetOnly, filter);

            if (selectedRow != null)
            {
                _estimateList.SelectedItem = selectedRow;
                _estimateList.ScrollIntoView(selectedRow);
            }
        }
        finally
        {
            _syncingEstimateSelection = false;
        }
    }

    private static bool EstimateItemMatchesFilter(TakeoffItem item, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        return TextContains(item.Name, filter) ||
               TextContains(TakeoffTypeTitle(item), filter);
    }

    private static bool EstimateMeasurementMatchesFilter(TakeoffItem item, Measurement measurement, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        return TextContains(item.Name, filter) ||
               TextContains(MeasurementTypeTitle(measurement.MType), filter) ||
               TextContains(SectionDisplayName(item, measurement, item.Measurements.IndexOf(measurement)), filter) ||
               TextContains(SectionPageName(measurement), filter) ||
               TextContains(measurement.Notes, filter);
    }

    private static bool TextContains(string value, string filter) =>
        value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

    private void DeleteSection(TakeoffItem item, Measurement measurement)
    {
        DeleteTakeoffSections(new TakeoffMeasurementNode(item, measurement));
    }

    private void DeleteTakeoffSections(TakeoffMeasurementNode anchor)
    {
        var selectedNodes = SelectedTakeoffSectionNodes(anchor, fallbackToAnchor: true);
        if (selectedNodes.Count == 0)
            return;

        string entryTitle = selectedNodes.Count == 1
            ? MeasurementEntryTitle(anchor.Item)
            : MeasurementEntryTitlePlural(selectedNodes);
        if (MessageBox.Show(
                selectedNodes.Count == 1
                    ? $"Delete this {entryTitle.ToLowerInvariant()} from {anchor.Item.Name}?"
                    : $"Delete {selectedNodes.Count} selected {entryTitle}?",
                selectedNodes.Count == 1 ? $"Delete {entryTitle}" : "Delete Takeoff Rows",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var removedMeasurements = selectedNodes
            .Select(node => node.Measurement)
            .Distinct()
            .ToList();
        foreach (var group in selectedNodes.GroupBy(node => node.Item))
        {
            foreach (Measurement measurement in group.Select(node => node.Measurement).Distinct())
                group.Key.Measurements.Remove(measurement);

            OurPlaneCoreJobStore.SaveTakeoffItem(group.Key);
            RefreshTreeItem(group.Key);
        }

        _takeoffSectionMultiSelection.Clear();
        _takeoffSectionRangeAnchorKey = null;
        _viewport.DeleteMeasurements(removedMeasurements);
        RefreshPagesTakeoffIndicators();
        ApplyTakeoffPageHighlights();
        RefreshSheetLegend();
        RefreshEstimateTable();
        UpdateTotalDisplay();
        TxtStatus.Text = removedMeasurements.Count == 1
            ? $"Deleted {entryTitle.ToLowerInvariant()} from {anchor.Item.Name}."
            : $"Deleted {removedMeasurements.Count} selected {entryTitle}.";
    }

    private static string SectionDisplayName(TakeoffItem item, Measurement measurement, int index) =>
        string.IsNullOrWhiteSpace(measurement.Name)
            ? DefaultSectionName(item, measurement, index)
            : measurement.Name;

    private static string DefaultSectionName(TakeoffItem item, Measurement measurement) =>
        DefaultSectionName(item, measurement, Math.Max(0, item.Measurements.IndexOf(measurement)));

    private static string DefaultSectionName(TakeoffItem item, Measurement measurement, int index)
    {
        string page = SectionPageName(measurement);
        string entry = OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "point" ? "Count" : "Section";
        return string.IsNullOrWhiteSpace(page)
            ? $"{entry} {index + 1}"
            : $"{entry} {index + 1} - {page}";
    }

    private static string SectionPageName(Measurement measurement) =>
        string.IsNullOrWhiteSpace(measurement.PageFolder)
            ? ""
            : OurPlaneCoreJobStore.DisplayName(measurement.PageFolder);

    private static string SectionCountLabel(TakeoffItem item) =>
        OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "point"
            ? item.Measurements.Count == 1 ? "1 count" : $"{item.Measurements.Count} counts"
            : item.Measurements.Count == 1 ? "1 section" : $"{item.Measurements.Count} sections";

    private static string MeasurementEntryTitle(TakeoffItem item) =>
        OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "point" ? "Count" : "Section";

    private static string MeasurementEntryTitlePlural(IEnumerable<TakeoffMeasurementNode> nodes)
    {
        var types = nodes
            .Select(node => OurPlaneCoreJobStore.NormalizeMeasurementType(node.Item.MeasurementType))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (types.Count == 1)
            return types[0] == "point" ? "counts" : "sections";
        return "section/count rows";
    }

    private static bool CanRemoveMeasurementVertex(Measurement measurement) =>
        measurement.MType switch
        {
            "line" => measurement.Points.Count > 2,
            "area" => measurement.Points.Count > 3,
            _ => false,
        };

    private static string SectionTooltip(TakeoffItem item)
    {
        var lines = new List<string> { SectionCountLabel(item) };
        for (int i = 0; i < item.Measurements.Count; i++)
        {
            Measurement m = item.Measurements[i];
            string page = string.IsNullOrWhiteSpace(m.PageFolder)
                ? "unknown page"
                : OurPlaneCoreJobStore.DisplayName(m.PageFolder);
            string name = string.IsNullOrWhiteSpace(m.Name)
                ? (OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "point" ? $"Count {i + 1}" : $"Section {i + 1}")
                : m.Name;
            string detail = OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "point"
                ? "1 count"
                : $"{m.Points.Count} vertices";
            lines.Add($"{name}: {page}, {detail}");
            if (!string.IsNullOrWhiteSpace(m.Notes))
                lines.Add($"  Notes: {m.Notes}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string? TakeoffItemTooltip(TakeoffItem item, bool isActive)
    {
        var lines = new List<string>();
        if (isActive)
            lines.Add("Active takeoff target");
        if (!string.IsNullOrWhiteSpace(item.Notes))
            lines.Add($"Notes: {item.Notes}");
        if (item.Measurements.Count > 0)
            lines.Add(SectionTooltip(item));

        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private string QuantityText(TakeoffItem item)
    {
        string mt = OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
        double value = item.Total(_viewport.ScaleMetersPerPt);
        if (item.IsJoistArea)
            return QuantityText("line", value);
        return QuantityText(mt, value);
    }

    private string QuantityText(Measurement measurement)
    {
        string mt = OurPlaneCoreJobStore.NormalizeMeasurementType(measurement.MType);
        double value = measurement.Value(_viewport.ScaleMetersPerPt);
        if (measurement.JoistEnabled)
            return QuantityText("line", value);
        return QuantityText(mt, value);
    }

    private string QuantityText(string mt, double value)
    {
        return mt switch
        {
            "line" => _viewport.UnitMode == UnitMode.Imperial
                ? (value / 0.3048).ToString("F2", CultureInfo.InvariantCulture)
                : value.ToString("F2", CultureInfo.InvariantCulture),
            "area" => _viewport.UnitMode == UnitMode.Imperial
                ? (value / 0.0929030).ToString("F2", CultureInfo.InvariantCulture)
                : value.ToString("F2", CultureInfo.InvariantCulture),
            "point" => value.ToString("F0", CultureInfo.InvariantCulture),
            _ => value.ToString("F2", CultureInfo.InvariantCulture),
        };
    }

    private string UnitText(string measurementType)
    {
        string mt = OurPlaneCoreJobStore.NormalizeMeasurementType(measurementType);
        return mt switch
        {
            "line" => _viewport.UnitMode == UnitMode.Imperial ? "ft" : "m",
            "area" => _viewport.UnitMode == UnitMode.Imperial ? "sf" : "m2",
            "point" => "ea",
            _ => "",
        };
    }

    private static string UnitPriceText(TakeoffItem item) =>
        item.UnitPrice > 0 ? item.UnitPrice.ToString("F2", CultureInfo.InvariantCulture) : "";

    private string CostText(TakeoffItem item)
    {
        if (item.UnitPrice <= 0 || item.Measurements.Count == 0)
            return "";

        double quantity = EstimateQuantity(item);
        return (quantity * item.UnitPrice).ToString("F2", CultureInfo.InvariantCulture);
    }

    private string CostText(TakeoffItem item, IReadOnlyList<Measurement> measurements)
    {
        if (item.UnitPrice <= 0 || measurements.Count == 0)
            return "";

        double quantity = EstimateQuantity(item, measurements);
        return (quantity * item.UnitPrice).ToString("F2", CultureInfo.InvariantCulture);
    }

    private double EstimateQuantity(TakeoffItem item)
    {
        string mt = OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
        double value = item.Total(_viewport.ScaleMetersPerPt);
        return mt switch
        {
            _ when item.IsJoistArea && _viewport.UnitMode == UnitMode.Imperial => value / 0.3048,
            "line" when _viewport.UnitMode == UnitMode.Imperial => value / 0.3048,
            "area" when _viewport.UnitMode == UnitMode.Imperial => value / 0.0929030,
            _ => value,
        };
    }

    private double EstimateQuantity(TakeoffItem item, IReadOnlyList<Measurement> measurements)
    {
        string mt = OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
        double fallbackScale = _currentPage?.ScaleMetersPerPt > 0
            ? _currentPage.ScaleMetersPerPt
            : _viewport.ScaleMetersPerPt;
        double value = measurements.Sum(measurement => measurement.Value(fallbackScale));
        return mt switch
        {
            _ when measurements.Any(measurement => measurement.JoistEnabled) && _viewport.UnitMode == UnitMode.Imperial => value / 0.3048,
            "line" when _viewport.UnitMode == UnitMode.Imperial => value / 0.3048,
            "area" when _viewport.UnitMode == UnitMode.Imperial => value / 0.0929030,
            _ => value,
        };
    }
}
