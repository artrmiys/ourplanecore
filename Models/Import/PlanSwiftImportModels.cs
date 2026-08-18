using System.Collections.Generic;

namespace OurPlanCore;

public static class PlanSwiftSourceFormats
{
    public const string PlanSwift = "PlanSwift";
    public const string OurPlanCore = "OurPlanCore";
    public const string LegacyOurPlanCore = "OurPlaneCore";

    public static bool IsOurPlanCore(string? value) =>
        string.Equals(value, OurPlanCore, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, LegacyOurPlanCore, StringComparison.OrdinalIgnoreCase);
}

public sealed class PlanSwiftImportOptions
{
    public const string DefaultCurrentJobImportFolderName = "01. planswift";

    public string SourceJobPath { get; init; } = "";
    public string DestinationParentPath { get; init; } = "";
    public string DestinationJobName { get; init; } = "";
    public string DestinationJobPath { get; init; } = "";
    public string ImportRootFolderName { get; init; } = DefaultCurrentJobImportFolderName;
    public bool ConvertPageImages { get; init; } = true;
    public bool ImportAllSheetsAndTakeoffFolders { get; init; }
    public int MaxPages { get; init; }
    public int MaxTakeoffItems { get; init; }
    public int MaxMeasurements { get; init; }
    public bool PortableReportPaths { get; init; }

    public bool ImportIntoExistingJob => !string.IsNullOrWhiteSpace(DestinationJobPath);
}

public sealed record PlanSwiftImportResult(
    string SourceJobPath,
    string DestinationJobPath,
    int PagesImported,
    int TakeoffItemsImported,
    int MeasurementsImported,
    int Warnings,
    IReadOnlyList<string> Messages,
    int TakeoffFoldersImported = 0);

public sealed class PlanSwiftProjectManifest
{
    public string SourceJobPath { get; init; } = "";
    public string SourceFormat { get; init; } = PlanSwiftSourceFormats.PlanSwift;
    public string JobName { get; init; } = "";
    public IReadOnlyList<PlanSwiftClassCount> TakeoffClassCounts { get; init; } = [];
    public IReadOnlyList<PlanSwiftPageRecord> Pages { get; init; } = [];
    public IReadOnlyList<PlanSwiftFolderRecord> TakeoffFolders { get; init; } = [];
    public IReadOnlyList<PlanSwiftTakeoffItemRecord> TakeoffItems { get; init; } = [];
    public IReadOnlyList<PlanSwiftSegmentRecord> Segments { get; init; } = [];
    public IReadOnlyList<PlanSwiftSourceRecord> EstimateItems { get; init; } = [];
    public IReadOnlyList<PlanSwiftSourceRecord> Notes { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record PlanSwiftClassCount(string ClassName, int Count);

public sealed record PlanSwiftFolderRecord(
    string SourceFolder,
    string RelativeFolder,
    string ParentRelativeFolder,
    string Name,
    int OrderIndex);

public sealed record PlanSwiftPageRecord(
    string SourceFolder,
    string RelativeFolder,
    string ParentRelativeFolder,
    string Name,
    string Guid,
    string ImagePath,
    double ScaleX,
    double ScaleY,
    string ScaleUnits,
    int OrderIndex);

public sealed class PlanSwiftTakeoffItemRecord
{
    public string SourceFolder { get; init; } = "";
    public string RelativeFolder { get; init; } = "";
    public string ParentRelativeFolder { get; init; } = "";
    public string Name { get; init; } = "";
    public string ClassName { get; init; } = "";
    public string MeasurementType { get; init; } = "line";
    public string ColorHex { get; init; } = "#FF4444";
    public int OrderIndex { get; init; }
    public IReadOnlyDictionary<string, string> Properties { get; init; } =
        new Dictionary<string, string>();
    public IReadOnlyList<PlanSwiftSectionRecord> Sections { get; init; } = [];
}

public sealed class PlanSwiftSegmentRecord
{
    public string SourceFolder { get; init; } = "";
    public string RelativeFolder { get; init; } = "";
    public string ParentRelativeFolder { get; init; } = "";
    public string SourceParentRelativeFolder { get; init; } = "";
    public string Name { get; init; } = "";
    public string ParentName { get; init; } = "";
    public string ClassName { get; init; } = "Segment";
    public string Guid { get; init; } = "";
    public string ColorHex { get; init; } = "#666666";
    public int OrderIndex { get; init; }
    public IReadOnlyDictionary<string, string> Properties { get; init; } =
        new Dictionary<string, string>();
    public IReadOnlyList<PlanSwiftSectionRecord> Sections { get; init; } = [];
}

public sealed class PlanSwiftSourceRecord
{
    public string SourceFolder { get; init; } = "";
    public string RelativeFolder { get; init; } = "";
    public string ParentRelativeFolder { get; init; } = "";
    public string Name { get; init; } = "";
    public string ClassName { get; init; } = "";
    public string Guid { get; init; } = "";
    public int OrderIndex { get; init; }
    public IReadOnlyDictionary<string, string> Properties { get; init; } =
        new Dictionary<string, string>();
}

public sealed record PlanSwiftSectionRecord(
    string SourceFolder,
    string Name,
    string Guid,
    string PageGuid,
    string MeasurementType,
    bool Visible,
    IReadOnlyList<PlanSwiftPoint> Points,
    IReadOnlyList<IReadOnlyList<PlanSwiftPoint>> Holes,
    string BoxMode,
    bool Closed,
    int OrderIndex)
{
    public IReadOnlyDictionary<string, string> Properties { get; init; } =
        new Dictionary<string, string>();
}

public sealed record PlanSwiftPoint(float X, float Y);

public sealed record PlanSwiftPageNormalization(
    int PixelWidth,
    int PixelHeight,
    double DpiX,
    double DpiY,
    double WidthPt,
    double HeightPt,
    double CoordinateScaleX,
    double CoordinateScaleY,
    string Source,
    string Message = "")
{
    public static PlanSwiftPageNormalization Default() =>
        new(
            1600,
            1000,
            72.0,
            72.0,
            1600,
            1000,
            1.0,
            1.0,
            "placeholder");

    public double WidthInches => WidthPt / 72.0;
    public double HeightInches => HeightPt / 72.0;
}
