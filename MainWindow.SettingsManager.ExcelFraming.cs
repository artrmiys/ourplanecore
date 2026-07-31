using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OurPlanCore;

public partial class MainWindow
{
    private CheckBox? _excelFramingIncludeAll;
    private TextBox? _excelFramingRootAliases;
    private TextBox? _excelFramingWorkbook;
    private TextBox? _excelFramingSheet;
    private TextBox? _excelFramingSourceColumn;
    private TextBox? _excelFramingSumMacro;
    private TextBox? _excelFramingHeaderNote;
    private TextBox? _excelFramingHeaderColor;
    private TextBox? _excelFramingProtectedNoteColor;
    private TextBox? _excelFramingFloors;
    private TextBox? _excelFramingCategories;

    private static readonly Regex ExcelColorPattern = new(
        "^#[0-9A-Fa-f]{6}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private FrameworkElement BuildExcelFramingSettingsSection()
    {
        var section = new StackPanel();
        section.Children.Add(Header("Framing folders → Excel blocks"));
        section.Children.Add(new TextBlock
        {
            Text =
                "ALL reads framing/<floor>/{posts, beams, headers/ext|int, joists, details, stairs}. " +
                "Sum runs before Beams, Posts, Headers, and Stairs; Joists stay grouped by takeoff name. " +
                "Details remain as source rows in J:L without changing A:H.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = TryFindResource("SecondaryForegroundBrush") as Brush,
            Margin = new Thickness(0, 0, 0, 8),
            MaxWidth = 820,
            HorizontalAlignment = HorizontalAlignment.Left,
        });

        _excelFramingIncludeAll = new CheckBox
        {
            Content = "Include framing export in ALL",
            Margin = new Thickness(0, 0, 0, 7),
        };
        section.Children.Add(_excelFramingIncludeAll);

        var fields = new Grid
        {
            MaxWidth = 820,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        _excelFramingRootAliases =
            AddExcelField(fields, 0, 0, "Framing root aliases", 1);
        _excelFramingWorkbook =
            AddExcelField(fields, 0, 2, "Workbook", 3);
        _excelFramingSheet =
            AddExcelField(fields, 1, 0, "Worksheet", 1);
        _excelFramingSourceColumn =
            AddExcelField(fields, 1, 2, "Source start column", 3);
        _excelFramingSumMacro =
            AddExcelField(fields, 2, 0, "Duplicate Sum VBA", 1);
        _excelFramingHeaderColor =
            AddExcelField(fields, 2, 2, "Block color", 3);
        _excelFramingHeaderNote =
            AddExcelField(fields, 3, 0, "Header note text", 1);
        Grid.SetColumnSpan(_excelFramingHeaderNote, 3);
        _excelFramingProtectedNoteColor =
            AddExcelField(fields, 4, 0, "Protected note color", 1);
        section.Children.Add(fields);

        section.Children.Add(new TextBlock
        {
            Text =
                "Floor rules: order | roof | aliases | framing heading | shifted header wall | same-floor wall for roof headers",
            TextWrapping = TextWrapping.Wrap,
            Foreground = TryFindResource("SecondaryForegroundBrush") as Brush,
            Margin = new Thickness(0, 4, 0, 3),
        });
        _excelFramingFloors = BuildFramingRuleBox(132);
        section.Children.Add(_excelFramingFloors);

        section.Children.Add(new TextBlock
        {
            Text = "Categories: order | id | mode | sum | label | aliases | VBA macro",
            TextWrapping = TextWrapping.Wrap,
            Foreground = TryFindResource("SecondaryForegroundBrush") as Brush,
            Margin = new Thickness(0, 7, 0, 3),
        });
        _excelFramingCategories = BuildFramingRuleBox(145);
        _excelFramingCategories.ToolTip =
            "Modes: Macro, Headers, Joists, Details, Direct. Separate aliases with semicolons.";
        section.Children.Add(_excelFramingCategories);
        return section;
    }

    private static TextBox BuildFramingRuleBox(double height) =>
        new()
        {
            Height = height,
            MaxWidth = 820,
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
        };

    private void BindExcelFramingSettingsFields()
    {
        if (_excelFramingIncludeAll == null)
            return;
        ExcelFramingExportConfig framing = _excelMacroExportConfig.Framing;
        _excelFramingIncludeAll.IsChecked = framing.IncludeInAll;
        SetText(
            _excelFramingRootAliases,
            string.Join(", ", framing.FramingFolderAliases));
        SetText(_excelFramingWorkbook, framing.WorkbookName);
        SetText(_excelFramingSheet, framing.SheetName);
        SetText(_excelFramingSourceColumn, framing.SourceStartColumn);
        SetText(_excelFramingSumMacro, framing.SumMacroName);
        SetText(_excelFramingHeaderNote, framing.HeaderNoteText);
        SetText(_excelFramingHeaderColor, framing.TargetHeaderColor);
        SetText(_excelFramingProtectedNoteColor, framing.ProtectedNoteRowColor);
        SetText(
            _excelFramingFloors,
            string.Join(
                Environment.NewLine,
                framing.Floors
                    .OrderBy(rule => rule.Order)
                    .Select(FormatFramingFloorRule)));
        SetText(
            _excelFramingCategories,
            string.Join(
                Environment.NewLine,
                framing.Categories
                    .OrderBy(category => category.Order)
                    .Select(FormatFramingCategory)));
    }

    private bool TrySyncExcelFramingSettingsFromUi(out string error)
    {
        error = "";
        if (_excelFramingIncludeAll == null)
            return true;

        string workbook = TextOf(_excelFramingWorkbook);
        string sheet = TextOf(_excelFramingSheet);
        string sourceColumn = TextOf(_excelFramingSourceColumn).ToUpperInvariant();
        string sumMacro = TextOf(_excelFramingSumMacro);
        string headerNote = TextOf(_excelFramingHeaderNote);
        string color = TextOf(_excelFramingHeaderColor).ToUpperInvariant();
        string protectedNoteColor =
            TextOf(_excelFramingProtectedNoteColor).ToUpperInvariant();
        if (workbook.Length == 0 || sheet.Length == 0)
            error = "Framing workbook and worksheet are required.";
        else if (ExcelMacroTakeoffExportService.ColumnNumber(sourceColumn) <= 0)
            error = "Framing source start column is invalid.";
        else if (sumMacro.Length == 0)
            error = "Framing duplicate Sum VBA macro is required.";
        else if (headerNote.Length == 0)
            error = "Framing header note text is required.";
        else if (!ExcelColorPattern.IsMatch(color))
            error = "Framing block color must use #RRGGBB.";
        else if (!ExcelColorPattern.IsMatch(protectedNoteColor))
            error = "Framing protected note color must use #RRGGBB.";
        if (error.Length > 0)
            return false;

        if (!TryParseFramingFloors(
                _excelFramingFloors?.Text ?? "",
                out List<ExcelFramingFloorRule> floors,
                out error) ||
            !TryParseFramingCategories(
                _excelFramingCategories?.Text ?? "",
                out List<ExcelFramingCategoryConfig> categories,
                out error))
        {
            return false;
        }

        _excelMacroExportConfig.Framing = new ExcelFramingExportConfig
        {
            IncludeInAll = _excelFramingIncludeAll.IsChecked == true,
            FramingFolderAliases =
                SplitExcelAliases(TextOf(_excelFramingRootAliases)),
            WorkbookName = workbook,
            SheetName = sheet,
            SourceStartColumn = sourceColumn,
            SumMacroName = sumMacro,
            HeaderNoteText = headerNote,
            TargetHeaderColor = color,
            ProtectedNoteRowColor = protectedNoteColor,
            Floors = floors,
            Categories = categories,
        };
        return true;
    }

    private static string FormatFramingFloorRule(ExcelFramingFloorRule rule) =>
        string.Join(
            " | ",
            rule.Order.ToString(CultureInfo.InvariantCulture),
            rule.IsRoof ? "true" : "false",
            string.Join("; ", rule.Aliases),
            rule.FramingHeading,
            rule.HeaderWallHeading,
            rule.SameFloorWallHeading);

    private static string FormatFramingCategory(
        ExcelFramingCategoryConfig category) =>
        string.Join(
            " | ",
            category.Order.ToString(CultureInfo.InvariantCulture),
            category.Id,
            category.Mode,
            category.UseSum ? "true" : "false",
            category.Label,
            string.Join("; ", category.FolderAliases),
            category.MacroName);

    private static bool TryParseFramingFloors(
        string text,
        out List<ExcelFramingFloorRule> rules,
        out string error)
    {
        rules = [];
        error = "";
        int lineNumber = 0;
        foreach (string line in RuleLines(text))
        {
            lineNumber++;
            string[] fields = line.Split('|').Select(field => field.Trim()).ToArray();
            if (fields.Length != 6 ||
                !int.TryParse(
                    fields[0],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int order) ||
                !bool.TryParse(fields[1], out bool isRoof))
            {
                error = $"Invalid framing floor rule at line {lineNumber}.";
                return false;
            }
            List<string> aliases = SplitRuleAliases(fields[2]);
            if (aliases.Count == 0 || fields[3].Length == 0)
            {
                error = $"Framing floor line {lineNumber} needs aliases and a heading.";
                return false;
            }
            rules.Add(new ExcelFramingFloorRule
            {
                Order = order,
                IsRoof = isRoof,
                Aliases = aliases,
                FramingHeading = fields[3],
                HeaderWallHeading = fields[4],
                SameFloorWallHeading = fields[5],
            });
        }
        if (rules.Count == 0 || rules.Count(rule => rule.IsRoof) != 1)
        {
            error = "Framing floor rules must contain exactly one roof rule.";
            return false;
        }
        return true;
    }

    private static bool TryParseFramingCategories(
        string text,
        out List<ExcelFramingCategoryConfig> categories,
        out string error)
    {
        categories = [];
        error = "";
        HashSet<string> modes = new(StringComparer.OrdinalIgnoreCase)
        {
            ExcelFramingCategoryModes.Macro,
            ExcelFramingCategoryModes.Headers,
            ExcelFramingCategoryModes.Joists,
            ExcelFramingCategoryModes.Details,
            ExcelFramingCategoryModes.Direct,
        };
        int lineNumber = 0;
        foreach (string line in RuleLines(text))
        {
            lineNumber++;
            string[] fields = line.Split('|').Select(field => field.Trim()).ToArray();
            if (fields.Length != 7 ||
                !int.TryParse(
                    fields[0],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int order) ||
                !bool.TryParse(fields[3], out bool useSum) ||
                !modes.Contains(fields[2]))
            {
                error = $"Invalid framing category rule at line {lineNumber}.";
                return false;
            }
            List<string> aliases = SplitRuleAliases(fields[5]);
            bool macroMode = fields[2] is
                ExcelFramingCategoryModes.Macro or
                ExcelFramingCategoryModes.Headers or
                ExcelFramingCategoryModes.Joists;
            if (fields[1].Length == 0 ||
                fields[4].Length == 0 ||
                aliases.Count == 0 ||
                (macroMode && fields[6].Length == 0))
            {
                error = $"Framing category line {lineNumber} is incomplete.";
                return false;
            }
            categories.Add(new ExcelFramingCategoryConfig
            {
                Order = order,
                Id = fields[1],
                Mode = fields[2],
                UseSum = useSum,
                Label = fields[4],
                FolderAliases = aliases,
                MacroName = fields[6],
            });
        }
        if (categories.Count == 0 ||
            categories.Select(category => category.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != categories.Count)
        {
            error = "Framing category ids must be present and unique.";
            return false;
        }
        return true;
    }

    private static IEnumerable<string> RuleLines(string text) =>
        (text ?? "")
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n')
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static List<string> SplitRuleAliases(string text) =>
        (text ?? "")
        .Split(
            [';', ','],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}
