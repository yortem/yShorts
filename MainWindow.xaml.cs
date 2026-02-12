using System.Diagnostics;
using System.IO;
using System.Windows;
using yShorts.Models;
using yShorts.Services;

namespace yShorts;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        // Load config
        AppConfig.Load();

        _vm.AppendLog("yShorts ready. Enter a topic and click Generate.");

        // Auto-open settings if API keys are not configured
        if (!AppConfig.AreApiKeysConfigured())
        {
            _vm.AppendLog("⚠ API keys not configured — opening Settings...");
            Loaded += (_, _) =>
            {
                var settingsWin = new SettingsWindow { Owner = this };
                settingsWin.ShowDialog();
            };
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var settingsWin = new SettingsWindow { Owner = this };
        settingsWin.ShowDialog();
    }

    private async void GenerateBtn_Click(object sender, RoutedEventArgs e)
    {
        bool hasPrompt = (_vm.IsAdvancedMode && _vm.Stages.Any()) || (!_vm.IsAdvancedMode && !string.IsNullOrWhiteSpace(_vm.Topic));

        _vm.Reset();
        _vm.IsGenerating = true;

        var topicOrStages = _vm.IsAdvancedMode ? "Advanced stages" : (_vm.Topic?.Trim() ?? "Viral Short");

        // Create working directories...
        Directory.CreateDirectory(AppConfig.TempDir);
        Directory.CreateDirectory(AppConfig.OutputDir);

        var safeFileName = string.Join("_", topicOrStages.Split(Path.GetInvalidFileNameChars()))
            .Replace(" ", "_")
            .ToLowerInvariant();
        if (safeFileName.Length > 40) safeFileName = safeFileName[..40];
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(5);
            var aiService = new AIService(httpClient);
            var mediaService = new MediaService(httpClient);
            ScriptResult scriptResult = new ScriptResult();

            // ── Step 1: AI Script ── (Only if we have a prompt)
            if (hasPrompt)
            {
                _vm.SetStep(0, "Generating script with Gemini AI...", 5);
                Log("🤖 Calling Gemini API...");

                if (_vm.IsAdvancedMode)
                {
                    scriptResult = await Task.Run(() => aiService.GenerateScriptAdvancedAsync(_vm.Stages.ToList()));
                }
                else
                {
                    var topic = _vm.Topic.Trim();
                    scriptResult = await Task.Run(() => aiService.GenerateScriptAsync(topic));
                }

                Log($"✅ Script generated ({scriptResult.Script.Length} chars)");
                _vm.SeoTitle = scriptResult.SeoTitle;
                _vm.SeoDescription = scriptResult.SeoDescription;
            }
            else
            {
                Log("📽️ No prompt provided. Generating a visual-only short.");
            }

            // ── Step 2: Download Videos (Multi-stage or Normal) ──
            _vm.SetStep(1, "Preparing video clips...", 20);
            List<string> clipPaths = new List<string>();

            if (_vm.IsAdvancedMode)
            {
                // In advanced mode, we need one clip per stage
                for (int i = 0; i < _vm.Stages.Count; i++)
                {
                    var stage = _vm.Stages[i];
                    if (stage.HasLocalVideo)
                    {
                        clipPaths.Add(stage.LocalVideoPath);
                    }
                    else if (!string.IsNullOrEmpty(stage.VisualSearch))
                    {
                        Log($"🎬 Searching Pexels for stage {i + 1}: {stage.VisualSearch}...");
                        var downloaded = await Task.Run(() => mediaService.SearchAndDownloadVideosAsync(new List<string> { stage.VisualSearch }, AppConfig.TempDir, $"stage_{i}"));
                        if (downloaded.Any()) clipPaths.Add(downloaded.First());
                    }
                }
            }
            else if (_vm.LocalClips.Any())
            {
                Log($"🎬 Using {_vm.LocalClips.Count} manually selected clips");
                clipPaths = _vm.LocalClips.Select(c => c.FilePath).ToList();
            }
            else
            {
                Log("🎬 Searching Pexels for stock clips...");
                var keywords = scriptResult.Keywords.Any() ? scriptResult.Keywords : new List<string> { topicOrStages };
                clipPaths = await Task.Run(() => mediaService.SearchAndDownloadVideosAsync(keywords, AppConfig.TempDir));
            }

            if (!clipPaths.Any()) throw new Exception("No video clips available.");

            // ── Step 3: Generate TTS Audio ──
            string audioPath = "";
            double audioDuration = 15.0; // Default if silent

            if (hasPrompt && !string.IsNullOrWhiteSpace(scriptResult.Script))
            {
                _vm.SetStep(2, "Generating voiceover...", 40);
                audioPath = Path.Combine(AppConfig.TempDir, "voiceover.mp3");
                await Task.Run(() => mediaService.GenerateTtsAsync(scriptResult.Script, audioPath));
                audioDuration = await Task.Run(() => mediaService.GetAudioDurationAsync(audioPath));
                Log($"⏱️ Audio duration: {audioDuration:F1}s");
            }
            else
            {
                Log("🔇 Skipping voiceover (silent video).");
            }

            // ── Step 4: Generate Subtitles ──
            string subtitlePath = "";
            if (!string.IsNullOrEmpty(audioPath))
            {
                _vm.SetStep(3, "Generating subtitles...", 55);
                subtitlePath = Path.Combine(AppConfig.TempDir, "subtitles.srt");
                var subtitleService = new SubtitleService();
                subtitleService.GenerateSrt(scriptResult.Script, audioDuration, subtitlePath);
            }

            // ── Step 5: Render Final Video ──
            _vm.SetStep(4, "Rendering final video...", 65);
            var outputPath = Path.Combine(AppConfig.OutputDir, $"{safeFileName}_{timestamp}.mp4");

            var project = new VideoProject
            {
                Topic = _vm.Topic ?? "yShorts Video",
                Script = scriptResult.Script,
                VideoClipPaths = clipPaths,
                AudioPath = audioPath,
                SubtitlePath = subtitlePath,
                OutputPath = outputPath,
                AudioDurationSeconds = audioDuration,
                StageScripts = scriptResult.StageScripts,
                Stages = _vm.IsAdvancedMode ? _vm.Stages.ToList() : new List<VideoStage>()
            };

            var videoEngine = new VideoEngine();
            var progress = new Progress<double>(p =>
            {
                _vm.ProgressValue = 65 + (p * 0.3);
            });

            await Task.Run(() => videoEngine.BuildVideoAsync(project, progress));

            // ── Step 6: Done ──
            Log("✅ Video complete: " + Path.GetFullPath(outputPath));
            _vm.SetComplete(Path.GetFullPath(outputPath));
        }
        catch (Exception ex)
        {
            Log($"❌ ERROR: {ex.Message}");
            if (ex.InnerException != null)
                Log($"   Inner: {ex.InnerException.Message}");
            Log(ex.StackTrace ?? "");

            _vm.IsGenerating = false;
            _vm.CurrentStep = "Failed — see log for details";
        }
    }

    private async void AddLocalClips_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Filter = "Video files|*.mp4;*.mov;*.avi;*.mkv|All files|*.*"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            // Disable generate while processing thumbnails
            _vm.IsGenerating = true;
            _vm.CurrentStep = "Generating thumbnails...";

            foreach (var file in openFileDialog.FileNames)
            {
                if (!_vm.LocalClips.Any(c => c.FilePath == file))
                {
                    try
                    {
                        var thumb = await GenerateThumbnailAsync(file);
                        _vm.LocalClips.Add(new yShorts.Models.LocalClip
                        {
                            FilePath = file,
                            Thumbnail = thumb
                        });
                    }
                    catch (Exception ex)
                    {
                        Log($"  ⚠️ Thumbnail failed for {Path.GetFileName(file)}: {ex.Message}");
                        _vm.LocalClips.Add(new yShorts.Models.LocalClip { FilePath = file });
                    }
                }
            }

            _vm.IsGenerating = false;
            _vm.CurrentStep = "";
            Log($"📂 Added {openFileDialog.FileNames.Length} local clips. (Total: {_vm.LocalClips.Count})");
        }
    }

    private void RemoveClip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is yShorts.Models.LocalClip clip)
        {
            _vm.RemoveClip(clip);
        }
    }

    private async Task<System.Windows.Media.Imaging.BitmapSource?> GenerateThumbnailAsync(string videoPath)
    {
        var thumbPath = Path.Combine(AppConfig.TempDir, $"thumb_{Guid.NewGuid()}.jpg");
        Directory.CreateDirectory(AppConfig.TempDir);

        // FFmpeg: take 1 frame at start
        var args = $"-i \"{videoPath}\" -vframes 1 -q:v 2 \"{thumbPath}\"";

        var processInfo = new ProcessStartInfo
        {
            FileName = AppConfig.FfmpegPath,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process != null) await process.WaitForExitAsync();

        if (File.Exists(thumbPath))
        {
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();

            // Safer way to load and delete immediately:
            using (var stream = new FileStream(thumbPath, FileMode.Open, FileAccess.Read))
            {
                bitmap.BeginInit();
                bitmap.StreamSource = stream;
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.EndInit();
            }

            bitmap.Freeze();

            try { File.Delete(thumbPath); } catch { /* ignore */ }
            return bitmap;
        }
        return null;
    }



    private void ClearLocalClips_Click(object sender, RoutedEventArgs e)
    {
        _vm.LocalClips.Clear();
        Log("🗑️ Manual clips cleared.");
    }

    private void AddStage_Click(object sender, RoutedEventArgs e)
    {
        _vm.AddStage();
    }

    private void RemoveStage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is yShorts.Models.VideoStage stage)
        {
            _vm.RemoveStage(stage);
        }
    }

    private void SelectStageVideo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is yShorts.Models.VideoStage stage)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Video files|*.mp4;*.mov;*.avi;*.mkv|All files|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                stage.LocalVideoPath = openFileDialog.FileName;
                _vm.AppendLog($"📁 Attached local video for stage: {Path.GetFileName(openFileDialog.FileName)}");
            }
        }
    }

    private void ClearStageVideo_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is yShorts.Models.VideoStage stage)
        {
            stage.LocalVideoPath = "";
        }
    }

    private void Log(string message)
    {
        Dispatcher.Invoke(() =>
        {
            _vm.AppendLog($"[{DateTime.Now:HH:mm:ss}] {message}");
            LogScroller.ScrollToBottom();
        });
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        _vm.LogText = "";
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_vm.LogText))
        {
            Clipboard.SetText(_vm.LogText);
            _vm.AppendLog("📋 Log copied to clipboard!");
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_vm.OutputFilePath))
        {
            var dir = Path.GetDirectoryName(_vm.OutputFilePath);
            if (dir != null && Directory.Exists(dir))
            {
                Process.Start("explorer.exe", dir);
            }
        }
    }

    private void PlayVideo_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_vm.OutputFilePath) && File.Exists(_vm.OutputFilePath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _vm.OutputFilePath,
                UseShellExecute = true
            });
        }
    }

    private void CopyTitle_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_vm.SeoTitle))
        {
            Clipboard.SetText(_vm.SeoTitle);
            _vm.AppendLog("📋 Title copied!");
        }
    }

    private void CopyDescription_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_vm.SeoDescription))
        {
            Clipboard.SetText(_vm.SeoDescription);
            _vm.AppendLog("📋 Description copied!");
        }
    }
}
