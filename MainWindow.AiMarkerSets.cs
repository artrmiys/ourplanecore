using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OurPlaneCore.Controls;
using SkiaSharp;


namespace OurPlaneCore;

public partial class MainWindow
{
    // AI marker set creation, filtering, export, and dialog helpers.

    private void CreateMarkerSetFromCurrentFilter()
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before creating marker sets.";
            return;
        }

        List<SmartAiMarker> markers = LoadMarkersForCurrentFilter(includeHiddenTypes: true);
        if (markers.Count == 0)
        {
            PostStatusInfo("No active AI markers match the current type/sample filters.");
            return;
        }

        MarkerSetInput? input = ShowMarkerSetDialog(DefaultMarkerSetName(markers), markers.Count);
        if (input == null)
            return;

        SmartAiMarkerSet set = SmartContextStore.SaveAiMarkerSet(
            _currentJob,
            input.Name,
            input.Description,
            SelectedMarkerTypeFilterForMarkers(),
            SelectedMarkerSampleFilter(),
            markers);
        TxtStatus.Text = $"Saved marker set '{set.Name}' with {set.MarkerCount} markers.";
    }

    private bool CanManageMarkerSets() =>
        _currentJob != null && SmartContextStore.LoadAiMarkerSets(_currentJob).Count > 0;

    private void ManageMarkerSets()
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before managing marker sets.";
            return;
        }

        IReadOnlyList<SmartAiMarkerSet> sets = SmartContextStore.LoadAiMarkerSets(_currentJob);
        if (sets.Count == 0)
        {
            PostStatusInfo("Create a marker set from the current marker filters first.");
            return;
        }

        var dialog = new MarkerSetsDialog(sets) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Result == null)
            return;

        MarkerSetsDialogResult result = dialog.Result;
        SmartAiMarkerSet set = result.MarkerSet;
        try
        {
            switch (result.Action)
            {
                case MarkerSetsDialogAction.Apply:
                    ApplyMarkerSetFilter(set);
                    TxtStatus.Text = $"Applied marker set '{set.Name}' ({set.MarkerCount} markers).";
                    break;

                case MarkerSetsDialogAction.Rename:
                    set.Name = result.Name;
                    set.Description = result.Description;
                    SmartContextStore.SaveAiMarkerSet(_currentJob, set);
                    TxtStatus.Text = $"Renamed marker set to '{set.Name}'.";
                    break;

                case MarkerSetsDialogAction.Delete:
                    DeleteMarkerSet(set);
                    break;

                case MarkerSetsDialogAction.OpenJson:
                    OpenJsonFile(
                        SmartContextStore.AiMarkerSetPath(_currentJob, set.Id),
                        "Marker set JSON is missing.");
                    break;
            }
        }
        catch (Exception ex)
        {
            ShowOperationError("Marker Sets", ex);
        }
    }

    private void ApplyMarkerSetFilter(SmartAiMarkerSet set)
    {
        string typeFilter = string.IsNullOrWhiteSpace(set.TypeFilter)
            ? MarkerTypeFilterAllMarkers
            : set.TypeFilter.Trim();
        string sampleFilter = string.IsNullOrWhiteSpace(set.SampleKindFilter)
            ? MarkerSampleFilterAny
            : set.SampleKindFilter.Trim();

        if (string.Equals(typeFilter, MarkerTypeFilterAllInbox, StringComparison.OrdinalIgnoreCase))
            typeFilter = MarkerTypeFilterAllMarkers;

        SelectComboValue(ComboMarkerTypeFilter, typeFilter);
        SelectComboValue(ComboMarkerSampleFilter, sampleFilter);

        if (!string.Equals(typeFilter, MarkerTypeFilterAllMarkers, StringComparison.OrdinalIgnoreCase) &&
            _hiddenAiMarkerTypes.Remove(typeFilter))
        {
            SavePersistedMarkerVisibility();
        }

        LoadObservationsInbox();
        RefreshAiMarkersOverlay();
    }

    private void DeleteMarkerSet(SmartAiMarkerSet set)
    {
        if (_currentJob == null)
            return;

        MessageBoxResult answer = MessageBox.Show(
            $"Delete marker set '{set.Name}'?\n\nThis only removes the saved set file. Marker JSON and crop evidence stay in AI_Context.",
            "Delete Marker Set",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        bool deleted = SmartContextStore.DeleteAiMarkerSet(_currentJob, set.Id);
        TxtStatus.Text = deleted
            ? $"Deleted marker set '{set.Name}'."
            : "Marker set JSON was already missing.";
    }

    private static void SelectComboValue(ComboBox combo, string value)
    {
        foreach (object item in combo.Items)
        {
            if (string.Equals(item?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        combo.Items.Add(value);
        combo.SelectedItem = value;
    }

    private void ExportMarkersContext(bool openAfterExport)
    {
        if (_currentJob == null)
        {
            TxtStatus.Text = "Open a job before exporting marker context.";
            return;
        }

        List<SmartAiMarker> markers = LoadMarkersForCurrentFilter(includeHiddenTypes: false);
        if (markers.Count == 0)
        {
            PostStatusInfo("No visible AI markers match the current type/sample filters.");
            return;
        }

        string path = SmartContextStore.ExportAiMarkersContext(
            _currentJob,
            markers,
            _hiddenAiMarkerTypes.ToList(),
            SelectedMarkerTypeFilterForMarkers(),
            SelectedMarkerSampleFilter());

        string relativePath = Path.GetRelativePath(_currentJob.RootPath, path);
        TxtStatus.Text = $"Exported {markers.Count} AI markers -> {relativePath}";
        if (openAfterExport)
            OpenJsonFile(path, "AI marker context export is missing.");
    }

    private List<SmartAiMarker> LoadMarkersForCurrentFilter(bool includeHiddenTypes)
    {
        if (_currentJob == null)
            return [];

        return SmartContextStore.LoadAiMarkers(_currentJob)
            .Where(MarkerMatchesCurrentFilters)
            .Where(marker => includeHiddenTypes || !_hiddenAiMarkerTypes.Contains(marker.Type))
            .OrderBy(marker => marker.Type, StringComparer.OrdinalIgnoreCase)
            .ThenBy(marker => marker.SampleKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(marker => marker.Page, StringComparer.OrdinalIgnoreCase)
            .ThenBy(marker => marker.CreatedAtUtc)
            .ToList();
    }

    private bool MarkerMatchesCurrentFilters(SmartAiMarker marker)
    {
        string typeFilter = SelectedMarkerTypeFilter();
        bool typeMatches =
            string.Equals(typeFilter, MarkerTypeFilterAllInbox, StringComparison.Ordinal) ||
            string.Equals(typeFilter, MarkerTypeFilterAllMarkers, StringComparison.Ordinal) ||
            string.Equals(marker.Type, typeFilter, StringComparison.OrdinalIgnoreCase);
        if (!typeMatches)
            return false;

        string sampleFilter = SelectedMarkerSampleFilter();
        return string.Equals(sampleFilter, MarkerSampleFilterAny, StringComparison.Ordinal) ||
               string.Equals(marker.SampleKind, sampleFilter, StringComparison.OrdinalIgnoreCase);
    }

    private string SelectedMarkerTypeFilterForMarkers()
    {
        string typeFilter = SelectedMarkerTypeFilter();
        return string.Equals(typeFilter, MarkerTypeFilterAllInbox, StringComparison.Ordinal)
            ? MarkerTypeFilterAllMarkers
            : typeFilter;
    }

    private string DefaultMarkerSetName(IReadOnlyCollection<SmartAiMarker> markers)
    {
        string typeFilter = SelectedMarkerTypeFilterForMarkers();
        string sampleFilter = SelectedMarkerSampleFilter();
        string typeLabel = string.Equals(typeFilter, MarkerTypeFilterAllMarkers, StringComparison.Ordinal)
            ? "All Markers"
            : typeFilter;
        if (!string.Equals(sampleFilter, MarkerSampleFilterAny, StringComparison.Ordinal))
            typeLabel += $" - {sampleFilter}";
        return $"{typeLabel} ({markers.Count})";
    }

    private MarkerSetInput? ShowMarkerSetDialog(string defaultName, int markerCount)
    {
        var win = new Window
        {
            Title = "Create Marker Set",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = this,
        };

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Markers: {markerCount}   Type: {SelectedMarkerTypeFilterForMarkers()}   Sample: {SelectedMarkerSampleFilter()}",
            Margin = new Thickness(0, 0, 0, 10),
            FontWeight = FontWeights.Normal,
        });

        panel.Children.Add(new TextBlock { Text = "Set name", Margin = new Thickness(0, 0, 0, 4) });
        var nameBox = new TextBox { Text = defaultName, Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(nameBox);

        panel.Children.Add(new TextBlock { Text = "Description / use", Margin = new Thickness(0, 0, 0, 4) });
        var descriptionBox = new TextBox
        {
            Height = 76,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 8),
        };
        panel.Children.Add(descriptionBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var ok = new Button { Content = "Save", Width = 78, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 78, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        win.Content = panel;
        MarkerSetInput? result = null;
        ok.Click += (_, _) =>
        {
            string name = nameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Marker set name is required.", "Create Marker Set",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            result = new MarkerSetInput(name, descriptionBox.Text.Trim());
            win.DialogResult = true;
        };
        win.Loaded += (_, _) => nameBox.Focus();

        return win.ShowDialog() == true ? result : null;
    }
}
