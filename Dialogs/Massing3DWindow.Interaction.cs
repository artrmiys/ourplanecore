using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace OurPlaneCore.Controls;

public sealed partial class Massing3DWindow
{
    private void MarkerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || _markerList.SelectedItem is not Marker3DRow row)
            return;

        _selectedMarkerId = row.MarkerId;
        RenderScene(preserveCamera: true);
        _statusText.Text = $"Selected marker: {row.Label} | page: {row.Page} | scene: {row.ScenePoint}.";
    }

    private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Point position = e.GetPosition(_viewport);
        _dragStart = position;
        _mouseDown = position;
        _mouseMoved = false;
        _viewport.CaptureMouse();
    }

    private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_mouseDown != null && !_mouseMoved)
            TrySelectSceneObject(e.GetPosition(_viewport));

        _dragStart = null;
        _mouseDown = null;
        _viewport.ReleaseMouseCapture();
    }

    private void Viewport_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart == null || e.LeftButton != MouseButtonState.Pressed)
            return;

        Point current = e.GetPosition(_viewport);
        Vector delta = current - _dragStart.Value;
        if (delta.Length > 2.5)
            _mouseMoved = true;
        _dragStart = current;
        _yaw += delta.X * 0.45;
        _pitch = Math.Clamp(_pitch - delta.Y * 0.35, -8, 88);
        UpdateCamera();
    }

    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        double factor = e.Delta > 0 ? 0.88 : 1.14;
        _distance = Math.Clamp(_distance * factor, 4, Math.Max(20, _sceneRadius * 18));
        UpdateCamera();
    }

    private void TrySelectSceneObject(Point point)
    {
        Massing3DHitInfo? selected = null;
        VisualTreeHelper.HitTest(
            _viewport,
            null,
            result =>
            {
                if (result is RayHitTestResult ray &&
                    ray.ModelHit is GeometryModel3D model &&
                    _hitInfo.TryGetValue(model, out Massing3DHitInfo? info))
                {
                    selected = info;
                    return HitTestResultBehavior.Stop;
                }

                return HitTestResultBehavior.Continue;
            },
            new PointHitTestParameters(point));

        if (selected == null)
            return;

        if (!string.IsNullOrWhiteSpace(selected.SourceMarkerId))
            _selectedMarkerId = selected.SourceMarkerId;

        RenderScene(preserveCamera: true);
        _statusText.Text = string.IsNullOrWhiteSpace(selected.SourceMarkerId)
            ? $"Selected: {selected.Label}."
            : $"Selected: {selected.Label} | source marker: {selected.SourceMarkerId}.";
    }

    private void FitView(bool resetAngles)
    {
        if (resetAngles)
        {
            _yaw = -38;
            _pitch = 28;
        }

        _distance = Math.Max(10, _sceneRadius * 2.75);
        UpdateCamera();
    }

    private void SetView(double yaw, double pitch)
    {
        _yaw = yaw;
        _pitch = Math.Clamp(pitch, -8, 88);
        FitView(resetAngles: false);
    }

    private void UpdateCamera()
    {
        double yaw = _yaw * Math.PI / 180.0;
        double pitch = _pitch * Math.PI / 180.0;
        double horizontal = _distance * Math.Cos(pitch);
        var position = new Point3D(
            _target.X + horizontal * Math.Sin(yaw),
            _target.Y + _distance * Math.Sin(pitch),
            _target.Z + horizontal * Math.Cos(yaw));
        _camera.Position = position;
        _camera.LookDirection = _target - position;
        _camera.UpDirection = new Vector3D(0, 1, 0);
    }
}
