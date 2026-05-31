using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace OurPlaneCore;

public partial class MainWindow
{
    // 3D wall and roof color/material helpers.

    private static Color ParseWallColor(string hex)
    {
        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color color)
                return color;
        }
        catch
        {
        }

        return Color.FromRgb(120, 144, 156);
    }

    private static Material CreateRoofFaceMaterial(SolidColorBrush diffuseBrush)
    {
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(diffuseBrush));
        // A faint emissive evens the surface out so the triangulation diagonals
        // don't read as stripes across a slope. (A specular sheen was tried here
        // instead, but on a flat-shaded mesh it lit every triangle border as a
        // visible band, so the emissive flatten stays.)
        material.Children.Add(new EmissiveMaterial(
            new SolidColorBrush(diffuseBrush.Color) { Opacity = 0.18 }));
        return material;
    }

    // Roof faces render in their source takeoff's color so editing the takeoff
    // color shows up on the model. The roof group's generated slab records the
    // source takeoff folder; we resolve the *live* takeoff color at render time
    // (not the snapshot copied onto the slab) so a color edit is reflected
    // without regenerating the roof. Falls back to the slab snapshot, then null
    // (caller keeps the plane's own color).
    private string? ResolveRoofGroupTakeoffColor(string roofGroupId)
    {
        if (string.IsNullOrWhiteSpace(roofGroupId))
            return null;

        ThreeDFloorSlab? slab = _threeDFloorSlabs.FirstOrDefault(s =>
            IsRoofSlab(s) && SameRoofGroup(s.RoofGroupId, roofGroupId));
        if (slab == null)
            return null;

        // Slab.TakeoffFolder can combine several source folders with '|'.
        string folder = (slab.TakeoffFolder ?? "")
            .Split('|')
            .FirstOrDefault(f => !string.IsNullOrWhiteSpace(f)) ?? "";
        if (!string.IsNullOrWhiteSpace(folder))
        {
            TakeoffItem? item = _takeoffItems.FirstOrDefault(t =>
                string.Equals(t.FolderPath, folder, StringComparison.OrdinalIgnoreCase));
            if (item != null && !string.IsNullOrWhiteSpace(item.Color))
                return item.Color;
        }

        return string.IsNullOrWhiteSpace(slab.Color) ? null : slab.Color;
    }

    private static Color ToVisibleRoofColor(Color color, bool selected)
    {
        Color boosted = Color.FromRgb(
            BoostRoofChannel(color.R),
            BoostRoofChannel(color.G),
            BoostRoofChannel(color.B));
        if (!selected)
            return boosted;

        return Color.FromRgb(
            MixRoofChannel(boosted.R, 245, 0.22),
            MixRoofChannel(boosted.G, 158, 0.22),
            MixRoofChannel(boosted.B, 11, 0.22));
    }

    private static byte BoostRoofChannel(byte channel) =>
        (byte)Math.Clamp(channel * 1.16 + 20, 0, 255);

    private static byte MixRoofChannel(byte value, byte accent, double accentShare) =>
        (byte)Math.Clamp(value * (1 - accentShare) + accent * accentShare, 0, 255);

    // Revit-style clean shaded look: keep most of the takeoff hue but soften
    // it toward a light neutral so it reads as a clean matte surface with
    // clear color. Selected colors are left vivid and bypass this.
    private static Color ToCleanMeshTint(Color color)
    {
        const double keep = 0.72; // share of the original hue retained
        static byte Mix(byte neutral, byte channel) =>
            (byte)Math.Clamp(neutral * (1 - keep) + channel * keep, 0, 255);
        return Color.FromRgb(Mix(206, color.R), Mix(210, color.G), Mix(216, color.B));
    }
}
