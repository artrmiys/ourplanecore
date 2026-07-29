using System;
using System.Collections.Generic;
using System.Linq;

namespace OurPlanCore;

public sealed class ExcelMacroExportConfig
{
    public int SchemaVersion { get; set; } = 3;
    public List<ExcelMacroExportActionConfig> Actions { get; set; } = [];
    public List<ExcelMacroFloorRule> FloorRules { get; set; } = [];
    public List<string> BatchActionOrder { get; set; } = [];

    public ExcelMacroExportConfig Clone() =>
        new()
        {
            SchemaVersion = SchemaVersion,
            Actions = (Actions ?? []).Where(action => action != null).Select(action => action.Clone()).ToList(),
            FloorRules = (FloorRules ?? []).Where(rule => rule != null).Select(rule => rule.Clone()).ToList(),
            BatchActionOrder = [.. (BatchActionOrder ?? [])],
        };

    public ExcelMacroExportActionConfig Action(string id)
    {
        Actions ??= [];
        ExcelMacroExportActionConfig? action = Actions.FirstOrDefault(action =>
            string.Equals(action.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? BuildDefault().Actions.First(action =>
            string.Equals(action.Id, id, StringComparison.OrdinalIgnoreCase));
        return action;
    }

    public static ExcelMacroExportConfig UpgradeForCurrentSchema(
        ExcelMacroExportConfig? source)
    {
        ExcelMacroExportConfig defaults = BuildDefault();
        int sourceSchema = source?.SchemaVersion ?? 0;
        ExcelMacroExportConfig result = source?.Clone() ?? defaults.Clone();
        result.SchemaVersion = defaults.SchemaVersion;
        foreach (ExcelMacroExportActionConfig defaultAction in defaults.Actions)
        {
            if (!result.Actions.Any(action =>
                    string.Equals(action.Id, defaultAction.Id, StringComparison.OrdinalIgnoreCase)))
            {
                result.Actions.Add(defaultAction.Clone());
            }
        }
        foreach (ExcelMacroFloorRule defaultRule in defaults.FloorRules)
        {
            if (!result.FloorRules.Any(rule => rule.Floor == defaultRule.Floor))
                result.FloorRules.Add(defaultRule.Clone());
        }
        if (sourceSchema < 2)
        {
            ExcelMacroExportActionConfig openings =
                result.Action(ExcelMacroExportActionIds.Openings);
            if (string.IsNullOrWhiteSpace(openings.PerFloorPreprocessMacroName))
            {
                openings.PerFloorPreprocessMacroName =
                    defaults.Action(ExcelMacroExportActionIds.Openings)
                        .PerFloorPreprocessMacroName;
            }
        }
        if (sourceSchema < 3)
        {
            foreach (ExcelMacroExportActionConfig action in result.Actions)
            {
                ExcelMacroExportActionConfig? defaultAction =
                    defaults.Actions.FirstOrDefault(item =>
                        string.Equals(item.Id, action.Id, StringComparison.OrdinalIgnoreCase));
                action.RowOrderMode =
                    defaultAction?.RowOrderMode ?? ExcelMacroRowOrderModes.Source;
            }
            result.BatchActionOrder = [.. defaults.BatchActionOrder];
        }
        foreach (ExcelMacroExportActionConfig action in result.Actions)
        {
            if (!string.IsNullOrWhiteSpace(action.RowOrderMode))
                continue;
            action.RowOrderMode = defaults.Actions.FirstOrDefault(item =>
                    string.Equals(item.Id, action.Id, StringComparison.OrdinalIgnoreCase))
                ?.RowOrderMode ?? ExcelMacroRowOrderModes.Source;
        }
        if (result.BatchActionOrder.Count == 0)
            result.BatchActionOrder = [.. defaults.BatchActionOrder];
        return result;
    }

    public static ExcelMacroExportConfig BuildDefault() =>
        new()
        {
            Actions =
            [
                new ExcelMacroExportActionConfig
                {
                    Id = ExcelMacroExportActionIds.Sqft,
                    Label = "SQFT",
                    FolderAliases = ["sqft", "sqfts"],
                    WorkbookName = "TemplateCom.xlsm",
                    SheetName = "Detailed Frame List",
                    ScanStartColumn = "I",
                    ScanEndColumn = "N",
                    WriteStartColumn = "J",
                    StartRow = 10,
                    BlankRowsBetween = 1,
                    MacroName = "A2_SQFT_calc",
                },
                new ExcelMacroExportActionConfig
                {
                    Id = ExcelMacroExportActionIds.Walls,
                    Label = "Walls",
                    FolderAliases = ["walls"],
                    WorkbookName = "TemplateCom.xlsm",
                    SheetName = "Detailed Frame List",
                    ScanStartColumn = "I",
                    ScanEndColumn = "N",
                    WriteStartColumn = "J",
                    StartRow = 10,
                    BlankRowsBetween = 1,
                    MacroName = "A3_Walls_Calc_AllGroup",
                    UseFloorHeaders = true,
                    RowOrderMode = ExcelMacroRowOrderModes.WallsStrict,
                },
                new ExcelMacroExportActionConfig
                {
                    Id = ExcelMacroExportActionIds.Openings,
                    Label = "Openings",
                    FolderAliases = ["openings"],
                    WorkbookName = "TemplateCom.xlsm",
                    SheetName = "Detailed Frame List",
                    ScanStartColumn = "Z",
                    ScanEndColumn = "AB",
                    WriteStartColumn = "Z",
                    StartRow = 158,
                    BlankRowsBetween = 1,
                    MacroName = "A5_Openings",
                    UseFloorHeaders = true,
                    PerFloorPreprocessMacroName = "C_SumNearWindowValues",
                },
                new ExcelMacroExportActionConfig
                {
                    Id = ExcelMacroExportActionIds.Gables,
                    Label = "Gables",
                    FolderAliases = ["gables", "gable"],
                    WorkbookName = "TemplateCom.xlsm",
                    SheetName = "Detailed Frame List",
                    ScanStartColumn = "I",
                    ScanEndColumn = "N",
                    WriteStartColumn = "J",
                    StartRow = 10,
                    BlankRowsBetween = 1,
                    MacroName = "A2_SQFT_calc",
                },
                new ExcelMacroExportActionConfig
                {
                    Id = ExcelMacroExportActionIds.TrussHeel,
                    Label = "Truss Heel",
                    FolderAliases = ["trussheel", "truss heel", "truss heels"],
                    WorkbookName = "TemplateCom.xlsm",
                    SheetName = "Detailed Frame List",
                    ScanStartColumn = "I",
                    ScanEndColumn = "N",
                    WriteStartColumn = "J",
                    StartRow = 10,
                    BlankRowsBetween = 1,
                    MacroName = "A2_SQFT_calc",
                },
                new ExcelMacroExportActionConfig
                {
                    Id = ExcelMacroExportActionIds.Parapet,
                    Label = "Parapet",
                    FolderAliases = ["parapets", "parapet"],
                    WorkbookName = "TemplateCom.xlsm",
                    SheetName = "Detailed Frame List",
                    ScanStartColumn = "I",
                    ScanEndColumn = "N",
                    WriteStartColumn = "J",
                    StartRow = 10,
                    BlankRowsBetween = 1,
                    MacroName = "A4_Parapet",
                },
                new ExcelMacroExportActionConfig
                {
                    Id = ExcelMacroExportActionIds.EveRakes,
                    Label = "Eve / Rakes",
                    FolderAliases = ["eves rakes", "eve rakes", "eaves rakes"],
                    WorkbookName = "TemplateCom.xlsm",
                    SheetName = "Detailed Frame List",
                    ScanStartColumn = "I",
                    ScanEndColumn = "N",
                    WriteStartColumn = "J",
                    StartRow = 10,
                    BlankRowsBetween = 1,
                    MacroName = "A6_Eve_Rakes",
                    RowOrderMode = ExcelMacroRowOrderModes.EvesThenRakesByValue,
                },
            ],
            FloorRules =
            [
                new ExcelMacroFloorRule { Floor = 0, Aliases = ["0", "basement", "bsmt"] },
                new ExcelMacroFloorRule { Floor = 1, Aliases = ["1", "1st", "first"] },
                new ExcelMacroFloorRule { Floor = 2, Aliases = ["2", "2nd", "second"] },
                new ExcelMacroFloorRule { Floor = 3, Aliases = ["3", "3rd", "third"] },
                new ExcelMacroFloorRule { Floor = 4, Aliases = ["4", "4th", "fourth"] },
                new ExcelMacroFloorRule { Floor = 5, Aliases = ["5", "5th", "fifth"] },
            ],
            BatchActionOrder =
            [
                ExcelMacroExportActionIds.Sqft,
                ExcelMacroExportActionIds.Walls,
                ExcelMacroExportActionIds.Gables,
                ExcelMacroExportActionIds.TrussHeel,
                ExcelMacroExportActionIds.Parapet,
                ExcelMacroExportActionIds.EveRakes,
                ExcelMacroExportActionIds.Openings,
            ],
        };
}

public static class ExcelMacroExportActionIds
{
    public const string Sqft = "sqft";
    public const string Walls = "walls";
    public const string Openings = "openings";
    public const string Gables = "gables";
    public const string TrussHeel = "truss_heel";
    public const string Parapet = "parapet";
    public const string EveRakes = "eve_rakes";
}

public static class ExcelMacroRowOrderModes
{
    public const string Source = "Source";
    public const string WallsStrict = "WallsStrict";
    public const string EvesThenRakesByValue = "EvesThenRakesByValue";
}

public sealed class ExcelMacroExportActionConfig
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public List<string> FolderAliases { get; set; } = [];
    public string WorkbookName { get; set; } = "TemplateCom.xlsm";
    public string SheetName { get; set; } = "Detailed Frame List";
    public string ScanStartColumn { get; set; } = "I";
    public string ScanEndColumn { get; set; } = "N";
    public string WriteStartColumn { get; set; } = "J";
    public int StartRow { get; set; } = 10;
    public int BlankRowsBetween { get; set; } = 1;
    public string MacroName { get; set; } = "";
    public bool UseFloorHeaders { get; set; }
    public string UnitSystem { get; set; } = "Imperial";
    public string PerFloorPreprocessMacroName { get; set; } = "";
    public string RowOrderMode { get; set; } = ExcelMacroRowOrderModes.Source;

    public ExcelMacroExportActionConfig Clone() =>
        new()
        {
            Id = Id,
            Label = Label,
            FolderAliases = [.. (FolderAliases ?? [])],
            WorkbookName = WorkbookName,
            SheetName = SheetName,
            ScanStartColumn = ScanStartColumn,
            ScanEndColumn = ScanEndColumn,
            WriteStartColumn = WriteStartColumn,
            StartRow = StartRow,
            BlankRowsBetween = BlankRowsBetween,
            MacroName = MacroName,
            UseFloorHeaders = UseFloorHeaders,
            UnitSystem = UnitSystem,
            PerFloorPreprocessMacroName = PerFloorPreprocessMacroName,
            RowOrderMode = RowOrderMode,
        };
}

public sealed class ExcelMacroFloorRule
{
    public int Floor { get; set; }
    public List<string> Aliases { get; set; } = [];

    public ExcelMacroFloorRule Clone() =>
        new() { Floor = Floor, Aliases = [.. (Aliases ?? [])] };
}

public static class ExcelMacroExportConfigProvider
{
    public static ExcelMacroExportConfig Current { get; private set; } =
        ExcelMacroExportConfig.BuildDefault();

    public static void Install(ExcelMacroExportConfig config) =>
        Current = config.Clone();
}
