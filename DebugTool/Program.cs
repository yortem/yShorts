using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("🧪 DebugTool: Testing Gemini API connection...");

        // Load config manually since we are in a separate tool
        string configPath = @"..\appsettings.json";
        if (!File.Exists(configPath))
        {
            Console.WriteLine("❌ appsettings.json not found!");
            return;
        }

        var json = File.ReadAllText(configPath);
        var doc = JsonDocument.Parse(json);
        var apiKey = doc.RootElement.GetProperty("GeminiApiKey").GetString();

        if (string.IsNullOrEmpty(apiKey) || apiKey.Contains("YOUR_"))
        {
            Console.WriteLine($"❌ Invalid API Key: '{apiKey}'");
            return;
        }

        Console.WriteLine($"✅ Use Key: {apiKey.Substring(0, 5)}...");

        // Define endpoint - using the one from AIService.cs (which might be wrong!)
        // The user changed it to gemini-2.5-flash
        string endpoint = "https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent";
        Console.WriteLine($"🔹 Endpoint: {endpoint}");

        using var client = new HttpClient();
        var prompt = "Explain quantum physics to a 5 year old in one sentence.";

        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        try
        {
            var response = await client.PostAsync($"{endpoint}?key={apiKey}", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"\n[Response Status]: {response.StatusCode}");
            Console.WriteLine($"[Response Body]:\n{responseBody}");

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("❌ Request Failed!");
            }
            else
            {
                Console.WriteLine("✅ Request Succeeded!");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Exception: {ex.Message}");
        }
    }
}
