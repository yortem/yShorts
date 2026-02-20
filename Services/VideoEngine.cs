using System.Diagnostics;
using System.Text;
using yShorts.Models;

namespace yShorts.Services;

/// <summary>
/// Orchestrates FFmpeg commands to build the final vertical video.
/// </summary>
public class VideoEngine
{
    /// <summary>
    /// Builds the final video by processing clips, overlaying audio, and burning subtitles.
    /// </summary>
    public async Task BuildVideoAsync(VideoProject project, IProgress<double>? progress = null)
    {
        Console.WriteLine("\n🎞️ [Video] Starting video assembly...");
        Directory.CreateDirectory(Path.GetDirectoryName(project.OutputPath)!);

        var finalStageClips = new List<string>();
        var finalStageAudios = new List<string>();
        double totalDuration = 0;

        bool isAdvanced = project.Stages.Any();
        int stageCount = isAdvanced ? project.Stages.Count : 1;

        // If not advanced, we treat the whole thing as one big stage
        if (!isAdvanced)
        {
            // Existing logic: Concat all clips to match total audio duration
            var scaledClips = await ScaleClipsAsync(project.VideoClipPaths, progress, 0, 15);
            var concatPath = Path.Combine(AppConfig.TempDir, "basic_concat.mp4");
            await CreateConcatListAsync(scaledClips, project.AudioDurationSeconds, Path.Combine(AppConfig.TempDir, "basic_list.txt"));
            await RunFfmpegAsync($"-f concat -safe 0 -i \"{Path.Combine(AppConfig.TempDir, "basic_list.txt")}\" -c copy -y \"{concatPath}\"", "Concatenating clips");

            finalStageClips.Add(concatPath);
            if (!string.IsNullOrEmpty(project.AudioPath)) finalStageAudios.Add(project.AudioPath);
            totalDuration = project.AudioDurationSeconds > 0 ? project.AudioDurationSeconds : 15.0;
        }
        else
        {
            // Advanced Sync Mode: Sync each stage to its narration
            for (int i = 0; i < project.Stages.Count; i++)
            {
                var stage = project.Stages[i];
                var stageScript = (project.StageScripts.Count > i) ? project.StageScripts[i] : "";

                // Use the clip index that corresponds to the stage
                string? inputClip = (project.VideoClipPaths.Count > i) ? project.VideoClipPaths[i] : project.VideoClipPaths.FirstOrDefault();

                if (inputClip == null) continue;

                // 1. Generate stage audio
                double stageDuration = 10.0; // Default silent duration for stage
                string? stageAudio = null;

                if (!string.IsNullOrWhiteSpace(stageScript))
                {
                    stageAudio = Path.Combine(AppConfig.TempDir, $"stage_{i}_audio.mp3");
                    var mediaService = new MediaService(new HttpClient());
                    await mediaService.GenerateTtsAsync(stageScript, stageAudio);
                    stageDuration = await mediaService.GetMediaDurationAsync(stageAudio);
                    finalStageAudios.Add(stageAudio);
                }

                // 2. Prepare stage video
                var scaledPath = Path.Combine(AppConfig.TempDir, $"stage_{i}_scaled_raw.mp4");
                await RunFfmpegAsync($"-i \"{inputClip}\" -vf \"scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,setsar=1\" -r 30 -an -y \"{scaledPath}\"", $"Scaling stage {i + 1}");

                var loopedPath = Path.Combine(AppConfig.TempDir, $"stage_{i}_final_loop.mp4");
                await CreateLoopVideoAsync(scaledPath, stageDuration, loopedPath);

                finalStageClips.Add(loopedPath);
                totalDuration += stageDuration;

                progress?.Report((i + 1) * (50.0 / project.Stages.Count));
            }
        }

        // 3. Final Assembly
        var finalVideoNoAudio = Path.Combine(AppConfig.TempDir, "final_no_audio.mp4");
        var finalAudioMerged = Path.Combine(AppConfig.TempDir, "final_merged_audio.mp3");

        // Merge videos
        var videoListPath = Path.Combine(AppConfig.TempDir, "final_video_list.txt");
        await File.WriteAllLinesAsync(videoListPath, finalStageClips.Select(p => $"file '{Path.GetFullPath(p).Replace("\\", "/")}'"));
        await RunFfmpegAsync($"-f concat -safe 0 -i \"{videoListPath}\" -c copy -y \"{finalVideoNoAudio}\"", "Final video concat");

        // Merge audios if any
        if (finalStageAudios.Any())
        {
            if (finalStageAudios.Count == 1)
            {
                finalAudioMerged = finalStageAudios[0];
            }
            else
            {
                var audioListPath = Path.Combine(AppConfig.TempDir, "final_audio_list.txt");
                await File.WriteAllLinesAsync(audioListPath, finalStageAudios.Select(p => $"file '{Path.GetFullPath(p).Replace("\\", "/")}'"));
                await RunFfmpegAsync($"-f concat -safe 0 -i \"{audioListPath}\" -c copy -y \"{finalAudioMerged}\"", "Final audio concat");
            }
        }

        // 4. Burn Subtitles and Overlay Audio
        var subtitleFilter = "";
        if (!string.IsNullOrEmpty(project.SubtitlePath) && File.Exists(project.SubtitlePath))
        {
            var escapedSubPath = project.SubtitlePath.Replace("\\", "/").Replace(":", "\\:");
            var fontName = AppConfig.VideoLanguage == "Hebrew" ? "Open Sans Hebrew" : "Arial";
            var style = $"FontName={fontName},FontSize=28,PrimaryColour=&H00FFFFFF,OutlineColour=&H00000000,BorderStyle=1,Outline=2.0,Shadow=0,Alignment=2,MarginV=80";
            subtitleFilter = $"-vf \"subtitles='{escapedSubPath}':force_style='{style}'\"";
        }

        var audioInput = finalStageAudios.Any() ? $"-i \"{finalAudioMerged}\"" : "";
        var audioMapping = finalStageAudios.Any() ? "-map 0:v -map 1:a -c:a aac -b:a 192k" : "-c:a copy";

        var finalArgs = $"-i \"{finalVideoNoAudio}\" {audioInput} {subtitleFilter} {audioMapping} -c:v libx264 -preset fast -crf 23 -t {totalDuration:F2} -shortest -y \"{project.OutputPath}\"";

        await RunFfmpegAsync(finalArgs, "Final render", null, totalDuration);
        progress?.Report(100);
    }

    private async Task<List<string>> ScaleClipsAsync(List<string> inputs, IProgress<double>? progress, double startP, double rangeP)
    {
        var results = new List<string>();
        for (int i = 0; i < inputs.Count; i++)
        {
            var output = Path.Combine(AppConfig.TempDir, $"scaled_{i}.mp4");
            await RunFfmpegAsync($"-i \"{inputs[i]}\" -vf \"scale=1080:1920:force_original_aspect_ratio=increase,crop=1080:1920,setsar=1\" -r 30 -an -y \"{output}\"", $"Scaling clip {i + 1}");
            results.Add(output);
            progress?.Report(startP + ((i + 1) / (double)inputs.Count * rangeP));
        }
        return results;
    }

    private async Task CreateLoopVideoAsync(string input, double duration, string output)
    {
        var mediaService = new MediaService(new HttpClient());
        var inputDuration = await mediaService.GetMediaDurationAsync(input);
        if (inputDuration >= duration)
        {
            await RunFfmpegAsync($"-i \"{input}\" -t {duration:F2} -c copy -y \"{output}\"", "Trimming clip");
        }
        else
        {
            // Loop clip
            int loops = (int)Math.Ceiling(duration / inputDuration);
            var sb = new StringBuilder();
            var fullPath = Path.GetFullPath(input).Replace("\\", "/");
            for (int i = 0; i < loops; i++) sb.AppendLine($"file '{fullPath}'");

            var listPath = Path.Combine(AppConfig.TempDir, "temp_loop_list.txt");
            await File.WriteAllTextAsync(listPath, sb.ToString());
            await RunFfmpegAsync($"-f concat -safe 0 -i \"{listPath}\" -t {duration:F2} -c copy -y \"{output}\"", "Looping clip");
        }
    }

    /// <summary>
    /// Creates a concat file that loops clips to fill the required audio duration.
    /// </summary>
    /// <summary>
    /// Creates a concat file that loops clips to fill the required audio duration.
    /// </summary>
    private async Task CreateConcatListAsync(List<string> clips, double targetDuration, string outputPath)
    {
        var sb = new StringBuilder();
        double currentDuration = 0;

        // Get the duration of each clip
        var clipDurations = new List<double>();
        var mediaService = new MediaService(new HttpClient());
        foreach (var clip in clips)
        {
            var duration = await mediaService.GetMediaDurationAsync(clip);
            clipDurations.Add(duration);
        }

        if (clipDurations.All(d => d <= 0))
        {
            throw new Exception("All video clips have 0 duration or could not be read.");
        }

        // Loop clips until we exceed target duration (we'll trim later)
        int clipIndex = 0;
        int loopCount = 0;

        while (currentDuration < targetDuration + 5) // Increased buffer to 5s to ensure no cutoffs
        {
            // Safety break to prevent infinite loops if something is wrong
            if (loopCount > 100) break;

            var clipPath = clips[clipIndex];
            var clipDuration = clipDurations[clipIndex];

            // Only use valid clips
            if (clipDuration > 0)
            {
                var fullPath = Path.GetFullPath(clipPath).Replace("\\", "/");
                sb.AppendLine($"file '{fullPath}'");
                currentDuration += clipDuration;
            }

            clipIndex = (clipIndex + 1) % clips.Count;
            loopCount++;
        }

        await File.WriteAllTextAsync(outputPath, sb.ToString());
        Console.WriteLine($"  Concat list created: {loopCount} clips looped to ~{currentDuration:F1}s (target: {targetDuration:F1}s)");
    }

    /// <summary>
    /// Runs an FFmpeg command and waits for completion.
    /// </summary>
    private async Task RunFfmpegAsync(string arguments, string stepDescription, IProgress<double>? progress = null, double totalDuration = 0)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  ▶ {stepDescription}");
        // Console.WriteLine($"    ffmpeg {arguments[..Math.Min(arguments.Length, 120)]}..."); // Too noisy
        Console.ResetColor();

        var processInfo = new ProcessStartInfo
        {
            FileName = AppConfig.FfmpegPath,
            Arguments = arguments,
            RedirectStandardOutput = false, // FFmpeg writes stats to stderr
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processInfo)
            ?? throw new Exception($"Failed to start FFmpeg for: {stepDescription}");

        // Parse stderr for progress
        var stderrBuffer = new StringBuilder();

        // We need to read stderr asynchronously to parse progress while it runs
        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data == null) return;
            stderrBuffer.AppendLine(e.Data);

            if (progress != null && totalDuration > 0)
            {
                // Try to parse "time=00:00:05.20"
                // Example: frame=  123 fps=0.0 q=-1.0 size=   123kB time=00:00:05.20 bitrate= 192.0kbits/s speed=10.4x
                var timeIndex = e.Data.IndexOf("time=");
                if (timeIndex >= 0)
                {
                    var timeStr = e.Data.Substring(timeIndex + 5, 11); // 00:00:00.00
                    if (TimeSpan.TryParse(timeStr, out var ts))
                    {
                        var percent = (ts.TotalSeconds / totalDuration) * 100;
                        if (percent > 100) percent = 100;
                        progress.Report(percent);
                    }
                }
            }
        };

        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  ❌ FFmpeg error for '{stepDescription}':");
            // Print the last few lines of stderr for debugging
            var errorLines = stderrBuffer.ToString().Split('\n').TakeLast(10);
            foreach (var line in errorLines)
                Console.WriteLine($"    {line.Trim()}");
            Console.ResetColor();
            throw new Exception($"FFmpeg failed for '{stepDescription}' with exit code {process.ExitCode}");
        }

        if (progress != null) progress.Report(100);

        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine($"  ✓ {stepDescription} complete");
        Console.ResetColor();
    }
}
