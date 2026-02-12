using System.Text;

namespace yShorts.Services;

/// <summary>
/// Generates .srt subtitle files from script text.
/// </summary>
public class SubtitleService
{
    /// <summary>
    /// Splits the script into sentences and generates an SRT file with evenly distributed timing.
    /// </summary>
    public void GenerateSrt(string script, double totalDurationSeconds, string outputPath)
    {
        Console.WriteLine("\n📝 [Subtitles] Generating .srt file...");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        // Split script into sentences
        var sentences = SplitIntoSentences(script);
        if (sentences.Count == 0)
        {
            Console.WriteLine("[Subtitles] ⚠️ No sentences found in script.");
            return;
        }

        // Calculate total character count for proportional timing
        int totalChars = sentences.Sum(s => s.Length);
        double currentTime = 0.0;

        var sb = new StringBuilder();

        for (int i = 0; i < sentences.Count; i++)
        {
            var sentence = sentences[i];

            // Distribute time proportionally based on character length
            double proportion = (double)sentence.Length / totalChars;
            double duration = proportion * totalDurationSeconds;

            // Ensure minimum 1.5 seconds per subtitle
            duration = Math.Max(duration, 1.5);

            double startTime = currentTime;
            double endTime = Math.Min(currentTime + duration, totalDurationSeconds);

            sb.AppendLine((i + 1).ToString());
            sb.AppendLine($"{FormatSrtTime(startTime)} --> {FormatSrtTime(endTime)}");
            sb.AppendLine(WrapSubtitleText(sentence));
            sb.AppendLine();

            currentTime = endTime;
        }

        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[Subtitles] ✅ Generated {sentences.Count} subtitle entries → {outputPath}");
        Console.ResetColor();
    }

    /// <summary>
    /// Splits text into sentences by punctuation.
    /// </summary>
    private List<string> SplitIntoSentences(string text)
    {
        var delimiters = new[] { '.', '!', '?' };
        var rawParts = text.Split(delimiters, StringSplitOptions.RemoveEmptyEntries);

        var sentences = new List<string>();
        foreach (var part in rawParts)
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
            {
                sentences.Add(trimmed);
            }
        }

        return sentences;
    }

    /// <summary>
    /// Wraps long subtitle text to max ~40 chars per line for readability on mobile.
    /// </summary>
    private string WrapSubtitleText(string text, int maxLineLength = 40)
    {
        if (text.Length <= maxLineLength) return text;

        var words = text.Split(' ');
        var lines = new List<string>();
        var currentLine = new StringBuilder();

        foreach (var word in words)
        {
            if (currentLine.Length + word.Length + 1 > maxLineLength && currentLine.Length > 0)
            {
                lines.Add(currentLine.ToString().Trim());
                currentLine.Clear();
            }
            currentLine.Append(word + " ");
        }

        if (currentLine.Length > 0)
            lines.Add(currentLine.ToString().Trim());

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Formats seconds to SRT time format (HH:MM:SS,mmm)
    /// </summary>
    private string FormatSrtTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";
    }
}
