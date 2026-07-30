using System;
using System.Collections.Generic;
using System.Linq;

namespace OurPlanCore;

public static class ExcelFramingCategoryModes
{
    public const string Macro = "Macro";
    public const string Headers = "Headers";
    public const string Joists = "Joists";
    public const string Details = "Details";
    public const string Direct = "Direct";
}

public sealed class ExcelFramingExportConfig
{
    public bool IncludeInAll { get; set; } = true;
    public List<string> FramingFolderAliases { get; set; } = [];
    public string WorkbookName { get; set; } = "TemplateCom.xlsm";
    public string SheetName { get; set; } = "Detailed Frame List";
    public string SourceStartColumn { get; set; } = "J";
    public string SumMacroName { get; set; } = "C_SumTheSameValues";
    public string HeaderNoteText { get; set; } =
        "Note: The headers indicated on the plan";
    public string TargetHeaderColor { get; set; } = "#99CC00";
    public List<ExcelFramingFloorRule> Floors { get; set; } = [];
    public List<ExcelFramingCategoryConfig> Categories { get; set; } = [];

    public ExcelFramingExportConfig Clone() =>
        new()
        {
            IncludeInAll = IncludeInAll,
            FramingFolderAliases = [.. (FramingFolderAliases ?? [])],
            WorkbookName = WorkbookName,
            SheetName = SheetName,
            SourceStartColumn = SourceStartColumn,
            SumMacroName = SumMacroName,
            HeaderNoteText = HeaderNoteText,
            TargetHeaderColor = TargetHeaderColor,
            Floors = (Floors ?? [])
                .Where(rule => rule != null)
                .Select(rule => rule.Clone())
                .ToList(),
            Categories = (Categories ?? [])
                .Where(category => category != null)
                .Select(category => category.Clone())
                .ToList(),
        };

    public static ExcelFramingExportConfig Upgrade(
        ExcelFramingExportConfig? source,
        ExcelFramingExportConfig defaults,
        bool replaceWithDefaults)
    {
        if (replaceWithDefaults || source == null)
            return defaults.Clone();

        ExcelFramingExportConfig result = source.Clone();
        result.FramingFolderAliases ??= [];
        result.Floors ??= [];
        result.Categories ??= [];
        foreach (ExcelFramingFloorRule defaultFloor in defaults.Floors)
        {
            if (!result.Floors.Any(rule => rule.Order == defaultFloor.Order))
                result.Floors.Add(defaultFloor.Clone());
        }
        foreach (ExcelFramingCategoryConfig defaultCategory in defaults.Categories)
        {
            if (!result.Categories.Any(category =>
                    string.Equals(
                        category.Id,
                        defaultCategory.Id,
                        StringComparison.OrdinalIgnoreCase)))
            {
                result.Categories.Add(defaultCategory.Clone());
            }
        }

        if (result.FramingFolderAliases.Count == 0)
            result.FramingFolderAliases = [.. defaults.FramingFolderAliases];
        if (string.IsNullOrWhiteSpace(result.WorkbookName))
            result.WorkbookName = defaults.WorkbookName;
        if (string.IsNullOrWhiteSpace(result.SheetName))
            result.SheetName = defaults.SheetName;
        if (string.IsNullOrWhiteSpace(result.SourceStartColumn))
            result.SourceStartColumn = defaults.SourceStartColumn;
        if (string.IsNullOrWhiteSpace(result.SumMacroName))
            result.SumMacroName = defaults.SumMacroName;
        if (string.IsNullOrWhiteSpace(result.HeaderNoteText))
            result.HeaderNoteText = defaults.HeaderNoteText;
        if (string.IsNullOrWhiteSpace(result.TargetHeaderColor))
            result.TargetHeaderColor = defaults.TargetHeaderColor;
        return result;
    }

    public static ExcelFramingExportConfig BuildDefault() =>
        new()
        {
            FramingFolderAliases = ["framing"],
            Floors =
            [
                Floor(
                    1,
                    ["1st floor framing", "1st framing", "first floor framing"],
                    "1st Floor Framing List",
                    "Basement Floor Walls",
                    "1st Floor Walls"),
                Floor(
                    2,
                    ["2nd floor framing", "2nd framing", "second floor framing"],
                    "2nd Floor Framing List",
                    "1st Floor Walls",
                    "2nd Floor Walls"),
                Floor(
                    3,
                    ["3rd floor framing", "3rd framing", "third floor framing"],
                    "3rd Floor Framing List",
                    "2nd Floor Walls",
                    "3rd Floor Walls"),
                Floor(
                    4,
                    ["4th floor framing", "4th framing", "fourth floor framing"],
                    "4th Floor Framing List",
                    "3rd Floor Walls",
                    "4th Floor Walls"),
                Floor(
                    5,
                    ["5th floor framing", "5th framing", "fifth floor framing"],
                    "5th Floor Framing List",
                    "4th Floor Walls",
                    "5th Floor Walls"),
                new ExcelFramingFloorRule
                {
                    Order = 100,
                    Aliases = ["roof framing", "roof", "loft framing", "loft"],
                    FramingHeading = "Roof Frame list",
                    IsRoof = true,
                },
            ],
            Categories =
            [
                Category(
                    "posts",
                    "Posts",
                    ["posts", "post"],
                    "C_PostsSort",
                    useSum: true,
                    ExcelFramingCategoryModes.Macro,
                    10),
                Category(
                    "beams",
                    "Beams",
                    ["beams", "beam"],
                    "C_BeamsSort",
                    useSum: true,
                    ExcelFramingCategoryModes.Macro,
                    20),
                Category(
                    "headers",
                    "Headers",
                    ["headers", "header"],
                    "C_HeadersSort",
                    useSum: true,
                    ExcelFramingCategoryModes.Headers,
                    30),
                Category(
                    "joists",
                    "Joists",
                    ["joists", "joist"],
                    "C_JoistsSort",
                    useSum: false,
                    ExcelFramingCategoryModes.Joists,
                    40),
                Category(
                    "details",
                    "Details",
                    ["details", "detail"],
                    "",
                    useSum: true,
                    ExcelFramingCategoryModes.Details,
                    50),
                Category(
                    "stairs",
                    "Stairs",
                    ["stairs", "stair"],
                    "",
                    useSum: true,
                    ExcelFramingCategoryModes.Direct,
                    60),
            ],
        };

    private static ExcelFramingFloorRule Floor(
        int order,
        List<string> aliases,
        string framingHeading,
        string wallHeading,
        string sameFloorWallHeading) =>
        new()
        {
            Order = order,
            Aliases = aliases,
            FramingHeading = framingHeading,
            HeaderWallHeading = wallHeading,
            SameFloorWallHeading = sameFloorWallHeading,
        };

    private static ExcelFramingCategoryConfig Category(
        string id,
        string label,
        List<string> aliases,
        string macroName,
        bool useSum,
        string mode,
        int order) =>
        new()
        {
            Id = id,
            Label = label,
            FolderAliases = aliases,
            MacroName = macroName,
            UseSum = useSum,
            Mode = mode,
            Order = order,
        };
}

public sealed class ExcelFramingFloorRule
{
    public int Order { get; set; }
    public List<string> Aliases { get; set; } = [];
    public string FramingHeading { get; set; } = "";
    public string HeaderWallHeading { get; set; } = "";
    public string SameFloorWallHeading { get; set; } = "";
    public bool IsRoof { get; set; }

    public ExcelFramingFloorRule Clone() =>
        new()
        {
            Order = Order,
            Aliases = [.. (Aliases ?? [])],
            FramingHeading = FramingHeading,
            HeaderWallHeading = HeaderWallHeading,
            SameFloorWallHeading = SameFloorWallHeading,
            IsRoof = IsRoof,
        };
}

public sealed class ExcelFramingCategoryConfig
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public List<string> FolderAliases { get; set; } = [];
    public string MacroName { get; set; } = "";
    public bool UseSum { get; set; }
    public string Mode { get; set; } = ExcelFramingCategoryModes.Direct;
    public int Order { get; set; }

    public ExcelFramingCategoryConfig Clone() =>
        new()
        {
            Id = Id,
            Label = Label,
            FolderAliases = [.. (FolderAliases ?? [])],
            MacroName = MacroName,
            UseSum = UseSum,
            Mode = Mode,
            Order = Order,
        };
}
