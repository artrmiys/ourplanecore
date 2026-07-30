using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OurPlanCore;

public partial class MainWindow
{
    private ComboBox? _excelSettingsAction;
    private TextBox? _excelSettingsWorkbook;
    private TextBox? _excelSettingsSheet;
    private TextBox? _excelSettingsAliases;
    private TextBox? _excelSettingsScanStart;
    private TextBox? _excelSettingsScanEnd;
    private TextBox? _excelSettingsWriteStart;
    private TextBox? _excelSettingsStartRow;
    private TextBox? _excelSettingsBlankRows;
    private TextBox? _excelSettingsMacro;
    private TextBox? _excelSettingsPreprocessMacro;
    private TextBox? _excelSettingsAfterMacro;
    private TextBox? _excelSettingsAfterRange;
    private TextBox? _excelSettingsProtectedLabels;
    private TextBox? _excelSettingsBatchOrder;
    private ComboBox? _excelSettingsUnits;
    private ComboBox? _excelSettingsRowOrder;
    private CheckBox? _excelSettingsFloorHeaders;
    private readonly Dictionary<int, TextBox> _excelSettingsFloorAliases = [];
    private TextBlock? _excelSettingsStatus;
    private string _excelSettingsActionId = ExcelMacroExportActionIds.Sqft;
    private bool _excelSettingsSyncing;
    private static readonly KeyValuePair<string, string>[] ExcelRowOrderOptions =
    [
        new(ExcelMacroRowOrderModes.Source, "Keep tree order"),
        new(ExcelMacroRowOrderModes.WallsStrict, "Walls: corners, LF, groups"),
        new(ExcelMacroRowOrderModes.EvesThenRakesByValue, "Eve LF, then Rake LF"),
    ];

    private FrameworkElement BuildExcelActionsPanel()
    {
        var content = new StackPanel();
        content.Children.Add(Header("Excel macro actions"));
        content.Children.Add(new TextBlock
        {
            Text =
                "These buttons append selected Takeoffs rows to the open TemplateCom workbook, " +
                "select the two macro input columns, and immediately run the configured VBA macro. " +
                "The scanner reads values/formulas only, so formatted blank rows do not move the insertion point.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = TryFindResource("SecondaryForegroundBrush") as Brush,
            Margin = new Thickness(0, 0, 0, 10),
            MaxWidth = 820,
            HorizontalAlignment = HorizontalAlignment.Left,
        });

        var actionBar = HBar();
        actionBar.Children.Add(new TextBlock
        {
            Text = "Action",
            Width = 130,
            VerticalAlignment = VerticalAlignment.Center,
        });
        _excelSettingsAction = new ComboBox
        {
            Width = 180,
            DisplayMemberPath = nameof(ExcelMacroExportActionConfig.Label),
            SelectedValuePath = nameof(ExcelMacroExportActionConfig.Id),
        };
        _excelSettingsAction.SelectionChanged += ExcelSettingsAction_SelectionChanged;
        actionBar.Children.Add(_excelSettingsAction);
        content.Children.Add(actionBar);

        var batchBar = HBar();
        batchBar.Children.Add(new TextBlock
        {
            Text = "ALL sequence",
            Width = 130,
            VerticalAlignment = VerticalAlignment.Center,
        });
        _excelSettingsBatchOrder = new TextBox
        {
            Width = 620,
            ToolTip =
                "Comma-separated action ids. Default: sqft, walls, gables, truss_heel, parapet, eve_rakes, openings",
        };
        batchBar.Children.Add(_excelSettingsBatchOrder);
        content.Children.Add(batchBar);

        var fields = new Grid { MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left };
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });

        _excelSettingsWorkbook = AddExcelField(fields, 0, 0, "Workbook", 1);
        _excelSettingsSheet = AddExcelField(fields, 0, 2, "Worksheet", 3);
        _excelSettingsAliases = AddExcelField(fields, 1, 0, "Takeoffs folder aliases", 1);
        _excelSettingsMacro = AddExcelField(fields, 1, 2, "VBA macro", 3);
        _excelSettingsScanStart = AddExcelField(fields, 2, 0, "Scan start column", 1);
        _excelSettingsScanEnd = AddExcelField(fields, 2, 2, "Scan end column", 3);
        _excelSettingsWriteStart = AddExcelField(fields, 3, 0, "Write start column", 1);
        _excelSettingsStartRow = AddExcelField(fields, 3, 2, "First scan row", 3);
        _excelSettingsBlankRows = AddExcelField(fields, 4, 0, "Blank rows between blocks", 1);

        AddExcelLabel(fields, 4, 2, "Output units");
        _excelSettingsUnits = new ComboBox
        {
            Margin = new Thickness(0, 0, 12, 6),
            ItemsSource = new[] { "Imperial", "Metric" },
        };
        Grid.SetRow(_excelSettingsUnits, 4);
        Grid.SetColumn(_excelSettingsUnits, 3);
        fields.Children.Add(_excelSettingsUnits);

        _excelSettingsPreprocessMacro =
            AddExcelField(fields, 5, 0, "Per-floor VBA first", 1);
        _excelSettingsPreprocessMacro.ToolTip =
            "Optional. Runs once for each floor's three-column item range before the main VBA macro.";
        AddExcelLabel(fields, 5, 2, "Row order");
        _excelSettingsRowOrder = new ComboBox
        {
            Margin = new Thickness(0, 0, 12, 6),
            ItemsSource = ExcelRowOrderOptions,
            DisplayMemberPath = "Value",
            SelectedValuePath = "Key",
            ToolTip =
                "Source keeps tree order. WallsStrict and EvesThenRakesByValue apply the configured export ordering.",
        };
        Grid.SetRow(_excelSettingsRowOrder, 5);
        Grid.SetColumn(_excelSettingsRowOrder, 3);
        fields.Children.Add(_excelSettingsRowOrder);
        _excelSettingsAfterMacro =
            AddExcelField(fields, 6, 0, "After VBA", 1);
        _excelSettingsAfterMacro.ToolTip =
            "Optional macro run after the main action, for example B_DeleteZeroRowsOnlyIn_AtoH.";
        _excelSettingsAfterRange =
            AddExcelField(fields, 6, 2, "After range", 3);
        _excelSettingsAfterRange.ToolTip =
            "The output range selected for the after-macro, for example A25:H1367.";

        AddExcelLabel(fields, 7, 0, "Always keep rows");
        _excelSettingsProtectedLabels = new TextBox
        {
            Margin = new Thickness(0, 0, 12, 6),
            Height = 74,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.NoWrap,
            ToolTip =
                "One exact output label per line. Every occurrence is protected before the after-macro runs.",
        };
        Grid.SetRow(_excelSettingsProtectedLabels, 7);
        Grid.SetColumn(_excelSettingsProtectedLabels, 1);
        Grid.SetColumnSpan(_excelSettingsProtectedLabels, 3);
        fields.Children.Add(_excelSettingsProtectedLabels);

        while (fields.RowDefinitions.Count <= 8)
            fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _excelSettingsFloorHeaders = new CheckBox
        {
            Content = "Insert numeric floor group rows (0-5) before the takeoff rows",
            Margin = new Thickness(150, 2, 0, 8),
        };
        Grid.SetRow(_excelSettingsFloorHeaders, 8);
        Grid.SetColumnSpan(_excelSettingsFloorHeaders, 4);
        fields.Children.Add(_excelSettingsFloorHeaders);
        content.Children.Add(fields);

        content.Children.Add(Header("Floor folder matching"));
        var floorGrid = new Grid
        {
            MaxWidth = 820,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 8),
        };
        floorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        floorGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(680) });
        for (int floor = 0; floor <= 5; floor++)
        {
            floorGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock
            {
                Text = $"Floor {floor}",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 5),
            };
            Grid.SetRow(label, floor);
            floorGrid.Children.Add(label);
            var box = new TextBox
            {
                Margin = new Thickness(0, 0, 0, 5),
                ToolTip = "Comma-separated folder words, for example: 1, 1st, first",
            };
            Grid.SetRow(box, floor);
            Grid.SetColumn(box, 1);
            floorGrid.Children.Add(box);
            _excelSettingsFloorAliases[floor] = box;
        }
        content.Children.Add(floorGrid);
        content.Children.Add(BuildExcelFramingSettingsSection());

        var buttons = HBar();
        buttons.Children.Add(MgrButton("Reset built-in", (_, _) =>
        {
            _excelMacroExportConfig = ExcelMacroExportConfig.BuildDefault();
            BindExcelActionsSettings();
            SetExcelSettingsStatus("Built-in TemplateCom rules loaded as a draft.");
        }));
        buttons.Children.Add(MgrButton("Save global default", (_, _) =>
            SaveExcelActionsSettings(saveForJob: false)));
        buttons.Children.Add(MgrButton("Save as this job", (_, _) =>
            SaveExcelActionsSettings(saveForJob: true)));
        buttons.Children.Add(MgrButton("Use global for this job", (_, _) =>
            ClearJobExcelActionsOverride()));
        buttons.Children.Add(MgrButton("Apply / Run selected", async (_, _) =>
        {
            if (!TrySyncExcelSettingsFromUi(out string error))
            {
                SetExcelSettingsStatus(error, isError: true);
                return;
            }
            ExcelMacroExportConfigProvider.Install(_excelMacroExportConfig);
            await RunExcelMacroTakeoffActionAsync(_excelSettingsActionId);
        }, primary: true));
        buttons.Children.Add(MgrButton("Apply / Run ALL", async (_, _) =>
        {
            if (!TrySyncExcelSettingsFromUi(out string error))
            {
                SetExcelSettingsStatus(error, isError: true);
                return;
            }
            ExcelMacroExportConfigProvider.Install(_excelMacroExportConfig);
            await RunExcelMacroBatchAsync();
        }, primary: true));
        content.Children.Add(buttons);

        _excelSettingsStatus = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = TryFindResource("SecondaryForegroundBrush") as Brush,
            Margin = new Thickness(0, 2, 0, 0),
        };
        content.Children.Add(_excelSettingsStatus);

        return new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    private TextBox AddExcelField(
        Grid grid,
        int row,
        int labelColumn,
        string label,
        int fieldColumn)
    {
        while (grid.RowDefinitions.Count <= row)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddExcelLabel(grid, row, labelColumn, label);
        var box = new TextBox { Margin = new Thickness(0, 0, 12, 6) };
        Grid.SetRow(box, row);
        Grid.SetColumn(box, fieldColumn);
        grid.Children.Add(box);
        return box;
    }

    private static void AddExcelLabel(Grid grid, int row, int column, string text)
    {
        var label = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 6),
        };
        Grid.SetRow(label, row);
        Grid.SetColumn(label, column);
        grid.Children.Add(label);
    }

    private void ExcelSettingsAction_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_excelSettingsSyncing)
            return;
        SyncExcelSettingsDraft();
        if (_excelSettingsAction?.SelectedValue is string id)
            _excelSettingsActionId = id;
        BindExcelActionFields();
    }

    private void BindExcelActionsSettings()
    {
        if (_excelSettingsAction == null)
            return;

        _excelSettingsSyncing = true;
        try
        {
            _excelSettingsAction.ItemsSource = null;
            _excelSettingsAction.ItemsSource = _excelMacroExportConfig.Actions;
            if (!_excelMacroExportConfig.Actions.Any(action =>
                    string.Equals(action.Id, _excelSettingsActionId, StringComparison.OrdinalIgnoreCase)))
            {
                _excelSettingsActionId = ExcelMacroExportActionIds.Sqft;
            }
            _excelSettingsAction.SelectedValue = _excelSettingsActionId;
            BindExcelActionFields();
            SetExcelSettingsStatus(
                $"Effective source: {ExcelActionsSettingsSource()}. " +
                "The vertical Excel strip uses this effective configuration.");
        }
        finally
        {
            _excelSettingsSyncing = false;
        }
    }

    private void BindExcelActionFields()
    {
        ExcelMacroExportActionConfig action =
            _excelMacroExportConfig.Action(_excelSettingsActionId);
        SetText(_excelSettingsWorkbook, action.WorkbookName);
        SetText(_excelSettingsSheet, action.SheetName);
        SetText(_excelSettingsAliases, string.Join(", ", action.FolderAliases));
        SetText(_excelSettingsScanStart, action.ScanStartColumn);
        SetText(_excelSettingsScanEnd, action.ScanEndColumn);
        SetText(_excelSettingsWriteStart, action.WriteStartColumn);
        SetText(_excelSettingsStartRow, action.StartRow.ToString());
        SetText(_excelSettingsBlankRows, action.BlankRowsBetween.ToString());
        SetText(_excelSettingsMacro, action.MacroName);
        SetText(_excelSettingsPreprocessMacro, action.PerFloorPreprocessMacroName);
        SetText(_excelSettingsAfterMacro, action.AfterMacroName);
        SetText(_excelSettingsAfterRange, action.AfterMacroRange);
        SetText(
            _excelSettingsProtectedLabels,
            string.Join(Environment.NewLine, action.AfterMacroProtectedLabels));
        SetText(
            _excelSettingsBatchOrder,
            string.Join(", ", _excelMacroExportConfig.BatchActionOrder));
        if (_excelSettingsUnits != null)
            _excelSettingsUnits.SelectedItem = action.UnitSystem;
        if (_excelSettingsRowOrder != null)
            _excelSettingsRowOrder.SelectedValue = action.RowOrderMode;
        if (_excelSettingsFloorHeaders != null)
            _excelSettingsFloorHeaders.IsChecked = action.UseFloorHeaders;

        ExcelMacroExportConfig defaults = ExcelMacroExportConfig.BuildDefault();
        foreach (int floor in Enumerable.Range(0, 6))
        {
            ExcelMacroFloorRule? rule =
                _excelMacroExportConfig.FloorRules.FirstOrDefault(item => item.Floor == floor) ??
                defaults.FloorRules.FirstOrDefault(item => item.Floor == floor);
            if (_excelSettingsFloorAliases.TryGetValue(floor, out TextBox? box))
                box.Text = string.Join(", ", rule?.Aliases ?? []);
        }
        BindExcelFramingSettingsFields();
    }

    private bool TrySyncExcelSettingsFromUi(out string error)
    {
        error = "";
        ExcelMacroExportActionConfig action =
            _excelMacroExportConfig.Action(_excelSettingsActionId);

        string workbook = TextOf(_excelSettingsWorkbook);
        string sheet = TextOf(_excelSettingsSheet);
        string macro = TextOf(_excelSettingsMacro);
        string scanStart = TextOf(_excelSettingsScanStart).ToUpperInvariant();
        string scanEnd = TextOf(_excelSettingsScanEnd).ToUpperInvariant();
        string writeStart = TextOf(_excelSettingsWriteStart).ToUpperInvariant();
        if (workbook.Length == 0 || sheet.Length == 0 || macro.Length == 0)
            error = "Workbook, worksheet, and VBA macro are required.";
        else if (ExcelMacroTakeoffExportService.ColumnNumber(scanStart) <= 0 ||
                 ExcelMacroTakeoffExportService.ColumnNumber(scanEnd) <
                 ExcelMacroTakeoffExportService.ColumnNumber(scanStart) ||
                 ExcelMacroTakeoffExportService.ColumnNumber(writeStart) <= 0)
            error = "Enter valid Excel columns, for example I, N, J or Z, AB, Z.";
        else if (!int.TryParse(TextOf(_excelSettingsStartRow), out int startRow) ||
                 startRow < 1)
            error = "First scan row must be a positive whole number.";
        else if (!int.TryParse(TextOf(_excelSettingsBlankRows), out int blankRows) ||
                 blankRows < 0)
            error = "Blank rows between blocks must be zero or greater.";

        string afterMacro = TextOf(_excelSettingsAfterMacro);
        string afterRange = TextOf(_excelSettingsAfterRange).ToUpperInvariant();
        if (error.Length == 0 &&
            string.IsNullOrWhiteSpace(afterMacro) != string.IsNullOrWhiteSpace(afterRange))
        {
            error = "After VBA and After range must be configured together.";
        }
        else if (error.Length == 0 &&
                 afterRange.Length > 0 &&
                 !ExcelMacroTakeoffExportService.TryValidateRangeAddress(
                     afterRange,
                     out string afterRangeError))
        {
            error = afterRangeError;
        }

        List<string> batchOrder = SplitExcelAliases(TextOf(_excelSettingsBatchOrder));
        HashSet<string> actionIds = _excelMacroExportConfig.Actions
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<string> unknownBatchIds = batchOrder
            .Where(id => !actionIds.Contains(id))
            .ToList();
        if (error.Length == 0 && batchOrder.Count == 0)
            error = "ALL sequence must contain at least one action id.";
        else if (error.Length == 0 && unknownBatchIds.Count > 0)
            error = $"Unknown ALL action id: {string.Join(", ", unknownBatchIds)}.";

        if (error.Length > 0)
            return false;

        action.WorkbookName = workbook;
        action.SheetName = sheet;
        action.MacroName = macro;
        action.FolderAliases = SplitExcelAliases(TextOf(_excelSettingsAliases));
        action.ScanStartColumn = scanStart;
        action.ScanEndColumn = scanEnd;
        action.WriteStartColumn = writeStart;
        action.StartRow = int.Parse(TextOf(_excelSettingsStartRow));
        action.BlankRowsBetween = int.Parse(TextOf(_excelSettingsBlankRows));
        action.UnitSystem = _excelSettingsUnits?.SelectedItem as string ?? "Imperial";
        action.UseFloorHeaders = _excelSettingsFloorHeaders?.IsChecked == true;
        action.PerFloorPreprocessMacroName = TextOf(_excelSettingsPreprocessMacro);
        action.RowOrderMode =
            _excelSettingsRowOrder?.SelectedValue as string ??
            ExcelMacroRowOrderModes.Source;
        action.AfterMacroName = afterMacro;
        action.AfterMacroRange = afterRange;
        action.AfterMacroProtectedLabels =
            SplitProtectedExcelLabels(_excelSettingsProtectedLabels?.Text ?? "");
        _excelMacroExportConfig.BatchActionOrder = batchOrder;

        var floorRules = new List<ExcelMacroFloorRule>();
        foreach (int floor in Enumerable.Range(0, 6))
        {
            string aliases = _excelSettingsFloorAliases.TryGetValue(floor, out TextBox? box)
                ? box.Text
                : "";
            floorRules.Add(new ExcelMacroFloorRule
            {
                Floor = floor,
                Aliases = SplitExcelAliases(aliases),
            });
        }
        _excelMacroExportConfig.FloorRules = floorRules;
        return TrySyncExcelFramingSettingsFromUi(out error);
    }

    private void SyncExcelSettingsDraft()
    {
        if (_excelSettingsWorkbook == null)
            return;
        _ = TrySyncExcelSettingsFromUi(out _);
    }

    private void SaveExcelActionsSettings(bool saveForJob)
    {
        if (!TrySyncExcelSettingsFromUi(out string error))
        {
            SetExcelSettingsStatus(error, isError: true);
            return;
        }
        if (saveForJob && _currentJob == null)
        {
            SetExcelSettingsStatus("Open a job before saving a job override.", isError: true);
            return;
        }

        try
        {
            if (saveForJob)
                SettingsPresetStore.SaveJobExcelMacroExportOverride(
                    _currentJob!,
                    _excelMacroExportConfig);
            else
                SettingsPresetStore.SaveGlobalExcelMacroExport(_excelMacroExportConfig);
            ExcelMacroExportConfigProvider.Install(_excelMacroExportConfig);
            SetExcelSettingsStatus(
                saveForJob
                    ? "Saved Excel macro actions for this job."
                    : "Saved global Excel macro action defaults.");
        }
        catch (Exception ex)
        {
            SetExcelSettingsStatus($"Could not save Excel actions: {ex.Message}", isError: true);
        }
    }

    private void ClearJobExcelActionsOverride()
    {
        if (_currentJob == null)
        {
            SetExcelSettingsStatus("Open a job before clearing its override.", isError: true);
            return;
        }
        if (!SettingsPresetStore.ClearJobExcelMacroExportOverride(_currentJob))
        {
            SetExcelSettingsStatus("Could not clear the job Excel action override.", isError: true);
            return;
        }

        _excelMacroExportConfig =
            SettingsPresetStore.ResolveExcelMacroExport(_currentJob).Clone();
        ExcelMacroExportConfigProvider.Install(_excelMacroExportConfig);
        BindExcelActionsSettings();
        SetExcelSettingsStatus("This job now uses the global Excel action defaults.");
    }

    private string ExcelActionsSettingsSource()
    {
        if (_currentJob != null &&
            SettingsPresetStore.LoadJobExcelMacroExportOverride(_currentJob) != null)
            return "this job override";
        if (SettingsPresetStore.LoadGlobalExcelMacroExport() != null)
            return "global default";
        return "built-in TemplateCom defaults";
    }

    private void SetExcelSettingsStatus(string text, bool isError = false)
    {
        if (_excelSettingsStatus == null)
            return;
        _excelSettingsStatus.Text = text;
        _excelSettingsStatus.Foreground = TryFindResource(
            isError ? "ErrorForegroundBrush" : "SecondaryForegroundBrush") as Brush;
    }

    private static List<string> SplitExcelAliases(string value) =>
        (value ?? "")
        .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static List<string> SplitProtectedExcelLabels(string value) =>
        (value ?? "")
        .Split(
            ['\r', '\n', ';'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static string TextOf(TextBox? box) => box?.Text.Trim() ?? "";

    private static void SetText(TextBox? box, string value)
    {
        if (box != null)
            box.Text = value ?? "";
    }
}
