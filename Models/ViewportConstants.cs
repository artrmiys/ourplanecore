namespace OurPlaneCore;

public static class ViewportConstants
{
    public const float GeometryEpsilon = 1e-4f;
    public const float ZeroLengthEpsilon = 1e-3f;
    public const int AutosaveDebounceMs = 500;
    public const int ZoomRerenderDelayMs = 180;
    public const int NavigationIdleMs = 180;
    public const float SnapToleranceScreen = 14f;
    public const float VertexHitRadiusScreen = 10f;
    public const float MeasurementHitRadiusScreen = 8f;
    public const double PdfPointMeters = 25.4 / 72.0 / 1000.0;
}
