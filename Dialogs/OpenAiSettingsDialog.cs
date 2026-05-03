using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SmartTakeoffs.Controls;

public sealed class OpenAiSettingsDialog : Window
{
    private readonly AppSettings _settings;
    private readonly TextBlock _keyStatusText;
    private readonly TextBlock _keySourceText;
    private readonly TextBlock _modelSourceText;
    private readonly PasswordBox _keyBox;
    private readonly ComboBox _modelBox;

    public string SelectedModel { get; private set; }

    public OpenAiSettingsDialog(AppSettings settings)
    {
        _settings = settings;
        SelectedModel = AppSettingsStore.NormalizeOpenAiModel(settings.OpenAiModel);

        Title = "OpenAI Settings";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var root = new StackPanel { Margin = new Thickness(14) };

        root.Children.Add(new TextBlock
        {
            Text = "API key",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6),
        });

        _keyStatusText = new TextBlock { Margin = new Thickness(0, 0, 0, 2) };
        _keySourceText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        _keySourceText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryForegroundBrush");
        root.Children.Add(_keyStatusText);
        root.Children.Add(_keySourceText);

        root.Children.Add(new TextBlock
        {
            Text = "Paste a new key only if you want to save or replace the Windows user environment key.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
        });
        _keyBox = new PasswordBox { Margin = new Thickness(0, 0, 0, 6) };
        root.Children.Add(_keyBox);

        var keyButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 14),
        };
        var saveKey = new Button { Content = "Save Key To User Env", MinWidth = 150, Margin = new Thickness(0, 0, 6, 0) };
        var clearUserKey = new Button { Content = "Clear User Env Key", MinWidth = 128 };
        keyButtons.Children.Add(saveKey);
        keyButtons.Children.Add(clearUserKey);
        root.Children.Add(keyButtons);

        root.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 12) });

        root.Children.Add(new TextBlock
        {
            Text = "Model",
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6),
        });
        _modelBox = new ComboBox
        {
            IsEditable = true,
            ItemsSource = AppSettingsStore.SuggestedOpenAiModels,
            Text = SelectedModel,
            Margin = new Thickness(0, 0, 0, 4),
        };
        root.Children.Add(_modelBox);

        _modelSourceText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        };
        _modelSourceText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryForegroundBrush");
        root.Children.Add(_modelSourceText);

        var settingsPathText = new TextBlock
        {
            Text = $"Model setting path: {AppSettingsStore.SettingsPath}",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        };
        settingsPathText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryForegroundBrush");
        root.Children.Add(settingsPathText);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var ok = new Button { Content = "Save", Width = 78, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 78, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        Content = root;

        saveKey.Click += (_, _) => SaveKeyToUserEnvironment();
        clearUserKey.Click += (_, _) => ClearUserEnvironmentKey();
        ok.Click += (_, _) => SaveModelAndClose();
        Loaded += (_, _) =>
        {
            RefreshStatus();
            _modelBox.Focus();
        };
    }

    private void RefreshStatus()
    {
        OpenAiKeyStatus keyStatus = AppSettingsStore.GetOpenAiKeyStatus();
        _keyStatusText.Text = keyStatus.Found ? "Key status: found" : "Key status: missing";
        _keyStatusText.Foreground = keyStatus.Found
            ? new SolidColorBrush(Color.FromRgb(36, 126, 70))
            : new SolidColorBrush(Color.FromRgb(180, 62, 48));
        _keySourceText.Text =
            $"Source: {keyStatus.Source}. {keyStatus.Description} Existing secrets are never shown.";

        OpenAiModelStatus modelStatus = AppSettingsStore.GetOpenAiModelStatus(_settings);
        _modelSourceText.Text =
            $"Runtime model source: {modelStatus.Source}. Effective model now: {modelStatus.Model}.";
    }

    private void SaveKeyToUserEnvironment()
    {
        string key = _keyBox.Password.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            MessageBox.Show(
                "Paste an OpenAI API key first.",
                "OpenAI Settings",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            AppSettingsStore.SaveOpenAiKeyToUserEnvironment(key);
            _keyBox.Clear();
            RefreshStatus();
            MessageBox.Show(
                "OpenAI API key was saved to the Windows user environment. It was also loaded into this running process.",
                "OpenAI Settings",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not save OpenAI API key:\n{ex.Message}",
                "OpenAI Settings",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ClearUserEnvironmentKey()
    {
        if (MessageBox.Show(
                "Clear the Windows user environment OPENAI_API_KEY value?\n\nA process-level key, if present, will still be used until the app is restarted.",
                "OpenAI Settings",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            AppSettingsStore.ClearOpenAiUserEnvironmentKey();
            RefreshStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not clear OpenAI API key:\n{ex.Message}",
                "OpenAI Settings",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void SaveModelAndClose()
    {
        string model = AppSettingsStore.NormalizeOpenAiModel(_modelBox.Text);
        SelectedModel = model;
        DialogResult = true;
    }
}
