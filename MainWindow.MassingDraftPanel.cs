using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using OurPlaneCore.Controls;
using SkiaSharp;
using Path = System.IO.Path;

namespace OurPlaneCore;

public partial class MainWindow
{
    // 3D massing draft JSON/window actions and right-panel refresh.

    private void OpenMassingDraftJson()
    {
        if (StopLegacy3DMassingWorkflow("Open 3D JSON"))
            return;

        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before opening the 3D draft.";
            return;
        }

        OpenJsonFile(SmartMassingDraftService.ModelPath(_currentJob), "3D massing draft JSON is missing.");
    }

    private void OpenMassing3DWindow()
    {
        if (StopLegacy3DMassingWorkflow("Open 3D Window"))
            return;

        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before opening the 3D viewport.";
            return;
        }

        try
        {
            SmartMassingDraft? draft = _currentMassingDraft;
            string path = SmartMassingDraftService.ModelPath(_currentJob);
            if (draft == null && File.Exists(path))
                draft = SmartMassingDraftService.LoadDraft(_currentJob);

            IReadOnlyList<SmartAiMarker> markers = SmartContextStore.LoadAiMarkers(_currentJob);
            if (draft == null && markers.Count > 0)
                draft = SmartMassingDraftService.BuildDraftFromMarkers(_currentJob);
            if (draft != null)
                SmartMassingDraftService.RefreshDerivedGeometry(draft);

            var window = new Massing3DWindow(_currentJob, draft, markers)
            {
                Owner = this,
            };
            window.Show();

            int footprintPoints = draft?.Footprints.Sum(footprint => footprint.Points.Count) ?? 0;
            TxtStatus.Text = $"Opened 3D viewport window with {markers.Count} marker(s) and {footprintPoints} footprint point(s).";
        }
        catch (Exception ex)
        {
            ShowOperationError("Open 3D Viewport", ex);
        }
    }

    private void RefreshMassingDraftPanel(SmartMassingDraft? draft = null, string? path = null)
    {
        if (_massingDraftTextBox == null)
            return;

        if (_currentJob == null)
        {
            _currentMassingDraft = null;
            _massingDraftTextBox.Text = "Open a job, then build a 3D massing draft from reviewed AI markers.";
            if (_massingOpenDraftButton != null)
                _massingOpenDraftButton.IsEnabled = false;
            if (_massingReviewRoofButton != null)
                _massingReviewRoofButton.IsEnabled = false;
            if (_massingReviewOpeningsButton != null)
                _massingReviewOpeningsButton.IsEnabled = false;
            if (_massingAcceptDraftButton != null)
                _massingAcceptDraftButton.IsEnabled = false;
            DrawMassingPreview(null);
            RefreshMassing3DPreview(null);
            RefreshMassingMarkerRows(null);
            return;
        }

        path ??= SmartMassingDraftService.ModelPath(_currentJob);
        if (draft == null && File.Exists(path))
        {
            try
            {
                draft = SmartMassingDraftService.LoadDraft(_currentJob);
            }
            catch
            {
                draft = null;
            }
        }

        bool exists = File.Exists(path);
        if (_massingOpenDraftButton != null)
            _massingOpenDraftButton.IsEnabled = exists;
        if (_massingReviewRoofButton != null)
            _massingReviewRoofButton.IsEnabled = exists && draft != null;
        if (_massingReviewOpeningsButton != null)
            _massingReviewOpeningsButton.IsEnabled = exists && draft != null && draft.Openings.Count > 0;
        if (_massingAcceptDraftButton != null)
            _massingAcceptDraftButton.IsEnabled = exists && draft != null && draft.Footprints.Any(footprint => footprint.Points.Count >= 3);

        _currentMassingDraft = draft;
        if (draft != null)
            SmartMassingDraftService.RefreshDerivedGeometry(draft);
        _massingDraftTextBox.Text = draft == null
            ? $"No 3D massing draft exists yet.\n\nTarget path:\n{path}\n\nUse Build 3D Draft after placing exterior_corner markers."
            : BuildMassingDraftSummary(draft, path);
        DrawMassingPreview(draft);
        RefreshMassing3DPreview(draft);
        RefreshMassingMarkerRows(draft);
    }
}
