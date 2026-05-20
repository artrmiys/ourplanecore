using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace OurPlaneCore.Controls;

public class OpcDataGrid : DataGrid
{
    public static readonly DependencyProperty StatusFooterTextProperty =
        DependencyProperty.Register(
            nameof(StatusFooterText),
            typeof(string),
            typeof(OpcDataGrid),
            new FrameworkPropertyMetadata("Ready"));

    public static readonly DependencyProperty ShowStatusFooterProperty =
        DependencyProperty.Register(
            nameof(ShowStatusFooter),
            typeof(bool),
            typeof(OpcDataGrid),
            new FrameworkPropertyMetadata(true));

    public static readonly DependencyProperty AggregateColumnsProperty =
        DependencyProperty.Register(
            nameof(AggregateColumns),
            typeof(string[]),
            typeof(OpcDataGrid),
            new FrameworkPropertyMetadata(
                Array.Empty<string>(),
                (_, e) =>
                {
                    if (e.NewValue is not string[])
                        throw new ArgumentException("AggregateColumns must be a string array.");
                }));

    public static readonly DependencyProperty FreezeFirstColumnProperty =
        DependencyProperty.Register(
            nameof(FreezeFirstColumn),
            typeof(bool),
            typeof(OpcDataGrid),
            new FrameworkPropertyMetadata(
                false,
                (d, _) => ((OpcDataGrid)d).ApplyFrozenColumnState()));

    private ScrollViewer? _scrollViewer;
    private StickyGroupHeaderAdorner? _stickyHeaderAdorner;

    public OpcDataGrid()
    {
        EnableColumnVirtualization = true;
        EnableRowVirtualization = true;

        SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
        SetValue(VirtualizingPanel.IsVirtualizingWhenGroupingProperty, true);
        SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);
        SetValue(ScrollViewer.CanContentScrollProperty, true);

        if (GroupStyle.Count == 0)
            GroupStyle.Add(CreateDefaultGroupStyle());

        Loaded += OpcDataGrid_Loaded;
        Unloaded += OpcDataGrid_Unloaded;
        SelectedCellsChanged += OpcDataGrid_SelectedCellsChanged;
        SelectionChanged += OpcDataGrid_SelectionChanged;
    }

    public string StatusFooterText
    {
        get => (string)GetValue(StatusFooterTextProperty);
        set => SetValue(StatusFooterTextProperty, value);
    }

    public bool ShowStatusFooter
    {
        get => (bool)GetValue(ShowStatusFooterProperty);
        set => SetValue(ShowStatusFooterProperty, value);
    }

    public string[] AggregateColumns
    {
        get => (string[])GetValue(AggregateColumnsProperty);
        set => SetValue(AggregateColumnsProperty, value);
    }

    public bool FreezeFirstColumn
    {
        get => (bool)GetValue(FreezeFirstColumnProperty);
        set => SetValue(FreezeFirstColumnProperty, value);
    }

    public override void OnApplyTemplate()
    {
        if (_scrollViewer != null)
            _scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;

        base.OnApplyTemplate();

        _scrollViewer = FindVisualChild<ScrollViewer>(this);
        if (_scrollViewer != null)
            _scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;

        EnsureStickyHeaderAdorner();
        UpdateStickyGroupHeader();
    }

    protected override void OnItemsSourceChanged(IEnumerable? oldValue, IEnumerable? newValue)
    {
        base.OnItemsSourceChanged(oldValue, newValue);
        Dispatcher.BeginInvoke(UpdateStickyGroupHeader, DispatcherPriority.Loaded);
        UpdateStatusFooter();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (HandleExcelKey(e))
            return;

        base.OnPreviewKeyDown(e);
    }

    protected override void OnColumnDisplayIndexChanged(DataGridColumnEventArgs e)
    {
        base.OnColumnDisplayIndexChanged(e);
        ApplyFrozenColumnState();
    }

    private bool HandleExcelKey(KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        if (ctrl && e.Key == Key.D)
        {
            e.Handled = TryFillDownSelection();
            return e.Handled;
        }

        if (ctrl && (e.Key == Key.OemSemicolon || e.Key == Key.Oem1))
        {
            e.Handled = TryInsertToday();
            return e.Handled;
        }

        switch (e.Key)
        {
            case Key.F2:
                e.Handled = TryBeginCurrentCellEdit(e);
                return e.Handled;
            case Key.Enter:
                e.Handled = TryCommitAndMove(verticalDelta: shift ? -1 : 1, horizontalDelta: 0);
                return e.Handled;
            case Key.Tab:
                e.Handled = TryCommitAndMove(verticalDelta: 0, horizontalDelta: shift ? -1 : 1);
                return e.Handled;
            default:
                return false;
        }
    }

    private bool TryBeginCurrentCellEdit(RoutedEventArgs e)
    {
        if (!IsCurrentCellEditable())
            return false;

        Focus();
        return BeginEdit(e);
    }

    private bool TryCommitAndMove(int verticalDelta, int horizontalDelta)
    {
        if (!TryGetCurrentPosition(out int rowIndex, out int columnIndex))
            return false;

        CommitEdit(DataGridEditingUnit.Cell, true);
        CommitEdit(DataGridEditingUnit.Row, true);

        List<DataGridColumn> columns = VisibleColumnsByDisplayIndex();
        if (columns.Count == 0)
            return false;

        int targetRow = rowIndex + verticalDelta;
        int targetColumn = columnIndex + horizontalDelta;

        if (horizontalDelta > 0 && targetColumn >= columns.Count)
        {
            targetColumn = 0;
            targetRow++;
        }
        else if (horizontalDelta < 0 && targetColumn < 0)
        {
            targetColumn = columns.Count - 1;
            targetRow--;
        }

        targetRow = Math.Clamp(targetRow, 0, Items.Count - 1);
        targetColumn = Math.Clamp(targetColumn, 0, columns.Count - 1);

        object? targetItem = Items[targetRow];
        if (!IsDataItem(targetItem))
            return false;

        CurrentCell = new DataGridCellInfo(targetItem, columns[targetColumn]);
        SelectedCells.Clear();
        SelectedCells.Add(CurrentCell);
        ScrollIntoView(targetItem, columns[targetColumn]);
        Focus();
        return true;
    }

    private bool TryGetCurrentPosition(out int rowIndex, out int columnIndex)
    {
        rowIndex = -1;
        columnIndex = -1;

        if (!IsDataItem(CurrentItem) ||
            CurrentCell.Column == null)
        {
            return false;
        }

        rowIndex = Items.IndexOf(CurrentItem);
        List<DataGridColumn> columns = VisibleColumnsByDisplayIndex();
        columnIndex = columns.IndexOf(CurrentCell.Column);
        return rowIndex >= 0 && columnIndex >= 0;
    }

    private bool TryFillDownSelection()
    {
        if (!IsCurrentCellEditable() ||
            !TryGetCurrentBindingPath(out string bindingPath) ||
            !TryGetPropertyValue(CurrentItem, bindingPath, out object? activeValue))
        {
            return false;
        }

        List<object> targets = SelectedItemsForActiveColumn();
        if (targets.Count <= 1)
            return false;

        foreach (object target in targets)
        {
            if (ReferenceEquals(target, CurrentItem))
                continue;

            TrySetPropertyValue(target, bindingPath, activeValue);
        }

        RefreshCurrentView();
        UpdateStatusFooter();
        return true;
    }

    private bool TryInsertToday()
    {
        if (!IsCurrentCellEditable() ||
            !TryGetCurrentBindingPath(out string bindingPath))
        {
            return false;
        }

        string today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!TrySetPropertyValue(CurrentItem, bindingPath, today))
            return false;

        RefreshCurrentView();
        UpdateStatusFooter();
        return true;
    }

    private List<object> SelectedItemsForActiveColumn()
    {
        var targets = new List<object>();
        DataGridColumn? activeColumn = CurrentCell.Column;
        if (activeColumn == null)
            return targets;

        foreach (DataGridCellInfo cell in SelectedCells)
        {
            if (ReferenceEquals(cell.Column, activeColumn) && IsDataItem(cell.Item))
                AddDistinct(targets, cell.Item);
        }

        foreach (object item in SelectedItems)
        {
            if (IsDataItem(item))
                AddDistinct(targets, item);
        }

        if (targets.Count == 0 && IsDataItem(CurrentItem))
            targets.Add(CurrentItem);

        return targets;
    }

    private bool IsCurrentCellEditable() =>
        !IsReadOnly &&
        CurrentCell.Column is { IsReadOnly: false } &&
        TryGetCurrentBindingPath(out string bindingPath) &&
        CanWriteProperty(CurrentItem, bindingPath);

    private bool TryGetCurrentBindingPath(out string bindingPath)
    {
        bindingPath = "";
        return CurrentCell.Column != null &&
               TryGetColumnBindingPath(CurrentCell.Column, out bindingPath);
    }

    private static bool TryGetColumnBindingPath(DataGridColumn column, out string bindingPath)
    {
        bindingPath = "";
        if (column is not DataGridBoundColumn boundColumn ||
            boundColumn.Binding is not Binding binding ||
            binding.Path == null ||
            string.IsNullOrWhiteSpace(binding.Path.Path))
        {
            return false;
        }

        bindingPath = binding.Path.Path;
        return true;
    }

    private void UpdateStatusFooter()
    {
        if (SelectedCells.Count == 0 && SelectedItems.Count == 0)
        {
            StatusFooterText = "Ready";
            return;
        }

        int selectedCount = SelectedCells.Count > 0 ? SelectedCells.Count : SelectedItems.Count;
        var aggregateParts = new List<string>();
        foreach (string bindingPath in AggregateColumns.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            if (!TrySumSelectedValues(bindingPath, out string header, out double sum))
                continue;

            aggregateParts.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"Σ {header} = {sum:N2}"));
        }

        StatusFooterText = aggregateParts.Count == 0
            ? $"{selectedCount.ToString(CultureInfo.InvariantCulture)} selected · Ready"
            : $"{selectedCount.ToString(CultureInfo.InvariantCulture)} selected · {string.Join("  ", aggregateParts)}";
    }

    private bool TrySumSelectedValues(string bindingPath, out string header, out double sum)
    {
        header = BindingHeader(bindingPath);
        sum = 0;
        bool foundNumeric = false;

        IEnumerable<object> selectedItems = SelectedCells.Count > 0
            ? SelectedCells
                .Where(cell => CellMatchesBindingPath(cell, bindingPath))
                .Select(cell => cell.Item)
                .Where(IsDataItem)
            : SelectedItems.Cast<object>().Where(IsDataItem);

        foreach (object item in selectedItems)
        {
            if (!TryGetPropertyValue(item, bindingPath, out object? rawValue))
                continue;

            string? text = Convert.ToString(rawValue, CultureInfo.InvariantCulture);
            if (!double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double value))
                continue;

            sum += value;
            foundNumeric = true;
        }

        return foundNumeric;
    }

    private bool CellMatchesBindingPath(DataGridCellInfo cell, string bindingPath) =>
        TryGetColumnBindingPath(cell.Column, out string cellBindingPath) &&
        string.Equals(cellBindingPath, bindingPath, StringComparison.Ordinal);

    private string BindingHeader(string bindingPath)
    {
        DataGridColumn? column = Columns.FirstOrDefault(column =>
            TryGetColumnBindingPath(column, out string columnBindingPath) &&
            string.Equals(columnBindingPath, bindingPath, StringComparison.Ordinal));

        return column?.Header?.ToString() ?? bindingPath;
    }

    private void ApplyFrozenColumnState()
    {
        FrozenColumnCount = FreezeFirstColumn ? 1 : 0;
    }

    private void OpcDataGrid_Loaded(object sender, RoutedEventArgs e)
    {
        EnsureStickyHeaderAdorner();
        ApplyFrozenColumnState();
        UpdateStatusFooter();
        UpdateStickyGroupHeader();
    }

    private void OpcDataGrid_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_scrollViewer != null)
            _scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;

        RemoveStickyHeaderAdorner();
    }

    private void OpcDataGrid_SelectedCellsChanged(object sender, SelectedCellsChangedEventArgs e)
    {
        UpdateStatusFooter();
        UpdateStickyGroupHeader();
    }

    private void OpcDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateStatusFooter();
        UpdateStickyGroupHeader();
    }

    private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateStickyGroupHeader();
    }

    private void EnsureStickyHeaderAdorner()
    {
        if (_stickyHeaderAdorner != null)
            return;

        AdornerLayer? layer = AdornerLayer.GetAdornerLayer(this);
        if (layer == null)
            return;

        _stickyHeaderAdorner = new StickyGroupHeaderAdorner(this);
        layer.Add(_stickyHeaderAdorner);
    }

    private void RemoveStickyHeaderAdorner()
    {
        if (_stickyHeaderAdorner == null)
            return;

        AdornerLayer? layer = AdornerLayer.GetAdornerLayer(this);
        layer?.Remove(_stickyHeaderAdorner);
        _stickyHeaderAdorner = null;
    }

    private void UpdateStickyGroupHeader()
    {
        if (_stickyHeaderAdorner == null)
            return;

        if (CollectionViewSource.GetDefaultView(ItemsSource) is not ICollectionView view ||
            view.GroupDescriptions.Count == 0 ||
            view.GroupDescriptions[0] is not PropertyGroupDescription groupDescription ||
            string.IsNullOrWhiteSpace(groupDescription.PropertyName) ||
            !TryGetFirstVisibleDataItem(out object? item) ||
            !TryGetPropertyValue(item, groupDescription.PropertyName, out object? groupValue))
        {
            _stickyHeaderAdorner.Hide();
            return;
        }

        string groupText = Convert.ToString(groupValue, CultureInfo.CurrentCulture) ?? "(Blank)";
        if (string.IsNullOrWhiteSpace(groupText))
            groupText = "(Blank)";

        double top = FindVisualChild<DataGridColumnHeadersPresenter>(this)?.ActualHeight ?? 0;
        _stickyHeaderAdorner.Show(groupText, top);
    }

    private bool TryGetFirstVisibleDataItem(out object? item)
    {
        item = null;
        double headerHeight = FindVisualChild<DataGridColumnHeadersPresenter>(this)?.ActualHeight ?? 0;

        for (int index = 0; index < Items.Count; index++)
        {
            if (ItemContainerGenerator.ContainerFromIndex(index) is not DataGridRow row ||
                !row.IsVisible ||
                row.ActualHeight <= 0)
            {
                continue;
            }

            Point rowPoint = row.TranslatePoint(new Point(0, 0), this);
            if (rowPoint.Y + row.ActualHeight < headerHeight)
                continue;

            object? candidate = Items[index];
            if (!IsDataItem(candidate))
                continue;

            item = candidate;
            return true;
        }

        return false;
    }

    private static GroupStyle CreateDefaultGroupStyle()
    {
        var panelFactory = new FrameworkElementFactory(typeof(VirtualizingStackPanel));
        panelFactory.SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
        panelFactory.SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);

        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.SetResourceReference(Border.BackgroundProperty, "ManagerHeaderBrush");
        borderFactory.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
        borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
        borderFactory.SetValue(Border.PaddingProperty, new Thickness(8, 4, 8, 4));

        var textFactory = new FrameworkElementFactory(typeof(TextBlock));
        textFactory.SetBinding(TextBlock.TextProperty, new Binding("Name"));
        textFactory.SetValue(TextBlock.FontSizeProperty, 11.0);
        textFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        textFactory.SetResourceReference(TextBlock.ForegroundProperty, "ControlForegroundBrush");

        borderFactory.AppendChild(textFactory);

        return new GroupStyle
        {
            HeaderTemplate = new DataTemplate { VisualTree = borderFactory },
            Panel = new ItemsPanelTemplate(panelFactory),
        };
    }

    private List<DataGridColumn> VisibleColumnsByDisplayIndex() =>
        Columns
            .Where(column => column.Visibility == Visibility.Visible)
            .OrderBy(column => column.DisplayIndex)
            .ToList();

    private void RefreshCurrentView()
    {
        CollectionViewSource.GetDefaultView(ItemsSource)?.Refresh();
    }

    private static bool IsDataItem(object? item) =>
        item != null &&
        !ReferenceEquals(item, CollectionView.NewItemPlaceholder) &&
        item is not CollectionViewGroup;

    private static void AddDistinct(List<object> items, object item)
    {
        if (items.Any(existing => ReferenceEquals(existing, item)))
            return;

        items.Add(item);
    }

    private static bool CanWriteProperty(object? item, string bindingPath)
    {
        if (!TryResolveProperty(item, bindingPath, out _, out PropertyInfo? property) ||
            property == null)
            return false;

        return property.CanWrite;
    }

    private static bool TryGetPropertyValue(object? item, string bindingPath, out object? value)
    {
        value = null;
        if (!TryResolveProperty(item, bindingPath, out object? owner, out PropertyInfo? property) ||
            owner == null ||
            property == null)
        {
            return false;
        }

        value = property.GetValue(owner);
        return true;
    }

    private static bool TrySetPropertyValue(object? item, string bindingPath, object? value)
    {
        if (!TryResolveProperty(item, bindingPath, out object? owner, out PropertyInfo? property) ||
            owner == null ||
            property == null ||
            !property.CanWrite)
        {
            return false;
        }

        object? converted = ConvertForProperty(value, property.PropertyType);
        if (converted == UnsetValueMarker.Instance)
            return false;

        property.SetValue(owner, converted);
        return true;
    }

    private static bool TryResolveProperty(
        object? item,
        string bindingPath,
        out object? owner,
        out PropertyInfo? property)
    {
        owner = null;
        property = null;

        if (item == null || string.IsNullOrWhiteSpace(bindingPath))
            return false;

        object? current = item;
        string[] parts = bindingPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int index = 0; index < parts.Length; index++)
        {
            if (current == null)
                return false;

            PropertyInfo? nextProperty = current.GetType().GetProperty(parts[index], BindingFlags.Instance | BindingFlags.Public);
            if (nextProperty == null)
                return false;

            if (index == parts.Length - 1)
            {
                owner = current;
                property = nextProperty;
                return true;
            }

            current = nextProperty.GetValue(current);
        }

        return false;
    }

    private static object? ConvertForProperty(object? value, Type propertyType)
    {
        Type targetType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (value == null)
            return propertyType.IsValueType && Nullable.GetUnderlyingType(propertyType) == null
                ? UnsetValueMarker.Instance
                : null;

        if (targetType.IsInstanceOfType(value))
            return value;

        try
        {
            if (targetType == typeof(string))
                return Convert.ToString(value, CultureInfo.InvariantCulture);

            if (targetType == typeof(DateTime) &&
                DateTime.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
            {
                return date;
            }

            if (targetType.IsEnum)
                return Enum.Parse(targetType, Convert.ToString(value, CultureInfo.InvariantCulture) ?? "", ignoreCase: true);

            return Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            return UnsetValueMarker.Instance;
        }
        catch (InvalidCastException)
        {
            return UnsetValueMarker.Instance;
        }
        catch (OverflowException)
        {
            return UnsetValueMarker.Instance;
        }
        catch (ArgumentException)
        {
            return UnsetValueMarker.Instance;
        }
    }

    private static T? FindVisualChild<T>(DependencyObject root)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                return match;

            T? nested = FindVisualChild<T>(child);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private sealed class UnsetValueMarker
    {
        public static readonly UnsetValueMarker Instance = new();

        private UnsetValueMarker()
        {
        }
    }

    private sealed class StickyGroupHeaderAdorner : Adorner
    {
        private readonly Border _border;
        private readonly TextBlock _textBlock;
        private readonly VisualCollection _visuals;
        private double _top;

        public StickyGroupHeaderAdorner(UIElement adornedElement)
            : base(adornedElement)
        {
            IsHitTestVisible = false;
            _visuals = new VisualCollection(this);
            _textBlock = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _textBlock.SetResourceReference(TextBlock.ForegroundProperty, "ControlForegroundBrush");

            _border = new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(8, 4, 8, 4),
                Child = _textBlock,
                Visibility = Visibility.Collapsed,
            };
            _border.SetResourceReference(Border.BackgroundProperty, "ManagerHeaderBrush");
            _border.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
            _visuals.Add(_border);
        }

        protected override int VisualChildrenCount => _visuals.Count;

        protected override Size MeasureOverride(Size constraint)
        {
            _border.Measure(new Size(constraint.Width, double.PositiveInfinity));
            return constraint;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double height = Math.Max(24, _border.DesiredSize.Height);
            _border.Arrange(new Rect(0, _top, finalSize.Width, height));
            return finalSize;
        }

        protected override Visual GetVisualChild(int index) => _visuals[index];

        public void Show(string text, double top)
        {
            _textBlock.Text = text;
            _top = top;
            _border.Visibility = Visibility.Visible;
            InvalidateMeasure();
            InvalidateArrange();
        }

        public void Hide()
        {
            _border.Visibility = Visibility.Collapsed;
            _textBlock.Text = "";
            InvalidateArrange();
        }
    }
}
