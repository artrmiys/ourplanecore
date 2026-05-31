using System;
using System.Windows.Media.Media3D;

namespace OurPlaneCore.Controls;

public sealed partial class Massing3DWindow
{
    private readonly record struct MarkerDraftPoint(double X, double Y, double Z);
    private readonly record struct MassingSceneFrame(double CenterX, double CenterY, double ModelSpan, double PdfCenterX, double PdfCenterY, double PdfScale);
    private sealed record Massing3DHitInfo(string Id, string Label, string SourceMarkerId);
    private sealed record Marker3DRow(string MarkerId, string Label, string Type, string Page, string ScenePoint);

    private sealed class MassingBounds
    {
        private double _minX = double.PositiveInfinity;
        private double _maxX = double.NegativeInfinity;
        private double _minY = double.PositiveInfinity;
        private double _maxY = double.NegativeInfinity;
        private double _minZ = double.PositiveInfinity;
        private double _maxZ = double.NegativeInfinity;

        public bool IsValid => !double.IsInfinity(_minX) && _maxX >= _minX && _maxY >= _minY && _maxZ >= _minZ;

        public Point3D Center => IsValid
            ? new((_minX + _maxX) / 2, (_minY + _maxY) / 2, (_minZ + _maxZ) / 2)
            : new Point3D(0, 2, 0);

        public double Radius
        {
            get
            {
                if (!IsValid)
                    return 8;

                double dx = _maxX - _minX;
                double dy = _maxY - _minY;
                double dz = _maxZ - _minZ;
                return Math.Max(4, Math.Sqrt(dx * dx + dy * dy + dz * dz) / 2);
            }
        }

        public void Include(Point3D point)
        {
            if (double.IsNaN(point.X) || double.IsNaN(point.Y) || double.IsNaN(point.Z))
                return;

            _minX = Math.Min(_minX, point.X);
            _maxX = Math.Max(_maxX, point.X);
            _minY = Math.Min(_minY, point.Y);
            _maxY = Math.Max(_maxY, point.Y);
            _minZ = Math.Min(_minZ, point.Z);
            _maxZ = Math.Max(_maxZ, point.Z);
        }
    }
}
