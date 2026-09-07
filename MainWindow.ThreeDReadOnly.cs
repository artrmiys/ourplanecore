using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OurPlanCore;

public partial class MainWindow
{
    private static readonly HashSet<string> ThreeDToolbarMutationLabels = new(StringComparer.Ordinal)
    {
        "Auto",
        "Wall",
        "Roof Base",
        "Select Edge",
        "Generate Roof",
        "Pick Faces",
        "Whole Roof",
        "Clear",
    };

    private readonly List<UIElement> _threeDMutationControls = [];
    private readonly Dictionary<UIElement, bool> _threeDMutationControlStates = [];
    private bool _threeDReadOnlyApplied;

    private bool ThreeDEditingAllowed => !_threeDReadOnlyApplied && !IsCurrentJobReadOnly;

    private bool EnsureThreeDEditable(string operation) =>
        EnsureCurrentJobWritable(operation);

    private T RegisterThreeDMutationControl<T>(T control) where T : UIElement
    {
        if (!_threeDMutationControls.Contains(control))
            _threeDMutationControls.Add(control);

        if (_threeDReadOnlyApplied)
        {
            _threeDMutationControlStates.TryAdd(control, control.IsEnabled);
            control.IsEnabled = false;
        }

        return control;
    }

    // Called by the job-access owner whenever a job becomes writable/read-only.
    // Viewer camera, selection, marker inspection, and JSON opening stay enabled.
    private void ApplyThreeDReadOnlyState(bool readOnly)
    {
        if (readOnly)
            CancelThreeDMutableInteractionForReadOnly();

        _threeDReadOnlyApplied = readOnly;
        foreach (UIElement control in _threeDMutationControls)
        {
            if (readOnly)
            {
                _threeDMutationControlStates.TryAdd(control, control.IsEnabled);
                control.IsEnabled = false;
            }
            else if (_threeDMutationControlStates.Remove(control, out bool wasEnabled))
            {
                control.IsEnabled = wasEnabled;
            }
        }

        foreach (Button button in FindVisualChildren<Button>(ThreeDManagerWorkspaceTab))
        {
            if (button.Content is string label && ThreeDToolbarMutationLabels.Contains(label))
                button.IsEnabled = !readOnly;
        }

        CmbRafterSpacing.IsEnabled = !readOnly;
        CmbRafterSize.IsEnabled = !readOnly;
        if (!readOnly)
        {
            UpdateThreeDEditor();
            UpdateThreeDRoofMoveControls();
            UpdateMassingReadOnlyButtons();
        }
    }

    private void UpdateMassingReadOnlyButtons()
    {
        bool canEdit = ThreeDEditingAllowed;
        if (_massingReviewRoofButton != null)
            _massingReviewRoofButton.IsEnabled = canEdit && _currentMassingDraft != null;
        if (_massingReviewOpeningsButton != null)
            _massingReviewOpeningsButton.IsEnabled = canEdit && _currentMassingDraft?.Openings.Count > 0;
        if (_massingAcceptDraftButton != null)
        {
            _massingAcceptDraftButton.IsEnabled = canEdit &&
                _currentMassingDraft?.Footprints.Any(footprint => footprint.Points.Count >= 3) == true;
        }
    }

    private void CancelThreeDMutableInteractionForReadOnly()
    {
        RestoreThreeDRoofPlacementDragSnapshot();
        _threeDRoofMoveModeEnabled = false;
        _threeDRafterFaceMode = false;
        if (IsThreeDRoofGizmoDragging)
            EndThreeDRoofGizmoDrag(save: false);

        _threeDViewerDragStart = null;
        _threeDViewerMouseDownPoint = null;
        _threeDViewerMouseMoved = false;
        _threeDSideViewerDragStart = null;
        _threeDSideViewerMouseDownPoint = null;
        _threeDSideViewerMouseMoved = false;
        ThreeDViewerViewportHost.ReleaseMouseCapture();
        _threeDSideViewportHost?.ReleaseMouseCapture();
        if (_threeDRoofEdgeSelectModeEnabled)
            SetThreeDRoofEdgeSelectMode(enabled: false);
        HideThreeDRoofPitchPopup();
        UpdateThreeDEditor();
    }
}
