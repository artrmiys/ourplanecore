using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Docnet.Core;
using Docnet.Core.Models;
using OurPlanCore;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    private bool EnsureScaleForLinearArea()
    {
        if (!IsMissingScaleForLinearArea())
            return true;

        if (_drawPts.Count > 0 || _rubberEnd.HasValue || _snapPreview.HasValue)
        {
            _drawPts.Clear();
            _rubberEnd = null;
            SetSnapPreview(null);
            RequestRepaint();
        }

        PostScaleRequiredStatus();
        return false;
    }

    private bool IsMissingScaleForLinearArea() =>
        _tool is ViewerTool.Line or ViewerTool.Area or ViewerTool.Ruler or ViewerTool.Beam or ViewerTool.Openings && ScaleMetersPerPt <= 0;

    private void PostScaleRequiredStatus()
    {
        string tool = _tool switch
        {
            ViewerTool.Area => "Area",
            ViewerTool.Ruler => "Ruler",
            ViewerTool.Beam => "Beam",
            ViewerTool.Openings => "Openings",
            _ => "Line",
        };
        string mode = _tool is ViewerTool.Ruler or ViewerTool.Beam or ViewerTool.Openings ? "markup" : "Record";
        PostStatus($"{tool} {mode} blocked: set sheet scale first with Scale or PDF Auto Scale. Count and drawing markups can be recorded without scale.");
    }

    private void PostRecordPrompt()
    {
        string modes = DigitizerModeSuffix();
        switch (_tool)
        {
            case ViewerTool.Point:
                PostStatus($"Count Record: click each item to add a count. Turn Record off for Pan.{modes}");
                break;
            case ViewerTool.Line:
                if (IsMissingScaleForLinearArea())
                {
                    PostScaleRequiredStatus();
                    break;
                }

                if (BoxModeEnabled)
                {
                    PostStatus(_drawPts.Count == 0
                        ? $"Line Box: click the first corner.{modes}"
                        : $"Line Box: click the opposite corner to create a rectangular perimeter.{modes}");
                    break;
                }

                PostStatus(_drawPts.Count switch
                {
                    0 => $"Line Record: click the first point.{modes}",
                    1 => $"Line Record: click the next point. Backspace/Ctrl+Z undo.{modes}",
                    _ => $"Line Record: click next point, or Esc / C / double-click to finish.{modes}",
                });
                break;
            case ViewerTool.Area:
                if (IsMissingScaleForLinearArea())
                {
                    PostScaleRequiredStatus();
                    break;
                }

                if (BoxModeEnabled)
                {
                    PostStatus(_drawPts.Count == 0
                        ? $"Area Box: click the first corner.{modes}"
                        : $"Area Box: click the opposite corner to create a rectangular area.{modes}");
                    break;
                }

                PostStatus(_drawPts.Count switch
                {
                    0 => $"Area Record: click the first corner.{modes}",
                    1 => $"Area Record: click the next corner. Backspace/Ctrl+Z undo.{modes}",
                    2 => $"Area Record: click at least one more corner, then finish.{modes}",
                    _ => $"Area Record: click next corner, or Esc / C / double-click to finish.{modes}",
                });
                break;
            case ViewerTool.AreaCut:
                if (BoxModeEnabled)
                {
                    PostStatus(_drawPts.Count == 0
                        ? $"Cut Box: click the first corner over Area/Line geometry.{modes}"
                        : $"Cut Box: click the opposite corner to erase Line pieces or cut Area holes.{modes}");
                }
                else
                {
                    PostStatus(_drawPts.Count switch
                    {
                        0 => $"Cut: click the first polygon point over Area/Line geometry.{modes}",
                        1 => $"Cut: click the next polygon point.{modes}",
                        2 => $"Cut: click at least one more point, then C / Esc / double-click to finish.{modes}",
                        _ => $"Cut: click next point, or C / Esc / double-click to finish.{modes}",
                    });
                }
                break;
            case ViewerTool.Scale:
                PostStatus(_scalePts.Count == 0
                    ? $"Scale: click the first point of a known distance.{modes}"
                    : $"Scale: click the second point of a known distance.{modes}");
                break;
            case ViewerTool.Ruler:
                if (IsMissingScaleForLinearArea())
                {
                    PostScaleRequiredStatus();
                    break;
                }

                PostStatus(_drawPts.Count == 0
                    ? $"Ruler: click the first endpoint.{modes}"
                    : $"Ruler: click the second endpoint to place the dimension label.{modes}");
                break;
            case ViewerTool.Pitch:
                PostStatus(_drawPts.Count == 0
                    ? $"Pitch: click the first roof-slope point.{modes}"
                    : $"Pitch: click the second point to place the rise:12 label.{modes}");
                break;
            case ViewerTool.Beam:
                if (IsMissingScaleForLinearArea())
                {
                    PostScaleRequiredStatus();
                    break;
                }

                PostStatus(_drawPts.Count == 0
                    ? $"Beam: click the first endpoint.{modes}"
                    : $"Beam: click the second endpoint to create the Ruler and Count item.{modes}");
                break;
            case ViewerTool.Openings:
                if (IsMissingScaleForLinearArea())
                {
                    PostScaleRequiredStatus();
                    break;
                }

                PostStatus(_drawPts.Count == 0
                    ? $"Openings: click the first corner.{modes}"
                    : $"Openings: click the opposite corner to create size Rulers and a Count item.{modes}");
                break;
            case ViewerTool.DrawHighlight:
                PostStatus(_drawPts.Count == 0
                    ? $"Highlighter: click the first corner.{modes}"
                    : $"Highlighter: click the opposite corner.{modes}");
                break;
            case ViewerTool.DrawLine:
                PostStatus(_drawPts.Count switch
                {
                    0 => $"Draw line: click the first point.{modes}",
                    1 => $"Draw line: click the next point. Backspace/Ctrl+Z undo.{modes}",
                    _ => $"Draw line: click next point, or Esc / C / double-click to finish.{modes}",
                });
                break;
            case ViewerTool.DrawArrow:
                PostStatus(_drawPts.Count == 0
                    ? $"Arrow: click the tail point.{modes}"
                    : $"Arrow: click the arrow head point.{modes}");
                break;
            case ViewerTool.DrawRect:
                PostStatus(_drawPts.Count == 0
                    ? $"Box: click the first corner.{modes}"
                    : $"Box: click the opposite corner.{modes}");
                break;
            case ViewerTool.DrawCloud:
                PostStatus(_drawPts.Count == 0
                    ? $"Cloud: click the first corner.{modes}"
                    : $"Cloud: click the opposite corner.{modes}");
                break;
            case ViewerTool.DrawArea:
                PostStatus(_drawPts.Count switch
                {
                    0 => $"Area annotation: click the first corner.{modes}",
                    1 => $"Area annotation: click the next corner. Backspace/Ctrl+Z undo.{modes}",
                    2 => $"Area annotation: click at least one more corner, then C / Esc / double-click to finish.{modes}",
                    _ => $"Area annotation: click next corner, or C / Esc / double-click to finish.{modes}",
                });
                break;
            case ViewerTool.Note:
                PostStatus($"Note: click the sheet to place a text note.{modes}");
                break;
            case ViewerTool.Select:
                PostStatus("Select: drag a box around measurements or markups. Ctrl toggles, Ctrl+Shift removes; Shift constrains edits horizontally/vertically.");
                break;
        }
    }

    private string DigitizerModeSuffix()
    {
        var modes = new List<string>();
        if (SnapEnabled)
            modes.Add("Snap F3");
        if (PdfSnapEnabled)
            modes.Add("PDF Snap Ctrl+F3");
        if (OrthoEnabled)
            modes.Add("Ortho F8");
        if (BoxModeEnabled)
            modes.Add("Box");
        return modes.Count == 0 ? "" : $" [{string.Join(", ", modes)}]";
    }

    private static string ToolTitle(string type) =>
        type switch
        {
            "point" => "Count",
            "line" => "Line",
            "area" => "Area",
            "areacut" => "Cut",
            "dimension" => "Ruler",
            "pitch" => "Pitch",
            "beam" => "Beam",
            "openings" => "Openings",
            "highlight" => "Highlighter",
            "arrow" => "Arrow",
            "rectangle" => "Box",
            "cloud" => "Cloud",
            "note" => "Note",
            "select" => "Select",
            _ => type,
        };

    private static string EntryTitle(string type) =>
        type == "point" ? "Count mark" : $"{ToolTitle(type)} section";

    private void CancelDrawing(bool clearSelection = true)
    {
        _drawPts.Clear();
        _scalePts.Clear();
        _rubberEnd = null;
        _boxSelecting = false;
        _boxVertexMode = false;
        _areaCutMeasurement = null;
        SetSnapPreview(null);
        ClearEdgeSnapPreview();
        if (_draggingVertex && IsMouseCaptured)
            ReleaseMouseCapture();
        if (clearSelection)
            ClearSelection();
        RequestRepaint();
    }

    // в•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђ
    // Helpers
    // в•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђ
}
