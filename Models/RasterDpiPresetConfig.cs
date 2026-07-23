using System;
using System.Collections.Generic;
using System.Linq;

namespace OurPlanCore;

public sealed class RasterDpiPresetConfig
{
    public List<int> Presets { get; set; } = [];

    public RasterDpiPresetConfig Clone() =>
        new()
        {
            Presets = Presets == null ? [] : [.. Presets],
        };

    public static RasterDpiPresetConfig BuildDefault() =>
        new()
        {
            Presets = [72, 100, 150, 200, 300, 400],
        };

    public static RasterDpiPresetConfig UpgradeForCurrentSchema(RasterDpiPresetConfig? config)
    {
        if (config == null)
            return BuildDefault();

        List<int> presets = (config.Presets ?? [])
            .Where(dpi => dpi is >= 72 and <= RasterSheetCacheService.MaxRasterDpi)
            .Distinct()
            .Take(12)
            .ToList();
        return presets.Count == 0
            ? BuildDefault()
            : new RasterDpiPresetConfig { Presets = presets };
    }
}

public static class RasterDpiPresetService
{
    private static RasterDpiPresetConfig _active = RasterDpiPresetConfig.BuildDefault();

    public static RasterDpiPresetConfig Active => _active.Clone();

    public static void Install(RasterDpiPresetConfig config) =>
        _active = RasterDpiPresetConfig.UpgradeForCurrentSchema(config);
}
