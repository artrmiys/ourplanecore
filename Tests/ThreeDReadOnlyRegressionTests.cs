internal static class ThreeDReadOnlyRegressionTests
{
    public static void EditingIsBlockedBeforeMutationWhileViewerStaysAvailable()
    {
        string access = Read("MainWindow.ThreeDReadOnly.cs");
        string walls = Read("MainWindow.ThreeDWalls.cs");
        string groups = Read("MainWindow.ThreeDRoofGroups.cs");
        string gizmo = Read("MainWindow.ThreeDRoofGizmo.cs");
        string massing = Read("MainWindow.MassingWorkflowCommands.cs");
        string viewer = Read("MainWindow.ThreeDViewer.cs");

        int apply = access.IndexOf("private void ApplyThreeDReadOnlyState(bool readOnly)", StringComparison.Ordinal);
        int cancel = access.IndexOf("CancelThreeDMutableInteractionForReadOnly();", apply, StringComparison.Ordinal);
        int state = access.IndexOf("_threeDReadOnlyApplied = readOnly;", cancel, StringComparison.Ordinal);
        AssertTrue(apply >= 0 && cancel > apply && state > cancel,
            "read-only transition must cancel mutable 3D interaction before applying disabled state");
        AssertTrue(
            access.Contains("RestoreThreeDRoofPlacementDragSnapshot();", StringComparison.Ordinal) &&
            access.Contains("ThreeDToolbarMutationLabels", StringComparison.Ordinal) &&
            access.Contains("CmbRafterSpacing.IsEnabled = !readOnly;", StringComparison.Ordinal),
            "3D read-only state must restore an active placement drag and disable central edit controls");

        int build = walls.IndexOf("private void Build3DWallsFromTakeoffSelection", StringComparison.Ordinal);
        int buildGuard = walls.IndexOf("EnsureThreeDEditable(\"build 3D walls\")", build, StringComparison.Ordinal);
        int buildMutation = walls.IndexOf("_threeDWallElements.Clear();", build, StringComparison.Ordinal);
        AssertTrue(buildGuard > build && buildMutation > buildGuard,
            "3D wall build must check write ownership before changing the in-memory model");

        int slider = groups.IndexOf("private void ThreeDRoofMoveSlider_ValueChanged", StringComparison.Ordinal);
        int sliderGuard = groups.IndexOf("if (!ThreeDEditingAllowed)", slider, StringComparison.Ordinal);
        int sliderMutation = groups.IndexOf("placement.OffsetXFeet =", slider, StringComparison.Ordinal);
        AssertTrue(sliderGuard > slider && sliderMutation > sliderGuard,
            "roof sliders must check write ownership before changing placement offsets");

        int beginGizmo = gizmo.IndexOf("private bool TryBeginThreeDRoofGizmoDrag", StringComparison.Ordinal);
        int gizmoGuard = gizmo.IndexOf("placement == null || !ThreeDEditingAllowed", beginGizmo, StringComparison.Ordinal);
        int gizmoSnapshot = gizmo.IndexOf("BeginThreeDRoofPlacementDragSnapshot();", gizmoGuard, StringComparison.Ordinal);
        AssertTrue(gizmoGuard > beginGizmo && gizmoSnapshot > gizmoGuard,
            "roof gizmo must reject read-only drag before capturing or changing placement state");

        int accept = massing.IndexOf("private void AcceptMassingDraft()", StringComparison.Ordinal);
        int acceptGuard = massing.IndexOf("EnsureThreeDEditable(\"accept the 3D massing draft\")", accept, StringComparison.Ordinal);
        int acceptMutation = massing.IndexOf("draft.Status = \"reviewed\";", accept, StringComparison.Ordinal);
        AssertTrue(acceptGuard > accept && acceptMutation > acceptGuard,
            "massing acceptance must check write ownership before changing the draft");

        int fit = viewer.IndexOf("private void Btn3dViewerFit_Click", StringComparison.Ordinal);
        int iso = viewer.IndexOf("private void Btn3dViewerIso_Click", fit, StringComparison.Ordinal);
        string fitMethod = viewer[fit..iso];
        AssertTrue(!fitMethod.Contains("EnsureThreeDEditable", StringComparison.Ordinal),
            "camera fit/navigation must remain available in read-only mode");
    }

    private static string Read(string relativePath) => File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

    private static string RepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ourplancore.csproj")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("OurPlanCore repository root was not found.");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
