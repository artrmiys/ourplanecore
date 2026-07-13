using System;
using System.Collections.Generic;
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
    public void SetSheetLegend(IEnumerable<SheetLegendEntry> entries)
    {
        _sheetLegendEntries.Clear();
        _sheetLegendEntries.AddRange(entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .Take(50));
        RequestRepaint();
    }

    public void ShowAiActionDraftPreview(SmartAiActionDraft draft, string pageName)
    {
        _aiActionDraftPreview = draft;
        _aiActionDraftPreviewPage = pageName;
        RequestRepaint();
    }

    public void ClearAiActionDraftPreview()
    {
        _aiActionDraftPreview = null;
        _aiActionDraftPreviewPage = "";
        RequestRepaint();
    }

    public void SetAiMarkers(IEnumerable<SmartAiMarker> markers)
    {
        _aiMarkers.Clear();
        _aiMarkers.AddRange(markers);
        RequestRepaint();
    }

    public void ClearAiMarkers()
    {
        _aiMarkers.Clear();
        RequestRepaint();
    }
}
