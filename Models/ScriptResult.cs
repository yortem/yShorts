namespace yShorts.Models;

/// <summary>
/// Holds the AI-generated script and keywords for a video topic.
/// </summary>
public class ScriptResult
{
    public string Script { get; set; } = string.Empty;
    public List<string> Keywords { get; set; } = new();
    public string SeoTitle { get; set; } = string.Empty;
    public string SeoDescription { get; set; } = string.Empty;
    public List<string> StageScripts { get; set; } = new();
}
