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
    private void SetUnitPrice(TakeoffItem item)
    {
        if (!RequireModule(ModuleId.Estimating, "Set Unit Price"))
            return;

        string? raw = ShowInputDialog(
            $"Unit price per {TakeoffUnitText(item)}:",
            item.UnitPrice > 0 ? item.UnitPrice.ToString("G", CultureInfo.InvariantCulture) : "0",
            "Set Unit Price");
        if (raw == null)
            return;

        if (!double.TryParse(raw.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out double price) ||
            price < 0)
        {
            PostStatusWarning("Enter a valid non-negative unit price.");
            return;
        }

        item.UnitPrice = price;
        QueueTakeoffAutosave(item);
        RefreshTreeItem(item);
        RefreshEstimateTable();
        RefreshSheetLegend();
        TxtStatus.Text = $"Unit price set for {item.Name}: {price:G}";
    }

    private void EditTakeoffItemProperties(TreeViewItem tvi, TakeoffItem item)
    {
        if (!ShowTakeoffItemPropertiesDialog(
                item,
                out string name,
                out string color,
                out double unitPrice,
                out string notes,
                out string countSymbol,
                out JoistTakeoffEdit joistEdit))
        {
            return;
        }

        try
        {
            bool colorChanged = !string.Equals(item.Color, color, StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(item.FolderPath) && Directory.Exists(item.FolderPath) &&
                !string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                string oldPath = item.FolderPath;
                string newPath = OurPlaneCoreJobStore.RenameNodeAllowDuplicateName(item.FolderPath, name);
                UnregisterTakeoffTreeItemPath(oldPath, tvi);
                item.FolderPath = newPath;
                RebasePageLegendTakeoffOrderReferences(oldPath, item.FolderPath);
                item.Name = OurPlaneCoreJobStore.DisplayName(item.FolderPath);
                foreach (var measurement in item.Measurements)
                    measurement.TakeoffFolder = item.FolderPath;
                RegisterTakeoffTreeItemSubtree(tvi);
            }
            else
            {
                item.Name = OurPlaneCoreJobStore.NormalizeDisplayName(name, 120);
            }

            item.Color = color;
            item.UnitPrice = unitPrice;
            item.Notes = notes.Trim();
            string normalizedCountSymbol = CountDisplaySymbol.Normalize(countSymbol);
            bool countSymbolChanged =
                IsCountTakeoffItem(item) &&
                !string.Equals(item.CountSymbol, normalizedCountSymbol, StringComparison.OrdinalIgnoreCase);
            if (IsCountTakeoffItem(item))
                item.CountSymbol = normalizedCountSymbol;

            bool joistChanged =
                item.IsJoistTakeoff != joistEdit.Enabled ||
                !string.Equals(item.JoistType, joistEdit.JoistType, StringComparison.Ordinal) ||
                Math.Abs(item.JoistSpacingInches - joistEdit.SpacingInches) > 0.0001 ||
                Math.Abs(item.JoistDirectionDegrees - joistEdit.DirectionDegrees) > 0.0001 ||
                !string.Equals(
                    JoistTakeoffCalculator.NormalizePitch(item.JoistPitch),
                    JoistTakeoffCalculator.NormalizePitch(joistEdit.Pitch),
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    JoistTakeoffCalculator.NormalizeLengthRounding(item.JoistLengthRounding),
                    JoistTakeoffCalculator.NormalizeLengthRounding(joistEdit.LengthRounding),
                    StringComparison.OrdinalIgnoreCase) ||
                item.JoistDirectionFollowsAreaRotation != joistEdit.DirectionFollowsAreaRotation ||
                item.JoistAddEndJoist != joistEdit.AddEndJoist ||
                item.JoistShowLabels != joistEdit.ShowLabels ||
                item.JoistDetailedLabels != joistEdit.DetailedLabels;
            bool wasJoistArea = item.IsJoistArea;
            bool joistShowLabelsChangedByDialog = item.JoistShowLabels != joistEdit.ShowLabels;
            item.IsJoistTakeoff = joistEdit.Enabled && OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType) == "area";
            item.JoistType = joistEdit.JoistType.Trim();
            item.JoistSpacingInches = joistEdit.SpacingInches > 0 ? joistEdit.SpacingInches : 16;
            item.JoistDirectionDegrees = joistEdit.DirectionDegrees;
            item.JoistDirectionFollowsAreaRotation = joistEdit.DirectionFollowsAreaRotation;
            item.JoistAddEndJoist = joistEdit.AddEndJoist;
            item.JoistPitch = JoistTakeoffCalculator.NormalizePitch(joistEdit.Pitch);
            item.JoistLengthRounding = JoistTakeoffCalculator.NormalizeLengthRounding(joistEdit.LengthRounding);
            item.JoistShowLabels = joistEdit.ShowLabels;
            if (item.IsJoistArea && joistShowLabelsChangedByDialog)
                item.JoistShowLabelsUserSet = true;
            item.JoistDetailedLabels = joistEdit.DetailedLabels;
            OurPlaneCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);
            if (colorChanged)
            {
                foreach (Measurement measurement in item.Measurements)
                    measurement.Color = color;
            }
            if (countSymbolChanged)
            {
                foreach (Measurement measurement in item.Measurements.Where(IsCountMeasurement))
                    measurement.CountSymbol = item.CountSymbol;
            }
            if (colorChanged || joistChanged || countSymbolChanged)
                _viewport.SetMeasurements(_takeoffItems.SelectMany(takeoff => takeoff.Measurements));

            QueueTakeoffAutosave(item);
            SetTreeItemHeader(tvi, item);
            RefreshTakeoffSectionNodes(tvi, item);
            RefreshEstimateTable();
            RefreshPagesTakeoffIndicators();
            ApplyTakeoffPageHighlights();
            RefreshSheetLegend();
            UpdateTotalDisplay();
            if (item.IsJoistArea && (!wasJoistArea || item.HasPendingJoistDirections))
                BeginNextPendingJoistDirectionCapture(item);
            TxtStatus.Text = $"Updated takeoff item properties: {item.Name}";
        }
        catch (Exception ex)
        {
            ShowOperationError("Takeoff Item Properties", ex);
        }
    }

    private bool ShowTakeoffItemPropertiesDialog(
        TakeoffItem item,
        out string name,
        out string color,
        out double unitPrice,
        out string notes,
        out string countSymbol,
        out JoistTakeoffEdit joistEdit)
    {
        name = item.Name;
        color = NormalizeTakeoffColor(item.Color);
        unitPrice = item.UnitPrice;
        notes = item.Notes;
        countSymbol = CountDisplaySymbol.Normalize(item.CountSymbol);
        string measurementType = OurPlaneCoreJobStore.NormalizeMeasurementType(item.MeasurementType);
        bool isAreaTakeoff = measurementType == "area";
        bool isLineTakeoff = measurementType == "line";
        bool isPointTakeoff = measurementType == "point";
        bool estimatingEnabled = IsModuleEnabled(ModuleId.Estimating);
        bool seedJoistEnableDefaults = isAreaTakeoff && !item.IsJoistArea;
        string initialJoistRounding = seedJoistEnableDefaults
            ? JoistTakeoffDefaults.LengthRounding
            : JoistTakeoffCalculator.NormalizeLengthRounding(item.JoistLengthRounding);
        bool initialJoistDetailedLabels = seedJoistEnableDefaults
            ? JoistTakeoffDefaults.DetailedAreaLabel
            : item.JoistDetailedLabels;
        bool initialJoistShowLabels = seedJoistEnableDefaults
            ? JoistTakeoffDefaults.ShowLabels
            : item.JoistShowLabels;
        joistEdit = new JoistTakeoffEdit(
            item.IsJoistArea,
            item.JoistType,
            item.JoistSpacingInches > 0 ? item.JoistSpacingInches : 16,
            item.JoistDirectionDegrees,
            item.JoistDirectionFollowsAreaRotation,
            item.JoistAddEndJoist,
            JoistTakeoffCalculator.NormalizePitch(item.JoistPitch),
            initialJoistRounding,
            initialJoistShowLabels,
            initialJoistDetailedLabels);

        var dialog = new Window
        {
            Title = "Takeoff Item Properties",
            Owner = this,
            Width = 500,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
        };

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = "Name:", Margin = new Thickness(0, 0, 0, 4) });
        var nameBox = new TextBox { Text = item.Name };
        panel.Children.Add(nameBox);

        panel.Children.Add(new TextBlock
        {
            Text = $"Type: {MeasurementTypeTitle(item.MeasurementType)}",
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 8, 0, 0),
        });
        var joistEnabledBox = new CheckBox
        {
            Content = "Joist layout",
            IsChecked = item.IsJoistArea,
            IsEnabled = isAreaTakeoff,
            Margin = new Thickness(0, 10, 0, 2),
        };
        if (isAreaTakeoff)
            panel.Children.Add(joistEnabledBox);

        var joistPanel = new Grid
        {
            Margin = new Thickness(18, 0, 0, 6),
            IsEnabled = isAreaTakeoff && joistEnabledBox.IsChecked == true,
        };
        joistPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });
        joistPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 9; i++)
            joistPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddLabeledTextBox(joistPanel, 0, "Joist type:", out TextBox joistTypeBox, item.JoistType);
        AddLabeledTextBox(
            joistPanel,
            1,
            "O.C. spacing (in):",
            out TextBox joistSpacingBox,
            (item.JoistSpacingInches > 0 ? item.JoistSpacingInches : 16).ToString("G", CultureInfo.InvariantCulture));
        AddLabeledTextBox(
            joistPanel,
            2,
            "Pitch (rise:run):",
            out TextBox joistPitchBox,
            JoistTakeoffCalculator.NormalizePitch(item.JoistPitch));
        joistPitchBox.ToolTip = "Roof pitch as rise:run, e.g. 3:12. Blank or 0:12 is flat.";
        var directionLabel = new TextBlock
        {
            Text = "Joist direction:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 3, 8, 3),
        };
        Grid.SetRow(directionLabel, 3);
        Grid.SetColumn(directionLabel, 0);
        joistPanel.Children.Add(directionLabel);
        var joistDirectionBox = new TextBox
        {
            Text = item.JoistDirectionDegrees.ToString("G", CultureInfo.InvariantCulture),
            IsReadOnly = true,
            Width = 78,
            ToolTip = "Direction is set by drawing a two-point line parallel to the joists after selecting or drawing an Area.",
        };
        Grid.SetRow(joistDirectionBox, 3);
        Grid.SetColumn(joistDirectionBox, 1);
        joistPanel.Children.Add(joistDirectionBox);

        var roundingLabel = new TextBlock
        {
            Text = "Length calc:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 3, 8, 3),
        };
        Grid.SetRow(roundingLabel, 4);
        Grid.SetColumn(roundingLabel, 0);
        joistPanel.Children.Add(roundingLabel);
        var roundingBox = new ComboBox
        {
            MinWidth = 190,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 3, 0, 3),
        };
        foreach (string rounding in new[]
                 {
                     JoistTakeoffCalculator.RoundingNone,
                     JoistTakeoffCalculator.RoundingNearestFoot,
                     JoistTakeoffCalculator.RoundingNearestEvenFoot,
                     JoistTakeoffCalculator.RoundingNearestTwoFeet,
                 })
        {
            roundingBox.Items.Add(new ComboBoxItem
            {
                Content = JoistTakeoffCalculator.LengthRoundingTitle(rounding),
                Tag = rounding,
            });
        }
        string selectedRounding = initialJoistRounding;
        for (int i = 0; i < roundingBox.Items.Count; i++)
        {
            if (roundingBox.Items[i] is ComboBoxItem option &&
                string.Equals((string?)option.Tag, selectedRounding, StringComparison.OrdinalIgnoreCase))
            {
                roundingBox.SelectedIndex = i;
                break;
            }
        }
        if (roundingBox.SelectedIndex < 0)
            roundingBox.SelectedIndex = 0;
        Grid.SetRow(roundingBox, 4);
        Grid.SetColumn(roundingBox, 1);
        joistPanel.Children.Add(roundingBox);

        var joistDirectionFollowsBox = new CheckBox
        {
            Content = "Rotate direction with area",
            IsChecked = item.JoistDirectionFollowsAreaRotation,
            Margin = new Thickness(0, 3, 0, 3),
            ToolTip = "When on, rotating a joist area rotates the saved joist direction with it.",
        };
        Grid.SetRow(joistDirectionFollowsBox, 5);
        Grid.SetColumn(joistDirectionFollowsBox, 1);
        joistPanel.Children.Add(joistDirectionFollowsBox);

        var joistAddEndBox = new CheckBox
        {
            Content = "Add end joist",
            IsChecked = item.JoistAddEndJoist,
            Margin = new Thickness(0, 3, 0, 3),
            ToolTip = "When on, add one joist at the far edge if the spacing pattern does not land there.",
        };
        Grid.SetRow(joistAddEndBox, 6);
        Grid.SetColumn(joistAddEndBox, 1);
        joistPanel.Children.Add(joistAddEndBox);

        var joistLabelsBox = new CheckBox
        {
            Content = "Label each joist",
            IsChecked = initialJoistShowLabels,
            Margin = new Thickness(0, 3, 0, 3),
            ToolTip = "When off, the area label still shows count and order length.",
        };
        Grid.SetRow(joistLabelsBox, 7);
        Grid.SetColumn(joistLabelsBox, 1);
        joistPanel.Children.Add(joistLabelsBox);

        var joistDetailedLabelsBox = new CheckBox
        {
            Content = "Detailed area label",
            IsChecked = initialJoistDetailedLabels,
            Margin = new Thickness(0, 3, 0, 3),
            ToolTip = "On: show order/raw/flat lengths. Off: use the old compact count / length format.",
        };
        Grid.SetRow(joistDetailedLabelsBox, 8);
        Grid.SetColumn(joistDetailedLabelsBox, 1);
        joistPanel.Children.Add(joistDetailedLabelsBox);

        joistEnabledBox.Checked += (_, _) => joistPanel.IsEnabled = isAreaTakeoff;
        joistEnabledBox.Unchecked += (_, _) => joistPanel.IsEnabled = false;
        if (isAreaTakeoff)
            panel.Children.Add(joistPanel);

        TextBox? pointOcSpacingBox = null;
        if (isLineTakeoff)
        {
            var pointOcPanel = new Grid
            {
                Margin = new Thickness(0, 10, 0, 4),
            };
            pointOcPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });
            pointOcPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pointOcPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            AddLabeledTextBox(
                pointOcPanel,
                0,
                "Point O.C. spacing (in):",
                out pointOcSpacingBox,
                (item.JoistSpacingInches > 0 ? item.JoistSpacingInches : 16).ToString("G", CultureInfo.InvariantCulture));
            pointOcSpacingBox.ToolTip = "Default spacing used by Create Count Points Along Lines.";
            panel.Children.Add(pointOcPanel);
        }

        ComboBox? countSymbolBox = null;
        if (isPointTakeoff)
        {
            panel.Children.Add(new TextBlock { Text = "Count display:", Margin = new Thickness(0, 10, 0, 4) });
            countSymbolBox = new ComboBox
            {
                MinWidth = 160,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            string selectedSymbol = CountDisplaySymbol.Normalize(item.CountSymbol);
            foreach (string symbol in CountDisplaySymbol.All)
            {
                countSymbolBox.Items.Add(new ComboBoxItem
                {
                    Content = CountDisplaySymbol.Title(symbol),
                    Tag = symbol,
                });
            }
            for (int i = 0; i < countSymbolBox.Items.Count; i++)
            {
                if (countSymbolBox.Items[i] is ComboBoxItem option &&
                    option.Tag is string optionSymbol &&
                    string.Equals(optionSymbol, selectedSymbol, StringComparison.OrdinalIgnoreCase))
                {
                    countSymbolBox.SelectedIndex = i;
                    break;
                }
            }
            if (countSymbolBox.SelectedIndex < 0)
                countSymbolBox.SelectedIndex = 0;
            panel.Children.Add(countSymbolBox);
        }

        panel.Children.Add(new TextBlock { Text = "Color:", Margin = new Thickness(0, 10, 0, 4) });
        string selectedColor = NormalizeTakeoffColor(item.Color);
        var colorBox = new TextBox { Text = selectedColor, Width = 90, HorizontalAlignment = HorizontalAlignment.Left };
        var swatches = new List<Border>();
        var colorPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
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
                foreach (Border border in swatches)
                    border.BorderBrush = Brushes.Transparent;
                swatch.BorderBrush = Brushes.White;
            };
            swatches.Add(swatch);
            colorPanel.Children.Add(swatch);
        }
        panel.Children.Add(colorPanel);
        panel.Children.Add(colorBox);

        var unitPriceLabel = new TextBlock
        {
            Text = $"Unit price per {TakeoffUnitText(item)}:",
            Margin = new Thickness(0, 10, 0, 4),
        };
        var priceBox = new TextBox
        {
            Text = item.UnitPrice > 0 ? item.UnitPrice.ToString("G", CultureInfo.InvariantCulture) : "0",
        };
        if (estimatingEnabled)
        {
            panel.Children.Add(unitPriceLabel);
            panel.Children.Add(priceBox);
            joistEnabledBox.Checked += (_, _) => unitPriceLabel.Text = $"Unit price per {UnitText("line")}:";
            joistEnabledBox.Unchecked += (_, _) => unitPriceLabel.Text = $"Unit price per {UnitText(item.MeasurementType)}:";
        }

        panel.Children.Add(new TextBlock { Text = "Notes:", Margin = new Thickness(0, 10, 0, 4) });
        var notesBox = new TextBox
        {
            Text = item.Notes,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 90,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        panel.Children.Add(notesBox);

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

        string resultName = item.Name;
        string resultColor = selectedColor;
        double resultPrice = item.UnitPrice;
        string resultNotes = item.Notes;
        string resultCountSymbol = countSymbol;
        JoistTakeoffEdit resultJoist = joistEdit;

        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text))
            {
                MessageBox.Show("Name is required.", "Takeoff Item Properties",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string cleanColor = NormalizeTakeoffColor(colorBox.Text);
            if (!IsValidWpfColor(cleanColor))
            {
                MessageBox.Show("Enter a valid color like #FF4444.", "Takeoff Item Properties",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            double parsedPrice = item.UnitPrice;
            if (estimatingEnabled &&
                (!double.TryParse(priceBox.Text.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out parsedPrice) ||
                 parsedPrice < 0))
            {
                MessageBox.Show("Enter a valid non-negative unit price.", "Takeoff Item Properties",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool joistEnabled = isAreaTakeoff && joistEnabledBox.IsChecked == true;
            double joistSpacing = item.JoistSpacingInches > 0 ? item.JoistSpacingInches : 16;
            double joistDirection = item.JoistDirectionDegrees;
            string joistPitch = JoistTakeoffCalculator.NormalizePitch(item.JoistPitch);
            string joistRounding = JoistTakeoffCalculator.RoundingNone;
            if (roundingBox.SelectedItem is ComboBoxItem selectedRoundingItem &&
                selectedRoundingItem.Tag is string selectedRoundingValue)
            {
                joistRounding = JoistTakeoffCalculator.NormalizeLengthRounding(selectedRoundingValue);
            }
            bool joistDetailedLabels = joistDetailedLabelsBox.IsChecked == true;

            if (joistEnabled)
            {
                if (!double.TryParse(joistSpacingBox.Text.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out joistSpacing) ||
                    joistSpacing <= 0)
                {
                    MessageBox.Show("Enter a valid positive joist spacing.", "Takeoff Item Properties",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (!double.TryParse(joistDirectionBox.Text.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out joistDirection))
                {
                    MessageBox.Show("Enter a valid joist direction angle.", "Takeoff Item Properties",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (!JoistTakeoffCalculator.TryNormalizePitch(joistPitchBox.Text, out joistPitch))
                {
                    MessageBox.Show("Enter roof pitch as rise:run, e.g. 3:12. Leave blank for flat.",
                                    "Takeoff Item Properties",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }
            else if (isLineTakeoff && pointOcSpacingBox != null)
            {
                if (!double.TryParse(pointOcSpacingBox.Text.Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out joistSpacing) ||
                    joistSpacing <= 0)
                {
                    MessageBox.Show("Enter a valid positive Point O.C. spacing.", "Takeoff Item Properties",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                joistRounding = JoistTakeoffCalculator.NormalizeLengthRounding(item.JoistLengthRounding);
                joistDetailedLabels = item.JoistDetailedLabels;
            }
            else if (!item.IsJoistArea)
            {
                joistRounding = JoistTakeoffCalculator.NormalizeLengthRounding(item.JoistLengthRounding);
                joistDetailedLabels = item.JoistDetailedLabels;
            }

            if (isPointTakeoff &&
                countSymbolBox?.SelectedItem is ComboBoxItem { Tag: string selectedCountSymbol })
            {
                resultCountSymbol = CountDisplaySymbol.Normalize(selectedCountSymbol);
            }
            else
            {
                resultCountSymbol = CountDisplaySymbol.Normalize(item.CountSymbol);
            }

            resultName = nameBox.Text.Trim();
            resultColor = cleanColor;
            resultPrice = parsedPrice;
            resultNotes = notesBox.Text.Trim();
            resultJoist = new JoistTakeoffEdit(
                joistEnabled,
                joistTypeBox.Text.Trim(),
                joistSpacing,
                joistDirection,
                joistDirectionFollowsBox.IsChecked == true,
                joistAddEndBox.IsChecked == true,
                joistPitch,
                joistRounding,
                joistLabelsBox.IsChecked == true,
                joistDetailedLabels);
            dialog.DialogResult = true;
        };
        dialog.Loaded += (_, _) => { nameBox.Focus(); nameBox.SelectAll(); };

        bool accepted = dialog.ShowDialog() == true;
        if (accepted)
        {
            name = resultName;
            color = resultColor;
            unitPrice = resultPrice;
            notes = resultNotes;
            countSymbol = resultCountSymbol;
            joistEdit = resultJoist;
        }

        return accepted;
    }

    private static void AddLabeledTextBox(Grid grid, int row, string label, out TextBox textBox, string value)
    {
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 3, 8, 3),
        };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        textBox = new TextBox
        {
            Text = value,
            MinWidth = 190,
            Margin = new Thickness(0, 3, 0, 3),
        };
        Grid.SetRow(textBox, row);
        Grid.SetColumn(textBox, 1);
        grid.Children.Add(textBox);
    }

    private static string NormalizeTakeoffColor(string value)
    {
        string trimmed = string.IsNullOrWhiteSpace(value) ? "#FF4444" : value.Trim();
        if (!trimmed.StartsWith('#'))
            trimmed = "#" + trimmed;
        return trimmed;
    }

    private static bool IsValidWpfColor(string value)
    {
        try
        {
            _ = (Color)ColorConverter.ConvertFromString(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Brush BrushFromHex(string value, Brush fallback)
    {
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(NormalizeTakeoffColor(value)));
        }
        catch
        {
            return fallback;
        }
    }
}
