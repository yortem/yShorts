namespace yShorts.Models;

public class VideoStage
{
    public string Goal { get; set; } = string.Empty;
    public string VisualSearch { get; set; } = string.Empty;
    public string LocalVideoPath { get; set; } = string.Empty;
    public bool HasLocalVideo => !string.IsNullOrEmpty(LocalVideoPath);
    public string LocalVideoName => System.IO.Path.GetFileName(LocalVideoPath);
}
