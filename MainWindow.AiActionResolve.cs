using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OurPlanCore.Controls;
using SkiaSharp;


namespace OurPlanCore;

public partial class MainWindow
{
    // AI action target, page, geometry, and naming helpers.

    private TakeoffItem? ResolveReviewedAiActionTarget(
        AiActionReviewRow row,
        SmartAiAction action,
        string measurementType,
        Dictionary<string, TakeoffItem> createdByType)
    {
        AiActionTargetOption? option = row.Target;
        if (option == null ||
            !string.Equals(option.MeasurementType, measurementType, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (option.Item != null)
        {
            string targetType = OurPlanCoreJobStore.NormalizeMeasurementType(option.Item.MeasurementType);
            return string.Equals(targetType, measurementType, StringComparison.OrdinalIgnoreCase)
                ? option.Item
                : null;
        }

        if (!option.CreatesNewItem)
            return null;

        if (createdByType.TryGetValue(measurementType, out TakeoffItem? existing))
            return existing;

        string name = AiActionTakeoffName(action, measurementType);
        var created = CreateUniqueTakeoffItem(name, "#00BCD4", measurementType, NewTakeoffItemParentFolder());
        _takeoffItems.Add(created);
        var parent = FindTakeoffTreeItemByFolder(Path.GetDirectoryName(created.FolderPath) ?? "") ?? (ItemsControl)TakeoffsTree;
        var tvi = AddTakeoffTreeItem(created, parent);
        if (parent is TreeViewItem parentTvi)
            parentTvi.IsExpanded = true;
        tvi.IsSelected = true;
        createdByType[measurementType] = created;
        return created;
    }

    private TakeoffItem ResolveAiActionTakeoffItem(
        SmartAiAction action,
        string measurementType,
        Dictionary<string, TakeoffItem> createdByType)
    {
        if (_activeItem != null &&
            string.Equals(
                OurPlanCoreJobStore.NormalizeMeasurementType(_activeItem.MeasurementType),
                measurementType,
                StringComparison.OrdinalIgnoreCase))
        {
            return _activeItem;
        }

        if (createdByType.TryGetValue(measurementType, out TakeoffItem? existing))
            return existing;

        string name = AiActionTakeoffName(action, measurementType);
        var created = CreateUniqueTakeoffItem(name, "#00BCD4", measurementType, NewTakeoffItemParentFolder());
        _takeoffItems.Add(created);
        var parent = FindTakeoffTreeItemByFolder(Path.GetDirectoryName(created.FolderPath) ?? "") ?? (ItemsControl)TakeoffsTree;
        var tvi = AddTakeoffTreeItem(created, parent);
        if (parent is TreeViewItem parentTvi)
            parentTvi.IsExpanded = true;
        tvi.IsSelected = true;
        createdByType[measurementType] = created;
        return created;
    }

    private PageInfo? ResolveAiActionPage(SmartAiAction action, SmartAiActionDraft draft, ObservationDisplayItem item)
    {
        if (FindPageByName(action.Page) is { } actionPage)
            return actionPage;
        if (FindPageByName(draft.Page) is { } draftPage)
            return draftPage;
        if (FindPageByName(item.Page) is { } observationPage)
            return observationPage;
        return _currentPage;
    }

    private static string NormalizeAiActionMeasurementType(SmartAiAction action)
    {
        string value = action.MeasurementType;
        if (string.IsNullOrWhiteSpace(value))
        {
            value = action.Type.Contains("area", StringComparison.OrdinalIgnoreCase)
                ? "area"
                : action.Type.Contains("point", StringComparison.OrdinalIgnoreCase)
                    ? "point"
                    : "line";
        }

        return OurPlanCoreJobStore.NormalizeMeasurementType(value.Trim().ToLowerInvariant());
    }

    private static List<SKPoint> ActionPoints(SmartAiAction action) =>
        action.Points
            .Select(point => new SKPoint(point.X, point.Y))
            .Where(point => !float.IsNaN(point.X) && !float.IsNaN(point.Y))
            .ToList();

    private static int ValidActionPointCount(SmartAiAction action) =>
        HasValidMeasurementGeometry(NormalizeAiActionMeasurementType(action), ActionPoints(action))
            ? action.Points.Count
            : 0;

    private static bool HasValidMeasurementGeometry(string measurementType, IReadOnlyList<SKPoint> points) =>
        measurementType switch
        {
            "point" => points.Count >= 1,
            "area" => points.Count >= 3,
            _ => points.Count >= 2,
        };

    private static string AiActionTakeoffName(SmartAiAction action, string measurementType)
    {
        string label = string.IsNullOrWhiteSpace(action.Label)
            ? MeasurementTypeTitle(measurementType)
            : action.Label.Trim();
        return $"AI {label}";
    }

    private static string AiActionMeasurementName(SmartAiAction action, TakeoffItem target)
    {
        if (!string.IsNullOrWhiteSpace(action.Label))
            return action.Label.Trim();
        return DefaultSectionName(target, new Measurement { PageFolder = "" }, target.Measurements.Count);
    }

    private static string AiActionMeasurementNotes(SmartAiAction action, SmartAiActionDraft draft)
    {
        var lines = new List<string> { "Created from AI action draft." };
        if (!string.IsNullOrWhiteSpace(draft.RequestId))
            lines.Add($"Request: {draft.RequestId}");
        if (!string.IsNullOrWhiteSpace(action.Type))
            lines.Add($"Action: {action.Type}");
        if (action.Confidence > 0)
            lines.Add($"Confidence: {action.Confidence:P0}");
        if (!string.IsNullOrWhiteSpace(action.Notes))
            lines.Add(action.Notes.Trim());
        return string.Join(Environment.NewLine, lines);
    }
}
