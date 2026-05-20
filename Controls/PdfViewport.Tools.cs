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
using OurPlaneCore;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace OurPlaneCore.Controls;

public sealed partial class PdfViewport
{
    // в•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђ
    // Drawing tool logic
    // в•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђв•ђ

    private void HandleLeftClick(SKPoint pdf)
    {
        if (!EnsureScaleForLinearArea())
            return;

        switch (_tool)
        {
            case ViewerTool.Scale:
                HandleScaleClick(pdf);
                break;
            case ViewerTool.Ruler:
                AddTwoPointAnnotation(pdf, "dimension");
                break;
            case ViewerTool.DrawLine:
                AddTwoPointAnnotation(pdf, "line");
                break;
            case ViewerTool.DrawArrow:
                AddTwoPointAnnotation(pdf, "arrow");
                break;
            case ViewerTool.DrawRect:
                AddTwoPointAnnotation(pdf, "rectangle");
                break;
            case ViewerTool.DrawCloud:
                AddTwoPointAnnotation(pdf, "cloud");
                break;
            case ViewerTool.DrawArea:
                _drawPts.Add(pdf);
                RequestRepaint();
                PostRecordPrompt();
                break;
            case ViewerTool.Note:
                AddNoteAnnotation(pdf);
                break;
            case ViewerTool.Point:
                _drawPts.Add(pdf);
                FinalizeDrawing();
                break;
            case ViewerTool.Line when BoxModeEnabled:
            case ViewerTool.Area when BoxModeEnabled:
                AddBoxMeasurementPoint(pdf);
                break;
            case ViewerTool.Line:
            case ViewerTool.Area:
                _drawPts.Add(pdf);
                RequestRepaint();
                PostRecordPrompt();
                break;
            case ViewerTool.AreaCut:
                AddAreaCutPoint(pdf);
                break;
        }
    }

    private void AddBoxMeasurementPoint(SKPoint pdf)
    {
        _drawPts.Add(pdf);
        if (_drawPts.Count < 2)
        {
            RequestRepaint();
            PostRecordPrompt();
            return;
        }

        SKPoint first = _drawPts[0];
        _drawPts.Clear();
        _drawPts.AddRange(BoxMeasurementPoints(first, pdf, closeForLine: _tool == ViewerTool.Line));
        FinalizeDrawing();
    }

    private void AddAreaCutPoint(SKPoint pdf)
    {
        if (_drawPts.Count == 0)
        {
            if (!TryResolveAreaCutTarget(pdf, out Measurement target, out string status))
            {
                PostStatus(status);
                return;
            }

            _areaCutMeasurement = target;
            SelectMeasurement(target, -1);
            _drawPts.Add(pdf);
            RequestRepaint();
            PostRecordPrompt();
            return;
        }

        if (BoxModeEnabled)
        {
            ApplyAreaCutBox(_drawPts[0], pdf);
            return;
        }

        _drawPts.Add(pdf);
        RequestRepaint();
        PostRecordPrompt();
    }

}
