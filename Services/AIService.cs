using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using yShorts.Models;

namespace yShorts.Services;

/// <summary>
/// Handles communication with Google Gemini API for script and keyword generation.
/// </summary>
public class AIService
{
    private readonly HttpClient _http;
    private const string GeminiEndpoint =
        "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent";

    public AIService(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Calls Gemini to generate a narration script and search keywords for the given topic.
    /// </summary>
    public async Task<ScriptResult> GenerateScriptAsync(string topic)
    {
        Console.WriteLine($"\n🤖 [AI] Generating script for: \"{topic}\" in {AppConfig.VideoLanguage}...");

        var mood = AppConfig.Mood;
        var prompt = $@"You are a short-form video scriptwriter. Create a script for a 30-60 second vertical video (TikTok/YouTube Shorts style) about: ""{topic}"".
Mood/Tone: {mood}.

The script MUST be in {AppConfig.VideoLanguage.ToUpper()}.

Return ONLY valid JSON (no markdown, no code fences) in this exact format:
{{
  ""script"": ""The full narration text to be spoken in {AppConfig.VideoLanguage}. Keep it engaging, concise, and around 80-150 words. Use short punchy sentences."",
  ""keywords"": [""english_keyword1"", ""english_keyword2"", ""english_keyword3"", ""english_keyword4"", ""english_keyword5""],
  ""seo_title"": ""A catchy, SEO-optimized title for YouTube Shorts/TikTok in {AppConfig.VideoLanguage}"",
  ""seo_description"": ""A compelling description including 3-5 relevant hashtags in {AppConfig.VideoLanguage}""
}}

IMPORTANT: The keywords MUST be in ENGLISH for stock video searching, even if the script is in {AppConfig.VideoLanguage}.
Focus on visually descriptive keywords that would match cinematic B-roll footage.";

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.8,
                maxOutputTokens = 4096
            }
        };

        var url = $"{GeminiEndpoint}?key={AppConfig.GeminiApiKey}";
        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync(url, content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Gemini API error ({response.StatusCode}): {responseBody}");
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var generatedText = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? throw new Exception("Empty response from AI");

            // Clean up: remove markdown code fences if present
            generatedText = generatedText.Trim();
            if (generatedText.StartsWith("```json")) generatedText = generatedText[7..];
            else if (generatedText.StartsWith("```")) generatedText = generatedText[3..];
            if (generatedText.EndsWith("```")) generatedText = generatedText[..^3];
            generatedText = generatedText.Trim();

            var root = JsonDocument.Parse(generatedText).RootElement;

            var result = new ScriptResult
            {
                Script = root.GetProperty("script").GetString() ?? "",
                SeoTitle = root.GetProperty("seo_title").GetString() ?? "",
                SeoDescription = root.GetProperty("seo_description").GetString() ?? "",
                Keywords = new List<string>()
            };

            foreach (var kw in root.GetProperty("keywords").EnumerateArray())
            {
                result.Keywords.Add(kw.GetString() ?? "");
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[AI] ✅ Script generated.");
            Console.ResetColor();
            Console.WriteLine($"[AI] Keywords: {string.Join(", ", result.Keywords)}");

            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AI] Error parsing response: {ex.Message}\nRaw: {responseBody}");
            throw;
        }
    }

    /// <summary>
    /// Advanced: Calls Gemini to generate a script based on specific stages/steps provided by the user.
    /// </summary>
    public async Task<ScriptResult> GenerateScriptAdvancedAsync(List<VideoStage> stages)
    {
        var stagesDescription = string.Join("\n", stages.Select((s, i) => $"Stage {i + 1}: Goal: {s.Goal}"));
        var mood = AppConfig.Mood;

        var prompt = $@"You are a short-form video scriptwriter. Create a script consisting of multiple parts for a video.
Mood/Tone: {mood}.
The video has {stages.Count} stages:
{stagesDescription}

The script MUST be in {AppConfig.VideoLanguage.ToUpper()}.

Return ONLY valid JSON (no markdown) in this exact format:
{{
  ""stage_scripts"": [""Short narration for stage 1 (7-14 seconds/20-35 words)"", ""Short narration for stage 2 (7-14 seconds/20-35 words)"", ...],
  ""seo_title"": ""A catchy title in {AppConfig.VideoLanguage}"",
  ""seo_description"": ""Description + hashtags in {AppConfig.VideoLanguage}""
}}

IMPORTANT: Each stage script MUST BE SHORT (7-14 seconds of speaking). This is roughly 20-35 words per stage.
Keep it punchy. stage_scripts MUST contain ONLY the spoken text. Do NOT include stage numbers or visual notes.";

        var requestBody = new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } },
            generationConfig = new { temperature = 0.7, maxOutputTokens = 2048 }
        };

        var url = $"{GeminiEndpoint}?key={AppConfig.GeminiApiKey}";
        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync(url, content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Gemini Advanced API error: {responseBody}");

        var doc = JsonDocument.Parse(responseBody);
        var generatedText = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "";

        // Clean markdown
        generatedText = generatedText.Trim();
        if (generatedText.StartsWith("```json")) generatedText = generatedText[7..];
        else if (generatedText.StartsWith("```")) generatedText = generatedText[3..];
        if (generatedText.EndsWith("```")) generatedText = generatedText[..^3];
        generatedText = generatedText.Trim();

        var root = JsonDocument.Parse(generatedText).RootElement;

        var result = new ScriptResult
        {
            SeoTitle = root.GetProperty("seo_title").GetString() ?? "",
            SeoDescription = root.GetProperty("seo_description").GetString() ?? "",
            Keywords = stages.Select(s => s.VisualSearch).ToList(),
            StageScripts = new List<string>()
        };

        foreach (var s in root.GetProperty("stage_scripts").EnumerateArray())
        {
            result.StageScripts.Add(s.GetString() ?? "");
        }

        // Combine for legacy compatibility
        result.Script = string.Join(" ", result.StageScripts);

        return result;
    }
}
