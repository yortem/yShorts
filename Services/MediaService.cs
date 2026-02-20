using System.Net.Http;
using System.Text.Json;

namespace yShorts.Services;

/// <summary>
/// Handles downloading stock footage from Pexels and generating TTS audio via Edge-TTS.
/// </summary>
public class MediaService
{
    private readonly HttpClient _http;

    public MediaService(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Searches Pexels for vertical videos matching the keywords and downloads 3-5 clips.
    /// </summary>
    public async Task<List<string>> SearchAndDownloadVideosAsync(List<string> keywords, string outputDir, string filePrefix = "clip")
    {
        Console.WriteLine("\n🎬 [Media] Searching Pexels for stock footage...");
        Directory.CreateDirectory(outputDir);

        var downloadedPaths = new List<string>();
        var usedVideoIds = new HashSet<int>();

        foreach (var keyword in keywords)
        {
            if (downloadedPaths.Count >= 5) break;

            try
            {
                var url = $"https://api.pexels.com/videos/search?query={Uri.EscapeDataString(keyword)}&orientation=portrait&size=medium&per_page=3";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Authorization", AppConfig.PexelsApiKey);

                var response = await _http.SendAsync(request);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"  ⚠️ Pexels search failed for \"{keyword}\": {response.StatusCode}");
                    continue;
                }

                var doc = JsonDocument.Parse(responseBody);
                var videos = doc.RootElement.GetProperty("videos");

                foreach (var video in videos.EnumerateArray())
                {
                    if (downloadedPaths.Count >= 5) break;

                    var videoId = video.GetProperty("id").GetInt32();
                    if (usedVideoIds.Contains(videoId)) continue;

                    // Find the best video file (prefer HD quality)
                    string? downloadUrl = null;
                    int bestWidth = 0;

                    foreach (var file in video.GetProperty("video_files").EnumerateArray())
                    {
                        var width = file.TryGetProperty("width", out var w) ? w.GetInt32() : 0;
                        var height = file.TryGetProperty("height", out var h) ? h.GetInt32() : 0;

                        // Prefer vertical or square videos, HD quality
                        if (height >= width && width >= 720 && width <= 1920)
                        {
                            if (width > bestWidth)
                            {
                                bestWidth = width;
                                downloadUrl = file.GetProperty("link").GetString();
                            }
                        }
                    }

                    // Fallback: take any file if no vertical found
                    if (downloadUrl == null)
                    {
                        foreach (var file in video.GetProperty("video_files").EnumerateArray())
                        {
                            var width = file.TryGetProperty("width", out var w) ? w.GetInt32() : 0;
                            if (width >= 720)
                            {
                                downloadUrl = file.GetProperty("link").GetString();
                                break;
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(downloadUrl)) continue;

                    // Download the video
                    var filePath = Path.Combine(outputDir, $"{filePrefix}_{downloadedPaths.Count + 1}.mp4");
                    Console.WriteLine($"  📥 Downloading clip for \"{keyword}\" → {filePath}");

                    var videoBytes = await _http.GetByteArrayAsync(downloadUrl);
                    await File.WriteAllBytesAsync(filePath, videoBytes);

                    downloadedPaths.Add(filePath);
                    usedVideoIds.Add(videoId);
                    break; // One clip per keyword
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ⚠️ Error searching for \"{keyword}\": {ex.Message}");
            }
        }

        if (downloadedPaths.Count == 0)
        {
            throw new Exception("No videos could be downloaded from Pexels. Check your API key and keywords.");
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[Media] ✅ Downloaded {downloadedPaths.Count} video clips");
        Console.ResetColor();

        return downloadedPaths;
    }

    /// <summary>
    /// Generates TTS audio from the script using Edge-TTS CLI.
    /// </summary>
    public async Task GenerateTtsAsync(string text, string outputPath)
    {
        Console.WriteLine("\n🔊 [TTS] Generating voiceover audio...");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var voice = AppConfig.TtsVoice;
        // Escape double quotes in text for the command line
        var escapedText = text.Replace("\"", "\\\"");

        var args = $"--voice \"{voice}\" --text \"{escapedText}\" --rate=+15% --pitch=+2Hz --write-media \"{outputPath}\"";

        var processInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = AppConfig.EdgeTtsPath,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(processInfo)
            ?? throw new Exception("Failed to start edge-tts process");

        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[TTS] edge-tts error: {stderr}");
            Console.ResetColor();
            throw new Exception($"edge-tts failed with exit code {process.ExitCode}");
        }

        if (!File.Exists(outputPath))
        {
            throw new Exception($"TTS output file not created at: {outputPath}");
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[TTS] ✅ Audio generated: {outputPath}");
        Console.ResetColor();
    }

    /// <summary>
    /// Gets the duration of an audio or video file in seconds using FFprobe.
    /// </summary>
    public async Task<double> GetMediaDurationAsync(string mediaPath)
    {
        var args = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{mediaPath}\"";

        var processInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = AppConfig.FfprobePath,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(processInfo)
            ?? throw new Exception("Failed to start ffprobe");

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (double.TryParse(output.Trim(), System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var duration))
        {
            return duration;
        }

        // Fallback for some files
        return 5.0;
    }
}
