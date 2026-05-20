using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace OurPlaneCore.Controls;

public partial class OpcDataGridDemo : UserControl
{
    private readonly ObservableCollection<DemoRow> _rows;
    private readonly ICollectionView _view;
    private bool _isGrouped;
    private int _nextNumber;

    public OpcDataGridDemo()
    {
        InitializeComponent();

        _rows = new ObservableCollection<DemoRow>(Enumerable.Range(1, 30).Select(CreateDemoRow));
        _nextNumber = _rows.Count + 1;
        _view = CollectionViewSource.GetDefaultView(_rows);

        DataContext = this;
        DemoGrid.AggregateColumns = [nameof(DemoRow.Qty), nameof(DemoRow.Cost)];
        DemoGrid.ItemsSource = _view;
    }

    private void AddRow_Click(object sender, RoutedEventArgs e)
    {
        DemoRow row = CreateDemoRow(_nextNumber++);
        _rows.Add(row);
        _view.Refresh();
        DemoGrid.ScrollIntoView(row);
        DemoGrid.CurrentCell = new DataGridCellInfo(row, DemoGrid.Columns[1]);
    }

    private void GroupByFolder_Click(object sender, RoutedEventArgs e)
    {
        _isGrouped = !_isGrouped;
        _view.GroupDescriptions.Clear();
        if (_isGrouped)
            _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DemoRow.Folder)));

        _view.Refresh();
    }

    private void ToggleFreeze_Click(object sender, RoutedEventArgs e)
    {
        DemoGrid.FreezeFirstColumn = !DemoGrid.FreezeFirstColumn;
    }

    private void ToggleFooter_Click(object sender, RoutedEventArgs e)
    {
        DemoGrid.ShowStatusFooter = !DemoGrid.ShowStatusFooter;
    }

    private static DemoRow CreateDemoRow(int number)
    {
        string[] folders = ["Concrete", "Steel", "Openings", "Exterior", "Finish"];
        string[] names = ["Slab", "Beam", "Window", "Wall", "Trim", "Deck", "Column", "Header"];
        string[] colors = ["#FF4444", "#4444FF", "#22AA66", "#CC8A00", "#7A4DFF"];

        string folder = folders[(number - 1) % folders.Length];
        string name = $"{names[(number - 1) % names.Length]} {number:D2}";
        double qty = Math.Round(36.5 + ((number * 7.35) % 180), 2);
        double cost = Math.Round(8.75 + ((number * 3.2) % 65), 2);

        return new DemoRow(number, folder, name, qty, cost, colors[(number - 1) % colors.Length]);
    }

    private sealed class DemoRow : INotifyPropertyChanged
    {
        private int _n;
        private string _folder;
        private string _name;
        private double _qty;
        private double _cost;
        private string _color;

        public DemoRow(int n, string folder, string name, double qty, double cost, string color)
        {
            _n = n;
            _folder = folder;
            _name = name;
            _qty = qty;
            _cost = cost;
            _color = color;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public int N
        {
            get => _n;
            set => SetField(ref _n, value);
        }

        public string Folder
        {
            get => _folder;
            set => SetField(ref _folder, value);
        }

        public string Name
        {
            get => _name;
            set => SetField(ref _name, value);
        }

        public double Qty
        {
            get => _qty;
            set => SetField(ref _qty, value);
        }

        public double Cost
        {
            get => _cost;
            set => SetField(ref _cost, value);
        }

        public string Color
        {
            get => _color;
            set => SetField(ref _color, value);
        }

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
