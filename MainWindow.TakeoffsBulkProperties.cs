using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OurPlaneCore;

public partial class MainWindow
{
    private void EditSelectedTakeoffProperties(TreeViewItem anchor)
    {
        var selectedItems = TakeoffItemsForSelection(anchor)
            .Where(item => !string.IsNullOrWhiteSpace(item.FolderPath) && Directory.Exists(item.FolderPath))
            .GroupBy(item => NormalizePath(item.FolderPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (selectedItems.Count == 0)
        {
            TxtStatus.Text = "No takeoff items selected for bulk properties.";
            return;
        }

        if (!ShowBulkTakeoffPropertiesDialog(selectedItems, out BulkTakeoffPropertiesEdit edit))
            return;

        try
        {
            foreach (TakeoffItem selectedItem in selectedItems)
            {
                if (edit.ApplyColor)
                {
                    selectedItem.Color = edit.Color;
                    foreach (Measurement measurement in selectedItem.Measurements)
                        measurement.Color = edit.Color;
                }

                if (edit.ApplyUnitPrice)
                    selectedItem.UnitPrice = edit.UnitPrice;

                if (edit.ApplyNotes)
                    selectedItem.Notes = edit.Notes.Trim();

                OurPlaneCoreJobStore.SaveTakeoffItem(selectedItem);
                RefreshTreeItem(selectedItem);
            }

            if (edit.ApplyColor && _activeItem != null &&
                selectedItems.Any(item => string.Equals(item.FolderPath, _activeItem.FolderPath, StringComparison.OrdinalIgnoreCase)))
            {
                _viewport.ActiveColor = _activeItem.Color;
            }

            RefreshEstimateTable();
            RefreshPagesTakeoffIndicators();
            RefreshActiveTakeoffVisuals();
            RefreshSheetLegend();
            UpdateTotalDisplay();
            SelectTakeoffSelectionMeasurementsOnCurrentPage(anchor);
            TxtStatus.Text = $"Updated bulk properties for {selectedItems.Count} takeoff item(s).";
        }
        catch (Exception ex)
        {
            ShowOperationError("Bulk Takeoff Properties", ex);
        }
    }

    private bool ShowBulkTakeoffPropertiesDialog(
        IReadOnlyList<TakeoffItem> items,
        out BulkTakeoffPropertiesEdit edit)
    {
        string firstColor = NormalizeTakeoffColor(items[0].Color);
        bool sameColor = items.All(item =>
            string.Equals(NormalizeTakeoffColor(item.Color), firstColor, StringComparison.OrdinalIgnoreCase));
        double firstPrice = items[0].UnitPrice;
        bool samePrice = items.All(item => Math.Abs(item.UnitPrice - firstPrice) < 0.0000001);
        string firstNotes = items[0].Notes;
        bool sameNotes = items.All(item => string.Equals(item.Notes, firstNotes, StringComparison.Ordinal));
        var selectedTypes = items
            .Select(item => OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        bool sameType = selectedTypes.Count == 1;
        string typeText = sameType ? MeasurementTypeTitle(selectedTypes[0]) : "mixed Line/Area/Count";

        edit = new BulkTakeoffPropertiesEdit(false, firstColor, false, firstPrice, false, firstNotes);

        var dialog = new Window
        {
            Title = $"Bulk Takeoff Properties ({items.Count})",
            Owner = this,
            Width = 430,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
        };

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Selected items: {items.Count} | Type: {typeText}",
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 0, 0, 8),
        });

        var applyColorBox = new CheckBox
        {
            Content = sameColor ? "Apply color" : "Apply color (currently mixed)",
            Margin = new Thickness(0, 0, 0, 4),
        };
        panel.Children.Add(applyColorBox);

        string selectedColor = firstColor;
        var colorBox = new TextBox
        {
            Text = selectedColor,
            Width = 90,
            HorizontalAlignment = HorizontalAlignment.Left,
            IsEnabled = false,
        };
        var swatches = new List<Border>();
        var colorPanel = new WrapPanel
        {
            Margin = new Thickness(0, 0, 0, 4),
            IsEnabled = false,
        };
        foreach (var preset in TakeoffColorPresets)
        {
            var swatch = new Border
            {
                Width = 32,
                Height = 22,
                Margin = new Thickness(2),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(preset.Hex)),
                BorderBrush = string.Equals(preset.Hex, selectedColor, StringComparison.OrdinalIgnoreCase)
                    ? Brushes.White
                    : Brushes.Transparent,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(3),
                ToolTip = preset.Label,
                Cursor = Cursors.Hand,
            };
            swatch.MouseLeftButtonDown += (_, _) =>
            {
                selectedColor = preset.Hex;
                colorBox.Text = selectedColor;
                applyColorBox.IsChecked = true;
                foreach (Border border in swatches)
                    border.BorderBrush = Brushes.Transparent;
                swatch.BorderBrush = Brushes.White;
            };
            swatches.Add(swatch);
            colorPanel.Children.Add(swatch);
        }
        panel.Children.Add(colorPanel);
        colorBox.TextChanged += (_, _) => applyColorBox.IsChecked = true;
        panel.Children.Add(colorBox);

        var applyPriceBox = new CheckBox
        {
            Content = sameType
                ? $"Apply unit price per {UnitText(selectedTypes[0])}"
                : "Unit price disabled for mixed Line/Area/Count selection",
            IsEnabled = sameType,
            Margin = new Thickness(0, 12, 0, 4),
        };
        panel.Children.Add(applyPriceBox);
        var priceBox = new TextBox
        {
            Text = samePrice && firstPrice > 0 ? firstPrice.ToString("G", CultureInfo.InvariantCulture) : "0",
            IsEnabled = false,
        };
        if (sameType)
            priceBox.TextChanged += (_, _) => applyPriceBox.IsChecked = true;
        panel.Children.Add(priceBox);

        var applyNotesBox = new CheckBox
        {
            Content = sameNotes ? "Replace notes" : "Replace notes (currently mixed)",
            Margin = new Thickness(0, 12, 0, 4),
        };
        panel.Children.Add(applyNotesBox);
        var notesBox = new TextBox
        {
            Text = sameNotes ? firstNotes : "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 90,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsEnabled = false,
        };
        notesBox.TextChanged += (_, _) => applyNotesBox.IsChecked = true;
        panel.Children.Add(notesBox);

        void RefreshEnabledFields()
        {
            bool applyColor = applyColorBox.IsChecked == true;
            colorPanel.IsEnabled = applyColor;
            colorBox.IsEnabled = applyColor;
            priceBox.IsEnabled = sameType && applyPriceBox.IsChecked == true;
            notesBox.IsEnabled = applyNotesBox.IsChecked == true;
        }

        applyColorBox.Checked += (_, _) => RefreshEnabledFields();
        applyColorBox.Unchecked += (_, _) => RefreshEnabledFields();
        applyPriceBox.Checked += (_, _) => RefreshEnabledFields();
        applyPriceBox.Unchecked += (_, _) => RefreshEnabledFields();
        applyNotesBox.Checked += (_, _) => RefreshEnabledFields();
        applyNotesBox.Unchecked += (_, _) => RefreshEnabledFields();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var ok = new Button { Content = "OK", Width = 78, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 78, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        BulkTakeoffPropertiesEdit result = edit;
        ok.Click += (_, _) =>
        {
            bool applyColor = applyColorBox.IsChecked == true;
            bool applyPrice = sameType && applyPriceBox.IsChecked == true;
            bool applyNotes = applyNotesBox.IsChecked == true;
            if (!applyColor && !applyPrice && !applyNotes)
            {
                MessageBox.Show("Choose at least one property to apply.", "Bulk Takeoff Properties",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string cleanColor = NormalizeTakeoffColor(colorBox.Text);
            if (applyColor && !IsValidWpfColor(cleanColor))
            {
                MessageBox.Show("Enter a valid color like #FF4444.", "Bulk Takeoff Properties",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            double parsedPrice = firstPrice;
            if (applyPrice &&
                (!double.TryParse(priceBox.Text.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out parsedPrice) ||
                 parsedPrice < 0))
            {
                MessageBox.Show("Enter a valid non-negative unit price.", "Bulk Takeoff Properties",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            result = new BulkTakeoffPropertiesEdit(
                applyColor,
                cleanColor,
                applyPrice,
                parsedPrice,
                applyNotes,
                notesBox.Text.Trim());
            dialog.DialogResult = true;
        };

        bool accepted = dialog.ShowDialog() == true;
        if (accepted)
            edit = result;

        return accepted;
    }
}
