using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using OurPlaneCore;
using SkiaSharp;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    private readonly List<ThreeDRoofGuide> _threeDRoofGuides = [];
    private readonly List<ThreeDRoofIssue> _threeDRoofIssues = [];
    private readonly List<SKPoint> _threeDRoofGuidePts = [];
    private SKPoint? _threeDRoofRubberEnd;
    private bool _threeDRoofModeEnabled;
    private bool _threeDRoofEdgeSelectModeEnabled;
    private string _threeDRoofGuideKind = ThreeDRoofGuideKinds.Ridge;
    private readonly HashSet<string> _selectedThreeDRoofGuideIds = new(StringComparer.Ordinal);

    public event Action<string, IReadOnlyList<SKPoint>>? ThreeDRoofGuideAdded;
    public event Action<string, bool>? ThreeDRoofGuideSelectionRequested;

    public bool IsThreeDRoofModeEnabled => _threeDRoofModeEnabled;
    public bool IsThreeDRoofEdgeSelectModeEnabled => _threeDRoofEdgeSelectModeEnabled;
    public string ThreeDRoofGuideKind => _threeDRoofGuideKind;

    public void SetThreeDRoofMode(bool enabled, string guideKind)
    {
        _threeDRoofGuideKind = ThreeDRoofGuideKinds.Normalize(guideKind);
        _threeDRoofModeEnabled = enabled;
        _threeDRoofGuidePts.Clear();
        _threeDRoofRubberEnd = null;
        SetSnapPreview(null);
        if (enabled)
            Cursor = Cursors.Cross;
        else
            UpdateCursor();
        PostThreeDRoofStatus();
        RequestRepaint();
    }

    public void SetThreeDRoofEdgeSelectMode(bool enabled)
    {
        _threeDRoofEdgeSelectModeEnabled = enabled;
        if (enabled)
        {
            _threeDRoofModeEnabled = false;
            _threeDRoofGuidePts.Clear();
            _threeDRoofRubberEnd = null;
            Cursor = Cursors.Hand;
        }
        else
        {
            UpdateCursor();
        }

        PostThreeDRoofStatus();
        RequestRepaint();
    }

    public void SetThreeDRoofGuideKind(string guideKind)
    {
        _threeDRoofGuideKind = ThreeDRoofGuideKinds.Normalize(guideKind);
        PostThreeDRoofStatus();
        RequestRepaint();
    }

    public void SetThreeDRoofGuides(IEnumerable<ThreeDRoofGuide> guides)
    {
        _threeDRoofGuides.Clear();
        _threeDRoofGuides.AddRange(guides);
        var availableIds = _threeDRoofGuides
            .Select(guide => guide.Id)
            .ToHashSet(StringComparer.Ordinal);
        _selectedThreeDRoofGuideIds.RemoveWhere(id => !availableIds.Contains(id));

        RequestRepaint();
    }

    public void SetSelectedThreeDRoofGuide(string? guideId)
    {
        _selectedThreeDRoofGuideIds.Clear();
        if (!string.IsNullOrWhiteSpace(guideId))
            _selectedThreeDRoofGuideIds.Add(guideId);
        RequestRepaint();
    }

    public void SetSelectedThreeDRoofGuides(IEnumerable<string> guideIds)
    {
        _selectedThreeDRoofGuideIds.Clear();
        foreach (string guideId in guideIds)
        {
            if (!string.IsNullOrWhiteSpace(guideId))
                _selectedThreeDRoofGuideIds.Add(guideId);
        }

        RequestRepaint();
    }

    public void SetThreeDRoofIssues(IEnumerable<ThreeDRoofIssue> issues)
    {
        _threeDRoofIssues.Clear();
        _threeDRoofIssues.AddRange(issues);
        RequestRepaint();
    }

    public void ClearThreeDRoofGuides()
    {
        _threeDRoofGuides.Clear();
        _threeDRoofIssues.Clear();
        _threeDRoofGuidePts.Clear();
        _threeDRoofRubberEnd = null;
        _selectedThreeDRoofGuideIds.Clear();
        RequestRepaint();
    }

    private bool HandleThreeDRoofClick(SKPoint rawPdf)
    {
        if (!_threeDRoofModeEnabled)
            return false;

        SKPoint pdf = ResolveThreeDRoofPoint(rawPdf, updatePreview: true);
        _threeDRoofGuidePts.Add(pdf);
        if (_threeDRoofGuidePts.Count == 1)
        {
            _threeDRoofRubberEnd = pdf;
            PostThreeDRoofStatus();
            RequestRepaint();
            return true;
        }

        ThreeDRoofGuideAdded?.Invoke(_threeDRoofGuideKind, _threeDRoofGuidePts.Take(2).ToList());
        _threeDRoofGuidePts.Clear();
        _threeDRoofRubberEnd = null;
        SetSnapPreview(null);
        PostThreeDRoofStatus();
        RequestRepaint();
        return true;
    }

    private bool HandleThreeDRoofEdgeSelectionClick(SKPoint rawPdf)
    {
        if (!_threeDRoofEdgeSelectModeEnabled)
            return false;

        if (TryFindThreeDRoofGuideAt(rawPdf, out string guideId))
        {
            bool additive = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != ModifierKeys.None;
            ThreeDRoofGuideSelectionRequested?.Invoke(guideId, additive);
        }
        else if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == ModifierKeys.None)
        {
            ThreeDRoofGuideSelectionRequested?.Invoke("", false);
        }

        RequestRepaint();
        return true;
    }

    private bool UpdateThreeDRoofPointer(SKPoint rawPdf)
    {
        if (_threeDRoofEdgeSelectModeEnabled)
        {
            _lastPointerPdf = rawPdf;
            RequestRepaint();
            return true;
        }

        if (!_threeDRoofModeEnabled)
            return false;

        SKPoint pdf = ResolveThreeDRoofPoint(rawPdf, updatePreview: true);
        _lastPointerPdf = pdf;
        if (_threeDRoofGuidePts.Count > 0)
            _threeDRoofRubberEnd = pdf;

        PostThreeDRoofStatus();
        RequestRepaint();
        return true;
    }

    private bool HandleThreeDRoofKey(KeyEventArgs e)
    {
        if (!_threeDRoofModeEnabled)
            return false;

        Key key = KeyboardShortcutKeys.EffectiveKey(e);
        switch (key)
        {
            case Key.Escape:
                if (_threeDRoofGuidePts.Count > 0)
                {
                    _threeDRoofGuidePts.Clear();
                    _threeDRoofRubberEnd = null;
                    SetSnapPreview(null);
                    PostStatus("3D Roof: guide cancelled.");
                }
                else
                {
                    SetThreeDRoofMode(false, _threeDRoofGuideKind);
                    PostStatus("3D Roof mode off.");
                }

                e.Handled = true;
                return true;
            case Key.Back:
            case Key.Z when Keyboard.Modifiers == ModifierKeys.Control:
                if (_threeDRoofGuidePts.Count > 0)
                {
                    _threeDRoofGuidePts.RemoveAt(_threeDRoofGuidePts.Count - 1);
                    _threeDRoofRubberEnd = null;
                    SetSnapPreview(null);
                    PostThreeDRoofStatus();
                    RequestRepaint();
                    e.Handled = true;
                    return true;
                }
                break;
            case Key.R:
                SetThreeDRoofGuideKind(ThreeDRoofGuideKinds.Ridge);
                e.Handled = true;
                return true;
            case Key.H:
                SetThreeDRoofGuideKind(ThreeDRoofGuideKinds.Hip);
                e.Handled = true;
                return true;
            case Key.V:
                SetThreeDRoofGuideKind(ThreeDRoofGuideKinds.Valley);
                e.Handled = true;
                return true;
            case Key.E:
                SetThreeDRoofGuideKind(ThreeDRoofGuideKinds.Eave);
                e.Handled = true;
                return true;
            case Key.K:
                SetThreeDRoofGuideKind(ThreeDRoofGuideKinds.Rake);
                e.Handled = true;
                return true;
            case Key.P:
                SetThreeDRoofGuideKind(ThreeDRoofGuideKinds.Pitch);
                e.Handled = true;
                return true;
        }

        return false;
    }

    private SKPoint ResolveThreeDRoofPoint(SKPoint rawPdf, bool updatePreview)
    {
        if (TryFindDigitizerSnapPoint(rawPdf, out SKPoint snapped, out string snapKind))
        {
            if (updatePreview)
                SetSnapPreview(snapped, snapKind);
            return snapped;
        }

        if (updatePreview)
            SetSnapPreview(null);

        return TryGetThreeDRoofOrthoAnchor(out SKPoint anchor) && IsOrthoActive()
            ? ApplyOrtho(anchor, rawPdf)
            : rawPdf;
    }

    private bool TryGetThreeDRoofOrthoAnchor(out SKPoint anchor)
    {
        if (_threeDRoofGuidePts.Count > 0)
        {
            anchor = _threeDRoofGuidePts[^1];
            return true;
        }

        anchor = default;
        return false;
    }

    private void DrawThreeDRoofGuides(SKCanvas canvas, SKRect visiblePdf)
    {
        if (_threeDRoofGuides.Count == 0 && _threeDRoofIssues.Count == 0 && _threeDRoofGuidePts.Count == 0)
            return;

        foreach (ThreeDRoofGuide guide in _threeDRoofGuides)
            DrawThreeDRoofGuide(canvas, guide, visiblePdf, committed: true);

        DrawThreeDRoofIssues(canvas, visiblePdf);

        if (_threeDRoofModeEnabled)
            DrawThreeDRoofDraft(canvas);
        else if (_threeDRoofEdgeSelectModeEnabled)
            DrawThreeDRoofEdgeSelectBadge(canvas);
    }

    private void DrawThreeDRoofGuide(SKCanvas canvas, ThreeDRoofGuide guide, SKRect visiblePdf, bool committed)
    {
        if (guide.Points.Count < 2)
            return;

        bool selected = _selectedThreeDRoofGuideIds.Contains(guide.Id);
        SKColor color = ParseRoofGuideColor(guide.Color);
        using var line = new SKPaint
        {
            Color = selected ? color.WithAlpha(255) : color.WithAlpha(committed ? (byte)210 : (byte)180),
            StrokeWidth = ScreenToPdfDistance(selected ? 4.4f : committed ? 2.4f : 1.8f),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };
        using var dot = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        float radius = ScreenToPdfDistance(4.5f);
        for (int i = 1; i < guide.Points.Count; i++)
        {
            ThreeDRoofGuidePoint a = guide.Points[i - 1];
            ThreeDRoofGuidePoint b = guide.Points[i];
            if (!SegmentTouchesVisiblePdf(a.PdfX, a.PdfY, b.PdfX, b.PdfY, visiblePdf))
                continue;

            canvas.DrawLine((float)a.PdfX, (float)a.PdfY, (float)b.PdfX, (float)b.PdfY, line);
        }

        foreach (ThreeDRoofGuidePoint point in guide.Points)
        {
            if (visiblePdf.Contains((float)point.PdfX, (float)point.PdfY))
                canvas.DrawCircle((float)point.PdfX, (float)point.PdfY, radius, dot);
        }
    }

    private void DrawThreeDRoofDraft(SKCanvas canvas)
    {
        SKColor color = ParseRoofGuideColor(ThreeDRoofGuideKinds.Color(_threeDRoofGuideKind));
        using var line = new SKPaint
        {
            Color = color.WithAlpha(190),
            StrokeWidth = ScreenToPdfDistance(2.2f),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            PathEffect = SKPathEffect.CreateDash([ScreenToPdfDistance(6f), ScreenToPdfDistance(4f)], 0),
        };
        using var dot = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        float radius = ScreenToPdfDistance(5f);
        foreach (SKPoint point in _threeDRoofGuidePts)
            canvas.DrawCircle(point, radius, dot);
        if (_threeDRoofGuidePts.Count > 0 && _threeDRoofRubberEnd.HasValue)
            canvas.DrawLine(_threeDRoofGuidePts[^1], _threeDRoofRubberEnd.Value, line);

        DrawThreeDRoofModeBadge(canvas, color);
    }

    private void DrawThreeDRoofIssues(SKCanvas canvas, SKRect visiblePdf)
    {
        if (_threeDRoofIssues.Count == 0)
            return;

        foreach (ThreeDRoofIssue issue in _threeDRoofIssues)
        {
            if (!issue.HasPdfPoint)
                continue;

            var point = new SKPoint((float)issue.PdfX, (float)issue.PdfY);
            if (!visiblePdf.Contains(point.X, point.Y))
                continue;

            SKColor color = ParseRoofGuideColor(issue.Color);
            float radius = ScreenToPdfDistance(issue.Severity == "error" ? 8f : 6f);
            using var fill = new SKPaint
            {
                Color = color.WithAlpha(55),
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };
            using var stroke = new SKPaint
            {
                Color = color,
                StrokeWidth = ScreenToPdfDistance(2.2f),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
            };
            canvas.DrawCircle(point, radius, fill);
            canvas.DrawCircle(point, radius, stroke);
            canvas.DrawLine(point.X - radius, point.Y - radius, point.X + radius, point.Y + radius, stroke);
            canvas.DrawLine(point.X - radius, point.Y + radius, point.X + radius, point.Y - radius, stroke);
            DrawThreeDRoofIssueLabel(canvas, issue, point, color, radius);
        }
    }

    private void DrawThreeDRoofIssueLabel(SKCanvas canvas, ThreeDRoofIssue issue, SKPoint point, SKColor color, float radius)
    {
        string label = string.IsNullOrWhiteSpace(issue.Message) ? issue.Kind : issue.Message;
        if (label.Length > 58)
            label = label[..55] + "...";

        float textSize = ScreenToPdfDistance(10f);
        float pad = ScreenToPdfDistance(3.5f);
        using var text = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            TextSize = textSize,
            Typeface = LabelTypeface,
        };
        SKRect bounds = new();
        text.MeasureText(label, ref bounds);
        var rect = new SKRect(
            point.X + radius + pad,
            point.Y - textSize - pad * 2,
            point.X + radius + pad * 3 + bounds.Width,
            point.Y + pad);
        using var fill = new SKPaint
        {
            Color = SKColors.White.WithAlpha(230),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var border = new SKPaint
        {
            Color = color.WithAlpha(180),
            StrokeWidth = ScreenToPdfDistance(1f),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };
        canvas.DrawRoundRect(rect, pad, pad, fill);
        canvas.DrawRoundRect(rect, pad, pad, border);
        canvas.DrawText(label, rect.Left + pad, rect.Bottom - pad * 1.5f, text);
    }

    private void DrawThreeDRoofModeBadge(SKCanvas canvas, SKColor color)
    {
        if (!_threeDRoofModeEnabled || !_lastPointerPdf.HasValue)
            return;

        SKPoint point = _lastPointerPdf.Value;
        string label = $"3D Roof {ThreeDRoofGuideKinds.Title(_threeDRoofGuideKind)}";
        float textSize = ScreenToPdfDistance(11f);
        float pad = ScreenToPdfDistance(4f);
        using var text = new SKPaint
        {
            Color = color,
            IsAntialias = true,
            TextSize = textSize,
            Typeface = LabelTypeface,
        };
        SKRect bounds = new();
        text.MeasureText(label, ref bounds);
        var rect = new SKRect(
            point.X + pad * 2,
            point.Y - textSize - pad * 3,
            point.X + pad * 4 + bounds.Width,
            point.Y - pad);
        using var fill = new SKPaint
        {
            Color = SKColors.White.WithAlpha(215),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var stroke = new SKPaint
        {
            Color = color.WithAlpha(180),
            StrokeWidth = ScreenToPdfDistance(1f),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };
        canvas.DrawRoundRect(rect, pad, pad, fill);
        canvas.DrawRoundRect(rect, pad, pad, stroke);
        canvas.DrawText(label, rect.Left + pad, rect.Bottom - pad * 1.4f, text);
    }

    private void PostThreeDRoofStatus()
    {
        if (_threeDRoofEdgeSelectModeEnabled)
        {
            PostStatus("3D Roof Edge Select: click roof base edges, enter pitch, then Edge Pitch. Ctrl/Shift-click adds more.");
            return;
        }

        if (!_threeDRoofModeEnabled)
            return;

        string title = ThreeDRoofGuideKinds.Title(_threeDRoofGuideKind);
        PostStatus(_threeDRoofGuidePts.Count == 0
            ? $"3D Roof {title}: click first point. Hotkeys R/H/V/E/K/P change guide kind."
            : $"3D Roof {title}: click second point. Esc cancels this guide.");
    }

    private bool TryFindThreeDRoofGuideAt(SKPoint pdf, out string guideId)
    {
        guideId = "";
        if (_threeDRoofGuides.Count == 0)
            return false;

        double tolerance = Math.Max(3.0, ScreenToPdfDistance(16f));
        string bestGuideId = "";
        double bestDistance = double.PositiveInfinity;
        foreach (ThreeDRoofGuide guide in _threeDRoofGuides.Where(IsSelectableRoofBaseGuide))
        {
            for (int i = 1; i < guide.Points.Count; i++)
            {
                ThreeDRoofGuidePoint a = guide.Points[i - 1];
                ThreeDRoofGuidePoint b = guide.Points[i];
                double distance = DistanceToSegment(pdf.X, pdf.Y, a.PdfX, a.PdfY, b.PdfX, b.PdfY);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestGuideId = guide.Id;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(bestGuideId) || bestDistance > tolerance)
            return false;

        guideId = bestGuideId;
        return true;
    }

    private static bool IsSelectableRoofBaseGuide(ThreeDRoofGuide guide) =>
        !string.Equals(guide.Status, ThreeDRoofPreviewBuilder.GeneratedSeamStatus, StringComparison.OrdinalIgnoreCase);

    private void DrawThreeDRoofEdgeSelectBadge(SKCanvas canvas)
    {
        if (!_lastPointerPdf.HasValue)
            return;

        SKPoint point = _lastPointerPdf.Value;
        string label = "Roof Edge Select";
        float textSize = ScreenToPdfDistance(11f);
        float pad = ScreenToPdfDistance(4f);
        using var text = new SKPaint
        {
            Color = SKColors.DarkSlateGray,
            IsAntialias = true,
            TextSize = textSize,
            Typeface = LabelTypeface,
        };
        SKRect bounds = new();
        text.MeasureText(label, ref bounds);
        var rect = new SKRect(
            point.X + pad * 2,
            point.Y - textSize - pad * 3,
            point.X + pad * 4 + bounds.Width,
            point.Y - pad);
        using var fill = new SKPaint
        {
            Color = SKColors.White.WithAlpha(215),
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };
        using var stroke = new SKPaint
        {
            Color = SKColors.DarkSlateGray.WithAlpha(180),
            StrokeWidth = ScreenToPdfDistance(1f),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };
        canvas.DrawRoundRect(rect, pad, pad, fill);
        canvas.DrawRoundRect(rect, pad, pad, stroke);
        canvas.DrawText(label, rect.Left + pad, rect.Bottom - pad * 1.4f, text);
    }

    private static double DistanceToSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax;
        double dy = by - ay;
        double len2 = dx * dx + dy * dy;
        if (len2 <= 0.000001)
            return Distance(px, py, ax, ay);

        double t = ((px - ax) * dx + (py - ay) * dy) / len2;
        t = Math.Clamp(t, 0, 1);
        return Distance(px, py, ax + dx * t, ay + dy * t);
    }

    private static double Distance(double ax, double ay, double bx, double by)
    {
        double dx = bx - ax;
        double dy = by - ay;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static bool SegmentTouchesVisiblePdf(double ax, double ay, double bx, double by, SKRect visiblePdf)
    {
        double left = Math.Min(ax, bx);
        double right = Math.Max(ax, bx);
        double top = Math.Min(ay, by);
        double bottom = Math.Max(ay, by);
        return right >= visiblePdf.Left &&
               left <= visiblePdf.Right &&
               bottom >= visiblePdf.Top &&
               top <= visiblePdf.Bottom;
    }

    private static SKColor ParseRoofGuideColor(string hex)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex) &&
                SKColor.TryParse(hex, out SKColor color))
            {
                return color;
            }
        }
        catch
        {
        }

        return new SKColor(0x8B, 0x5C, 0xF6);
    }
}
