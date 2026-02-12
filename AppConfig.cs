using System.Text.Json;

namespace yShorts;

/// <summary>
/// Loads and exposes application configuration from appsettings.json.
/// </summary>
public static class AppConfig
{
    private static string _configPath = "";

    public static string GeminiApiKey { get; set; } = string.Empty;
    public static string PexelsApiKey { get; set; } = string.Empty;
    public static string FfmpegPath { get; set; } = "ffmpeg";
    public static string FfprobePath { get; set; } = "ffprobe";
    public static string EdgeTtsPath { get; set; } = "edge-tts";
    public static string OutputDir { get; set; } = "output";
    public static string TempDir { get; set; } = "temp";
    public static string TtsVoice { get; set; } = "en-US-GuyNeural";
    public static string VideoLanguage { get; set; } = "English";

    public static void Load()
    {
        var localFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "yShorts");
        Directory.CreateDirectory(localFolder);
        _configPath = Path.Combine(localFolder, "appsettings.json");

        // Migration: If old config exists in project root, move it to new location
        var oldPath = "appsettings.json";
        if (File.Exists(oldPath) && !File.Exists(_configPath))
        {
            try
            {
                File.Move(oldPath, _configPath);
                Console.WriteLine($"✅ Migrated configuration to {_configPath}");
            }
            catch { /* fallback to default */ }
        }

        if (!File.Exists(_configPath))
        {
            // Create a default config file
            Save();
            return;
        }

        var json = File.ReadAllText(_configPath);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        GeminiApiKey = GetString(root, "GeminiApiKey", GeminiApiKey);
        PexelsApiKey = GetString(root, "PexelsApiKey", PexelsApiKey);
        FfmpegPath = GetString(root, "FfmpegPath", FfmpegPath);
        FfprobePath = GetString(root, "FfprobePath", FfprobePath);
        EdgeTtsPath = GetString(root, "EdgeTtsPath", EdgeTtsPath);
        OutputDir = GetString(root, "OutputDir", OutputDir);
        TempDir = GetString(root, "TempDir", TempDir);
        TtsVoice = GetString(root, "TtsVoice", TtsVoice);
        VideoLanguage = GetString(root, "VideoLanguage", VideoLanguage);

        AutoDetectEdgeTts();
    }

    private static void AutoDetectEdgeTts()
    {
        // If exact path exists, we are good
        if (File.Exists(EdgeTtsPath)) return;

        // If it's the default "edge-tts" command, we assume checking PATH is handled dynamically by Process.Start,
        // BUT if the user is having trouble, we can try to find the absolute path.

        // Search in %LOCALAPPDATA%\Python
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var pythonRoot = Path.Combine(localAppData, "Python");

            if (Directory.Exists(pythonRoot))
            {
                // Look for */Scripts/edge-tts.exe
                foreach (var dir in Directory.EnumerateDirectories(pythonRoot))
                {
                    var scriptDir = Path.Combine(dir, "Scripts");
                    var exePath = Path.Combine(scriptDir, "edge-tts.exe");
                    if (File.Exists(exePath))
                    {
                        EdgeTtsPath = exePath;
                        return; // Found it!
                    }
                }
            }
        }
        catch { /* ignore errors */ }
    }

    public static void Save()
    {
        var config = new Dictionary<string, string>
        {
            ["GeminiApiKey"] = GeminiApiKey,
            ["PexelsApiKey"] = PexelsApiKey,
            ["FfmpegPath"] = FfmpegPath,
            ["FfprobePath"] = FfprobePath,
            ["EdgeTtsPath"] = EdgeTtsPath,
            ["OutputDir"] = OutputDir,
            ["TempDir"] = TempDir,
            ["TtsVoice"] = TtsVoice,
            ["VideoLanguage"] = VideoLanguage
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(config, options);
        File.WriteAllText(_configPath, json);
    }

    /// <summary>
    /// Returns true if both API keys are configured (non-empty and not placeholder).
    /// </summary>
    public static bool AreApiKeysConfigured()
    {
        return !string.IsNullOrWhiteSpace(GeminiApiKey) && !GeminiApiKey.Contains("YOUR_")
            && !string.IsNullOrWhiteSpace(PexelsApiKey) && !PexelsApiKey.Contains("YOUR_");
    }

    private static string GetString(JsonElement root, string key, string defaultValue)
    {
        return root.TryGetProperty(key, out var prop) ? prop.GetString() ?? defaultValue : defaultValue;
    }
}
