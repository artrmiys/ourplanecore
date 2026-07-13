namespace OurPlaneCore;

public partial class MainWindow
{
    private void DeactivateThreeDForModuleDisable()
    {
        if (_threeDRoofEdgeSelectModeEnabled)
            SetThreeDRoofEdgeSelectMode(enabled: false);
        else
            _viewport.SetThreeDRoofEdgeSelectMode(enabled: false);

        _threeDRoofMoveModeEnabled = false;
        _threeDRafterFaceMode = false;
        if (IsThreeDRoofGizmoDragging)
            EndThreeDRoofGizmoDrag(save: false);

        _threeDViewerDragStart = null;
        _threeDViewerMouseDownPoint = null;
        _threeDViewerPanStart = null;
        _threeDViewerMouseMoved = false;
        _threeDSideViewerDragStart = null;
        _threeDSideViewerMouseDownPoint = null;
        _threeDSideViewerPanStart = null;
        _threeDSideViewerMouseMoved = false;
        ThreeDViewerViewportHost.ReleaseMouseCapture();
        _threeDSideViewportHost?.ReleaseMouseCapture();

        ClearThreeDRoofGuideSelection();
        HideThreeDRoofPitchPopup();
        _viewport.ClearThreeDRoofGuides();
        BuildCleanThreeDViewerScene();
        UpdateThreeDEditor();
    }

    private void ReactivateThreeDForModuleEnable()
    {
        if (!IsModuleEnabled(ModuleId.ThreeD))
            return;

        LoadThreeDModelForCurrentJob();
        RefreshThreeDRoofGuideOverlay();
        RefreshThreeDViewer();
    }
}
