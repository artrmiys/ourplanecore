using System.Windows;
using System.Windows.Input;

namespace OurPlanCore.Controls;

public sealed partial class PdfViewport
{
    internal bool HasKeyboardSheetOverlay => _sheetOverlayBitmap != null;

    private bool ExecuteCustomRoofKeyboardCommand(string action)
    {
        if (IsReadOnlyMode || !_threeDRoofModeEnabled) return true;
        if (action is "cancel" or "undo")
        {
            HandleThreeDRoofKey(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), 0,
                action == "cancel" ? Key.Escape : Key.Back) { RoutedEvent = Keyboard.KeyDownEvent });
        }
        else SetThreeDRoofGuideKind(action);
        return true;
    }

    private bool ExecuteCustomOverlayKeyboardCommand(string action)
    {
        if (IsReadOnlyMode || _sheetOverlayBitmap == null) return true;
        bool fine = action.StartsWith("fine", StringComparison.Ordinal);
        if (fine) action = char.ToLowerInvariant(action[4]) + action[5..];
        float nudge = fine ? 1 : 6, scaleFactor = fine ? 1.01f : 1.05f, rotation = fine ? .25f : 1;
        float x = _sheetOverlayOffsetXPt, y = _sheetOverlayOffsetYPt;
        float scale = _sheetOverlayScale, angle = _sheetOverlayRotationDegrees;
        switch (action)
        {
            case "left": x -= nudge; break;
            case "right": x += nudge; break;
            case "up": y -= nudge; break;
            case "down": y += nudge; break;
            case "scaleUp": scale *= scaleFactor; break;
            case "scaleDown": scale /= scaleFactor; break;
            case "rotateLeft": angle -= rotation; break;
            case "rotateRight": angle += rotation; break;
            case "reset": x = y = angle = 0; scale = 1; break;
            default: return false;
        }
        CancelPendingSheetOverlayTransformGesture(postStatus: false);
        ApplySheetOverlayTransform(x, y, scale, angle, BuildSheetOverlayTransformStatus("Overlay transformed", x, y, scale, angle));
        return true;
    }
}
