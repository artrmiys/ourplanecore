using System;

namespace OurPlaneCore;

public static class TransformEditConstraints
{
    public const double RotationSnapDegrees = 15.0;

    public static double SnapRotationDegrees(double degrees) =>
        Math.Round(degrees / RotationSnapDegrees, MidpointRounding.AwayFromZero) * RotationSnapDegrees;
}
