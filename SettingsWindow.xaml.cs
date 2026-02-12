using System.Windows;

namespace yShorts;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        LoadCurrentSettings();
    }

    private void LoadCurrentSettings()
    {
        GeminiKeyBox.Text = AppConfig.GeminiApiKey;
        PexelsKeyBox.Text = AppConfig.PexelsApiKey;
        FfmpegPathBox.Text = AppConfig.FfmpegPath;
        FfprobePathBox.Text = AppConfig.FfprobePath;
        EdgeTtsPathBox.Text = AppConfig.EdgeTtsPath;
        OutputDirBox.Text = AppConfig.OutputDir;
        TtsVoiceBox.Text = AppConfig.TtsVoice;

        // Set Language
        LanguageBox.SelectedIndex = AppConfig.VideoLanguage switch
        {
            "Hebrew" => 1,
            _ => 0
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        AppConfig.GeminiApiKey = GeminiKeyBox.Text.Trim();
        AppConfig.PexelsApiKey = PexelsKeyBox.Text.Trim();
        AppConfig.FfmpegPath = FfmpegPathBox.Text.Trim();
        AppConfig.FfprobePath = FfprobePathBox.Text.Trim();
        AppConfig.EdgeTtsPath = EdgeTtsPathBox.Text.Trim();
        AppConfig.OutputDir = OutputDirBox.Text.Trim();
        AppConfig.TtsVoice = TtsVoiceBox.Text.Trim();

        if (LanguageBox.SelectedItem is System.Windows.Controls.ComboBoxItem item)
        {
            AppConfig.VideoLanguage = item.Content.ToString() ?? "English";
        }

        AppConfig.Save();

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void LanguageBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (TtsVoiceBox == null) return;

        // Auto-switch voice if the user hasn't typed a custom one (or if it matches the other default)
        var currentVoice = TtsVoiceBox.Text.Trim();
        var isDefaultEn = currentVoice == "en-US-GuyNeural";
        var isDefaultHe = currentVoice == "he-IL-AvriNeural";
        var isEffectivelyEmpty = string.IsNullOrWhiteSpace(currentVoice);

        if (LanguageBox.SelectedItem is System.Windows.Controls.ComboBoxItem item)
        {
            var lang = item.Content.ToString();
            if (lang == "Hebrew")
            {
                if (isDefaultEn || isEffectivelyEmpty)
                    TtsVoiceBox.Text = "he-IL-AvriNeural";
            }
            else // English
            {
                if (isDefaultHe || isEffectivelyEmpty)
                    TtsVoiceBox.Text = "en-US-GuyNeural";
            }
        }
    }
}
