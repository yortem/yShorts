using System.Collections.Generic;

namespace yShorts.Models;

/// <summary>
/// Data container for all assets and paths needed to build the final video.
/// </summary>
public class VideoProject
{
    public string Topic { get; set; } = string.Empty;
    public string Script { get; set; } = string.Empty;
    public List<string> VideoClipPaths { get; set; } = new();
    public string AudioPath { get; set; } = string.Empty;
    public string SubtitlePath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public double AudioDurationSeconds { get; set; }
    public List<string> StageScripts { get; set; } = new();
    public List<VideoStage> Stages { get; set; } = new();
}
