using System.Windows;
using System.Windows.Input;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    internal bool IsKeyboardRoofContext => _threeDRoofModeEnabled;

    /// <summary>Uses the same production editing paths as keys/buttons, including their Undo history.</summary>
    internal bool ExecuteCustomKeyboardCommand(string id)
    {
        if (id.StartsWith("roof.", StringComparison.Ordinal)) return ExecuteCustomRoofKeyboardCommand(id[5..]);
        if (id.StartsWith("overlay.", StringComparison.Ordinal)) return ExecuteCustomOverlayKeyboardCommand(id[8..]);
        if (id is not ("view.fit" or "view.zoomIn" or "view.zoomOut" or "edit.copyMeasurements" or "edit.pasteMeasurements" or
            "edit.undo" or "edit.selectAll" or "edit.delete" or "edit.rename" or "edit.mirrorHorizontal" or "edit.mirrorVertical" or
            "edit.rotateLeft" or "edit.rotateRight" or "edit.combineUnion" or "edit.combineSubtract" or "edit.combineIntersect" or
            "edit.combineRemoveOverlap" or "edit.combineDivide" or "tool.toggleSnap" or "tool.togglePdfSnap" or "tool.toggleOrtho" or
            "tool.toggleBox" or "drawing.complete" or "drawing.cycleTrace" or "drawing.advanceTrace" or "drawing.cancel"))
            return false;
        bool readOnlyAllowed = id is "view.fit" or "view.zoomIn" or "view.zoomOut" or
            "edit.copyMeasurements" or "edit.selectAll" or "drawing.cancel" or "tool.toggleSnap" or
            "tool.togglePdfSnap" or "tool.toggleOrtho" or "tool.toggleBox";
        if (IsReadOnlyMode && !readOnlyAllowed)
        {
            PostStatus("This project is read-only.");
            return true;
        }
        switch (id)
        {
            case "view.fit": ZoomFit(); break;
            case "view.zoomIn": ZoomIn(); break;
            case "view.zoomOut": ZoomOut(); break;
            case "edit.copyMeasurements":
                if (!CopySelectedPageAnnotations() && !CopyCurrentMeasurementAndCutRegionSelection())
                    PostStatus("Select measurements, cutouts, or markups before copying.");
                break;
            case "edit.pasteMeasurements":
                if (IsAnnotationClipboardCurrent) PasteCopiedPageAnnotations(_lastPointerPdf);
                else PasteCurrentMeasurementAndCutRegionClipboard(_lastPointerPdf);
                break;
            case "edit.undo": UndoLast(); break;
            case "edit.selectAll": SelectAllActivePageObjects(); break;
            case "edit.delete": DeleteSelectedOverlay(); break;
            case "edit.rename": RequestSelectedTakeoffRename(); break;
            case "edit.mirrorHorizontal": MirrorSelectedHorizontal(); break;
            case "edit.mirrorVertical": MirrorSelectedVertical(); break;
            case "edit.rotateLeft": RotateSelectedBy(-90); break;
            case "edit.rotateRight": RotateSelectedBy(90); break;
            case "edit.combineUnion": CombineSelectedAreas(AreaCombineMode.Union); break;
            case "edit.combineSubtract": CombineSelectedAreas(AreaCombineMode.Subtract); break;
            case "edit.combineIntersect": CombineSelectedAreas(AreaCombineMode.Intersect); break;
            case "edit.combineRemoveOverlap": CombineSelectedAreas(AreaCombineMode.RemoveOverlap); break;
            case "edit.combineDivide": CombineSelectedAreas(AreaCombineMode.Divide); break;
            case "tool.toggleSnap": SnapEnabled = !SnapEnabled; break;
            case "tool.togglePdfSnap": PdfSnapEnabled = !PdfSnapEnabled; break;
            case "tool.toggleOrtho": OrthoEnabled = !OrthoEnabled; break;
            case "tool.toggleBox": BoxModeEnabled = !BoxModeEnabled; break;
            case "drawing.complete": CompleteOrCancelDrawing(); break;
            case "drawing.cycleTrace":
                if (!TryCycleEdgeSnapPreview() && _pdfLayerTraceEnabled)
                {
                    if (_pdfLayerTraceChoosingLayer) CyclePdfLayerTraceCandidate();
                    else CyclePdfLayerTraceMode();
                }
                break;
            case "drawing.advanceTrace":
                if (_pdfLayerTraceEnabled) _ = AdvancePdfLayerTraceAsync(_lastPointerPdf);
                break;
            case "drawing.cancel":
                // Reuse the complete ordered cancellation path without changing physical modifier state.
                OnKeyDown(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), 0, Key.Escape)
                    { RoutedEvent = Keyboard.KeyDownEvent });
                break;
            default: return false;
        }
        return true;
    }
}
