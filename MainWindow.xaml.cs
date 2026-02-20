using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using yShorts.Models;
using yShorts.Services;

namespace yShorts;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private System.Windows.Threading.DispatcherTimer _timer;
    private bool _isDraggingSlider = false;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        // Load config
        AppConfig.Load();

        _timer = new System.Windows.Threading.DispatcherTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(50); // Faster for smoother seeking
        _timer.Tick += Timer_Tick;

        _vm.AppendLog("yShorts ready. Enter a topic and click Generate.");

        // Global Keyboard Shortcuts
        PreviewKeyDown += MainWindow_KeyDown;

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

        // Save Style Studio choices
        AppConfig.Mood = _vm.SelectedMood;
        AppConfig.SubtitleStyle = _vm.SelectedSubtitleStyle;
        AppConfig.Save();

        _vm.Reset();
        _vm.IsGenerating = true;
        MainTabs.SelectedIndex = 2; // Switch to Activity Log tab

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
                audioDuration = await Task.Run(() => mediaService.GetMediaDurationAsync(audioPath));
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

    // ── Manual Cut Handlers ──

    private void ManualVideo_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void ManualVideo_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0)
            {
                LoadManualVideo(files[0]);
            }
        }
    }

    private void ManualVideo_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Video files|*.mp4;*.mov;*.avi;*.mkv|All files|*.*"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            LoadManualVideo(openFileDialog.FileName);
        }
    }

    private async void LoadManualVideo(string filePath)
    {
        _vm.ManualVideoPath = filePath;
        _vm.IsManualVideoLoaded = true;
        _vm.AppendLog($"📂 Loaded manual video: {Path.GetFileName(filePath)}");

        try
        {
            ManualPreview.Source = new Uri(filePath);
            ManualPreview.Play();
            ManualPreview.Pause();

            var thumb = await GenerateThumbnailAsync(filePath);
            _vm.ManualVideoThumbnail = thumb;

            var mediaService = new MediaService(new HttpClient());
            var duration = await mediaService.GetMediaDurationAsync(filePath);
            _vm.ManualVideoDuration = duration;
            _vm.ManualEndTime = duration;
            _vm.ManualStartTime = 0;
            
            _vm.AppendLog($"⏱️ Duration: {duration:F1}s");
            UpdateCropFrameSize();
        }
        catch (Exception ex)
        {
            Log($"⚠️ Failed to load video info: {ex.Message}");
        }
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (!_isDraggingSlider && ManualPreview.NaturalDuration.HasTimeSpan)
        {
            TimelineSlider.Value = ManualPreview.Position.TotalSeconds;
            UpdateTimeStatus();
        }
    }

    private void UpdateTimeStatus()
    {
        if (ManualPreview.NaturalDuration.HasTimeSpan)
        {
            TimeStatus.Text = string.Format("{0:mm\\:ss} / {1:mm\\:ss}", 
                ManualPreview.Position, 
                ManualPreview.NaturalDuration.TimeSpan);
        }
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsPlaying)
        {
            ManualPreview.Pause();
            _vm.IsPlaying = false;
        }
        else
        {
            ManualPreview.Play();
            _vm.IsPlaying = true;
            _timer.Start();
        }
        e.Handled = true;
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        ManualPreview.Stop();
        _timer.Stop();
        _vm.IsPlaying = false;
        TimelineSlider.Value = 0;
        e.Handled = true;
    }

    private void ManualPreview_MediaOpened(object sender, RoutedEventArgs e)
    {
        UpdateTimeStatus();
        UpdateCropFrameSize();
    }

    private void ManualPreview_MediaEnded(object sender, RoutedEventArgs e)
    {
        ManualPreview.Stop();
        _timer.Stop();
        _vm.IsPlaying = false;
    }

    private void TimelineSlider_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        TimelineHitArea.CaptureMouse();
        _isDraggingSlider = true;
        SeekToMouse(e);
        e.Handled = true;
    }

    private void TimelineSlider_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDraggingSlider && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            SeekToMouse(e);
            e.Handled = true;
        }
    }

    private void TimelineSlider_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_isDraggingSlider)
        {
            TimelineHitArea.ReleaseMouseCapture();
            _isDraggingSlider = false;
            e.Handled = true;
        }
    }

    private void SeekToMouse(System.Windows.Input.MouseEventArgs e)
    {
        if (TimelineHitArea.ActualWidth <= 0) return;
        var point = e.GetPosition(TimelineHitArea);
        var ratio = Math.Clamp(point.X / TimelineHitArea.ActualWidth, 0, 1);
        var time = ratio * _vm.ManualVideoDuration;
        ManualPreview.Position = TimeSpan.FromSeconds(time);
        TimelineSlider.Value = time;
        UpdateTimeStatus();
    }

    private void TimelineSlider_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (TimelineHitArea.ActualWidth <= 0) return;
        var point = e.GetPosition(TimelineHitArea);
        var ratio = point.X / TimelineHitArea.ActualWidth;
        var time = Math.Round(ratio * _vm.ManualVideoDuration, 2);

        if (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.LeftShift) || 
            System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.RightShift))
        {
            _vm.ManualEndTime = time;
        }
        else
        {
            _vm.ManualStartTime = time;
        }
        UpdateMarkerPositions();
        e.Handled = true;
    }

    private void TimelineSlider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
    {
        _isDraggingSlider = true;
    }

    private void TimelineSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _isDraggingSlider = false;
        ManualPreview.Position = TimeSpan.FromSeconds(TimelineSlider.Value);
    }

    private void TimelineSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isDraggingSlider && !TimelineHitArea.IsMouseCaptured)
        {
            ManualPreview.Position = TimeSpan.FromSeconds(TimelineSlider.Value);
            UpdateTimeStatus();
        }
    }

    private void SetStart_Click(object sender, RoutedEventArgs e)
    {
        _vm.ManualStartTime = Math.Round(ManualPreview.Position.TotalSeconds, 2);
        UpdateMarkerPositions();
        e.Handled = true;
    }

    private void SetEnd_Click(object sender, RoutedEventArgs e)
    {
        _vm.ManualEndTime = Math.Round(ManualPreview.Position.TotalSeconds, 2);
        UpdateMarkerPositions();
        e.Handled = true;
    }

    private void UpdateMarkerPositions()
    {
        if (TimelineHitArea.ActualWidth <= 0) return;
        Canvas.SetLeft(StartMarker, _vm.StartMarkerPos * TimelineHitArea.ActualWidth);
        Canvas.SetLeft(EndMarker, _vm.EndMarkerPos * TimelineHitArea.ActualWidth);
    }

    private void PreviewContainer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCropFrameSize();
        UpdateMarkerPositions();
    }

    private void UpdateCropFrameSize()
    {
        if (ManualPreview.NaturalVideoHeight <= 0) return;

        double containerWidth = PreviewContainer.ActualWidth;
        double containerHeight = PreviewContainer.ActualHeight;
        double videoAspect = (double)ManualPreview.NaturalVideoWidth / ManualPreview.NaturalVideoHeight;
        double containerAspect = containerWidth / containerHeight;

        double actualVideoWidth, actualVideoHeight;
        if (videoAspect > containerAspect)
        {
            actualVideoWidth = containerWidth;
            actualVideoHeight = containerWidth / videoAspect;
        }
        else
        {
            actualVideoHeight = containerHeight;
            actualVideoWidth = containerHeight * videoAspect;
        }

        double targetAspect = 9.0 / 16.0;
        if (videoAspect > targetAspect)
        {
            CropFrame.Height = actualVideoHeight;
            CropFrame.Width = actualVideoHeight * targetAspect;
        }
        else
        {
            CropFrame.Width = actualVideoWidth;
            CropFrame.Height = actualVideoWidth / targetAspect;
        }

        Canvas.SetLeft(CropFrame, (containerWidth - CropFrame.Width) / 2);
        Canvas.SetTop(CropFrame, (containerHeight - CropFrame.Height) / 2);
        
        _vm.CropWidth = CropFrame.Width;
        _vm.CropHeight = CropFrame.Height;
        UpdateCropPercentages();
    }

    private Rect GetVideoRect()
    {
        if (ManualPreview.NaturalVideoWidth == 0 || ManualPreview.NaturalVideoHeight == 0)
            return new Rect(0, 0, PreviewContainer.ActualWidth, PreviewContainer.ActualHeight);

        double containerWidth = PreviewContainer.ActualWidth;
        double containerHeight = PreviewContainer.ActualHeight;
        double videoAspect = (double)ManualPreview.NaturalVideoWidth / ManualPreview.NaturalVideoHeight;
        double containerAspect = containerWidth / containerHeight;

        double actualVideoWidth, actualVideoHeight;
        if (videoAspect > containerAspect)
        {
            actualVideoWidth = containerWidth;
            actualVideoHeight = containerWidth / videoAspect;
        }
        else
        {
            actualVideoHeight = containerHeight;
            actualVideoWidth = containerHeight * videoAspect;
        }

        double left = (containerWidth - actualVideoWidth) / 2;
        double top = (containerHeight - actualVideoHeight) / 2;

        return new Rect(left, top, actualVideoWidth, actualVideoHeight);
    }

    private bool _isDraggingCrop = false;
    private Point _lastMousePos;

    private void CropFrame_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _isDraggingCrop = true;
        _lastMousePos = e.GetPosition(PreviewContainer);
        CropFrame.CaptureMouse();
        e.Handled = true;
    }

    private void CropFrame_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _isDraggingCrop = false;
        _isResizingCrop = false;
        _currentResizeHandle = "";
        CropFrame.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void CropFrame_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        double scaleFactor = e.Delta > 0 ? 1.1 : 0.9;
        double newWidth = CropFrame.Width * scaleFactor;
        double newHeight = CropFrame.Height * scaleFactor;

        newWidth = Math.Clamp(newWidth, 50, PreviewContainer.ActualWidth);
        newHeight = newWidth / (_vm.CropWidth / _vm.CropHeight);

        if (newHeight > PreviewContainer.ActualHeight)
        {
            newHeight = PreviewContainer.ActualHeight;
            newWidth = newHeight * (_vm.CropWidth / _vm.CropHeight);
        }

        double dx = (newWidth - CropFrame.Width) / 2;
        double dy = (newHeight - CropFrame.Height) / 2;

        double left = Canvas.GetLeft(CropFrame) - dx;
        double top = Canvas.GetTop(CropFrame) - dy;

        left = Math.Clamp(left, 0, PreviewContainer.ActualWidth - newWidth);
        top = Math.Clamp(top, 0, PreviewContainer.ActualHeight - newHeight);

        CropFrame.Width = newWidth;
        CropFrame.Height = newHeight;
        Canvas.SetLeft(CropFrame, left);
        Canvas.SetTop(CropFrame, top);

        UpdateCropPercentages();
        e.Handled = true;
    }

    private bool _isResizingCrop = false;
    private string _currentResizeHandle = "";
    private void ResizeHandle_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            _isResizingCrop = true;
            _currentResizeHandle = fe.Tag?.ToString() ?? "";
            CropFrame.CaptureMouse();
            e.Handled = true;
        }
    }

    private void CropFrame_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        Rect videoRect = GetVideoRect();

        if (_isDraggingCrop)
        {
            Point currentPos = e.GetPosition(PreviewContainer);
            double dx = currentPos.X - _lastMousePos.X;
            double dy = currentPos.Y - _lastMousePos.Y;

            double left = Canvas.GetLeft(CropFrame) + dx;
            double top = Canvas.GetTop(CropFrame) + dy;

            left = Math.Clamp(left, videoRect.Left, videoRect.Right - CropFrame.Width);
            top = Math.Clamp(top, videoRect.Top, videoRect.Bottom - CropFrame.Height);

            Canvas.SetLeft(CropFrame, left);
            Canvas.SetTop(CropFrame, top);

            _lastMousePos = currentPos;
            UpdateCropPercentages();
            e.Handled = true;
        }
        else if (_isResizingCrop)
        {
            Point currentPos = e.GetPosition(PreviewContainer);
            double left = Canvas.GetLeft(CropFrame);
            double top = Canvas.GetTop(CropFrame);
            double width = CropFrame.Width;
            double height = CropFrame.Height;

            double targetRatio = 9.0 / 16.0;

            switch (_currentResizeHandle)
            {
                case "Right": width = Math.Max(50, currentPos.X - left); break;
                case "Left": double ow = width; width = Math.Max(50, width + (left - currentPos.X)); left -= (width - ow); break;
                case "Bottom": height = Math.Max(50, currentPos.Y - top); break;
                case "Top": double oh = height; height = Math.Max(50, height + (top - currentPos.Y)); top -= (height - oh); break;
                case "BottomRight": width = Math.Max(50, currentPos.X - left); height = width / targetRatio; break;
                case "BottomLeft": double owbl = width; width = Math.Max(50, width + (left - currentPos.X)); left -= (width - owbl); height = width / targetRatio; break;
                case "TopRight": width = Math.Max(50, currentPos.X - left); double ohtr = height; height = width / targetRatio; top -= (height - ohtr); break;
                case "TopLeft": double owtl = width; width = Math.Max(50, width + (left - currentPos.X)); left -= (width - owtl); double ohtl = height; height = width / targetRatio; top -= (height - ohtl); break;
            }

            if (left < videoRect.Left) { width -= (videoRect.Left - left); left = videoRect.Left; }
            if (top < videoRect.Top) { height -= (videoRect.Top - top); top = videoRect.Top; }
            if (left + width > videoRect.Right) width = videoRect.Right - left;
            if (top + height > videoRect.Bottom) height = videoRect.Bottom - top;

            Canvas.SetLeft(CropFrame, left);
            Canvas.SetTop(CropFrame, top);
            CropFrame.Width = Math.Max(50, width);
            CropFrame.Height = Math.Max(50, height);

            UpdateCropPercentages();
            e.Handled = true;
        }
    }

    private void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_vm.IsManualVideoLoaded && MainTabs.SelectedIndex == 0) // Only in Manual Editor
        {
            switch (e.Key)
            {
                case System.Windows.Input.Key.Space:
                    PlayPause_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case System.Windows.Input.Key.I:
                    SetStart_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case System.Windows.Input.Key.O:
                    SetEnd_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
            }
        }
    }

    private void UpdateDimPanels()
    {
        Rect v = GetVideoRect();
        double left = Canvas.GetLeft(CropFrame);
        double top = Canvas.GetTop(CropFrame);
        double width = CropFrame.Width;
        double height = CropFrame.Height;

        // Dim panels relative to video rect
        DimLeft.Width = Math.Max(0, left - v.Left);
        DimLeft.Height = v.Height;
        Canvas.SetLeft(DimLeft, v.Left);
        Canvas.SetTop(DimLeft, v.Top);

        DimRight.Width = Math.Max(0, v.Right - (left + width));
        DimRight.Height = v.Height;
        Canvas.SetLeft(DimRight, left + width);
        Canvas.SetTop(DimRight, v.Top);

        DimTop.Width = width;
        DimTop.Height = Math.Max(0, top - v.Top);
        Canvas.SetLeft(DimTop, left);
        Canvas.SetTop(DimTop, v.Top);

        DimBottom.Width = width;
        DimBottom.Height = Math.Max(0, v.Bottom - (top + height));
        Canvas.SetLeft(DimBottom, left);
        Canvas.SetTop(DimBottom, top + height);
    }

    private void UpdateCropPercentages()
    {
        _vm.CropWidth = CropFrame.Width;
        _vm.CropHeight = CropFrame.Height;
        _vm.CropXPercent = (Canvas.GetLeft(CropFrame) + CropFrame.Width / 2) / PreviewContainer.ActualWidth;
        _vm.CropYPercent = (Canvas.GetTop(CropFrame) + CropFrame.Height / 2) / PreviewContainer.ActualHeight;
        UpdateDimPanels();
    }

    private void SetVertical_Click(object sender, RoutedEventArgs e)
    {
        Rect v = GetVideoRect();
        double targetAspect = 9.0 / 16.0;
        
        // Try to fill height
        CropFrame.Height = v.Height * 0.8;
        CropFrame.Width = CropFrame.Height * targetAspect;
        
        // If too wide for video
        if (CropFrame.Width > v.Width)
        {
            CropFrame.Width = v.Width;
            CropFrame.Height = CropFrame.Width / targetAspect;
        }
        
        CenterCrop_Click(null!, null!);
    }

    private void SetHorizontal_Click(object sender, RoutedEventArgs e)
    {
        Rect v = GetVideoRect();
        double targetAspect = 16.0 / 9.0;
        
        // Try to fill width
        CropFrame.Width = v.Width * 0.8;
        CropFrame.Height = CropFrame.Width / targetAspect;
        
        // If too high for video
        if (CropFrame.Height > v.Height)
        {
            CropFrame.Height = v.Height;
            CropFrame.Width = CropFrame.Height * targetAspect;
        }
        
        CenterCrop_Click(null!, null!);
    }

    private void CenterCrop_Click(object sender, RoutedEventArgs e)
    {
        Rect v = GetVideoRect();
        Canvas.SetLeft(CropFrame, v.Left + (v.Width - CropFrame.Width) / 2);
        Canvas.SetTop(CropFrame, v.Top + (v.Height - CropFrame.Height) / 2);
        UpdateCropPercentages();
    }

    private async void ManualCutBtn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_vm.ManualVideoPath) || !File.Exists(_vm.ManualVideoPath))
        {
            MessageBox.Show("Please load a video first.", "No Video", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _vm.Reset();
        _vm.IsGenerating = true;
        MainTabs.SelectedIndex = 2; // Switch to Activity Log tab
        _vm.SetStep(4, "Processing video...", 0);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var fileName = Path.GetFileNameWithoutExtension(_vm.ManualVideoPath);
        var outputPath = Path.Combine(AppConfig.OutputDir, $"{fileName}_cut_{timestamp}.mp4");

        try
        {
            Directory.CreateDirectory(AppConfig.OutputDir);
            var startTime = _vm.ManualStartTime;
            var endTime = _vm.ManualEndTime;
            var duration = endTime - startTime;
            if (duration <= 0) throw new Exception("End time must be greater than start time.");

            var videoInfo = await Task.Run(() => GetVideoInfoAsync(_vm.ManualVideoPath));
            int vWidth = videoInfo.Width;
            int vHeight = videoInfo.Height;

            // 🎯 Accurate Crop Mapping
            Rect videoRect = GetVideoRect();
            
            // Relative to the ACTUAL video pixels on screen
            double uiXRel = (Canvas.GetLeft(CropFrame) - videoRect.Left) / videoRect.Width;
            double uiYRel = (Canvas.GetTop(CropFrame) - videoRect.Top) / videoRect.Height;
            double uiWRel = CropFrame.Width / videoRect.Width;
            double uiHRel = CropFrame.Height / videoRect.Height;

            int cropX = (int)Math.Round(uiXRel * vWidth);
            int cropY = (int)Math.Round(uiYRel * vHeight);
            int cropW = (int)Math.Round(uiWRel * vWidth);
            int cropH = (int)Math.Round(uiHRel * vHeight);

            // Sanity clamp
            cropX = Math.Clamp(cropX, 0, vWidth);
            cropY = Math.Clamp(cropY, 0, vHeight);
            cropW = Math.Min(cropW, vWidth - cropX);
            cropH = Math.Min(cropH, vHeight - cropY);

            string vf;
            if (_vm.IsVerticalRatio)
            {
                vf = $"crop={cropW}:{cropH}:{cropX}:{cropY},scale=1080:1920,setsar=1";
            }
            else
            {
                vf = $"crop={cropW}:{cropH}:{cropX}:{cropY},split[fg][bg_raw]; " +
                     $"[bg_raw]scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,boxblur=20:10[bg]; " +
                     $"[fg]scale=1080:-1[fg_scaled]; " +
                     $"[bg][fg_scaled]overlay=(W-w)/2:(H-h)/2[out]; [out]setsar=1";
            }

            var args = $"-ss {startTime:F2} -t {duration:F2} -i \"{_vm.ManualVideoPath}\" -vf \"{vf}\" -c:v libx264 -preset fast -crf 23 -c:a aac -b:a 192k -y \"{outputPath}\"";
            Log($"✂️ Cutting video: {cropW}x{cropH} at {cropX},{cropY}");

            // Execute with Progress
            await Task.Run(() => RunFfmpegWithProgress(args, duration));

            if (File.Exists(outputPath))
            {
                Log("✅ Cut complete: " + Path.GetFullPath(outputPath));
                _vm.SetComplete(Path.GetFullPath(outputPath));
                
                // 🔊 Native Success Sound & Message
                System.Media.SystemSounds.Exclamation.Play();
                MessageBox.Show($"Video exported successfully!\n\nLocation: {outputPath}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else throw new Exception("FFmpeg failed to produce output file.");
        }
        catch (Exception ex) { Log($"❌ ERROR: {ex.Message}"); _vm.IsGenerating = false; }
    }

    private void RunFfmpegWithProgress(string arguments, double duration)
    {
        var processInfo = new ProcessStartInfo
        {
            FileName = AppConfig.FfmpegPath,
            Arguments = arguments,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo);
        if (process == null) return;

        while (!process.StandardError.EndOfStream)
        {
            var line = process.StandardError.ReadLine();
            if (line == null) continue;

            // Simple FFmpeg progress parsing (time=00:00:00.00)
            if (line.Contains("time="))
            {
                var timeIndex = line.IndexOf("time=");
                var timeStr = line.Substring(timeIndex + 5, 11);
                if (TimeSpan.TryParse(timeStr, out var ts))
                {
                    double percent = (ts.TotalSeconds / duration) * 100;
                    _vm.ProgressValue = Math.Clamp(percent, 0, 100);
                }
            }
        }
        process.WaitForExit();
    }

    private async Task<(int Width, int Height)> GetVideoInfoAsync(string videoPath)
    {
        var args = $"-v error -select_streams v:0 -show_entries stream=width,height -of csv=s=x:p=0 \"{videoPath}\"";
        var processInfo = new ProcessStartInfo { FileName = AppConfig.FfprobePath, Arguments = args, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var process = Process.Start(processInfo);
        if (process == null) return (1920, 1080);
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        var parts = output.Trim().Split('x');
        if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h)) return (w, h);
        return (1920, 1080);
    }
}
