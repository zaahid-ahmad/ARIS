using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ARIS1.Services
{
    public class GeminiChatAssistantService : IChatAssistantService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _model;

        public GeminiChatAssistantService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _apiKey = configuration["Gemini:ApiKey"]
                ?? throw new InvalidOperationException("Gemini:ApiKey is not configured.");
            _model = configuration["Gemini:Model"] ?? "gemini-flash-latest";
        }

        public async Task<string> GetResponseAsync(string userInput, IReadOnlyList<ChatConcern> concerns)
        {
            var systemInstruction = BuildSystemInstruction(concerns);

            var request = new GeminiRequest
            {
                SystemInstruction = new GeminiContent
                {
                    Parts = new List<GeminiPart> { new() { Text = systemInstruction } }
                },
                Contents = new List<GeminiContent>
                {
                    new()
                    {
                        Role = "user",
                        Parts = new List<GeminiPart> { new() { Text = userInput } }
                    }
                }
            };

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

            try
            {
                var response = await _httpClient.PostAsJsonAsync(url, request);
                if (!response.IsSuccessStatusCode)
                {
                    return "I'm having trouble reaching the assistant right now — try again in a moment.";
                }

                var result = await response.Content.ReadFromJsonAsync<GeminiResponse>();
                var text = result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;
                return string.IsNullOrWhiteSpace(text)
                    ? "I couldn't come up with a response to that — try rephrasing your question."
                    : text.Trim();
            }
            catch (HttpRequestException)
            {
                return "I'm having trouble reaching the assistant right now — try again in a moment.";
            }
            catch (TaskCanceledException)
            {
                return "That took too long to answer — try again in a moment.";
            }
        }

        private static string BuildSystemInstruction(IReadOnlyList<ChatConcern> concerns)
        {
            if (concerns.Count == 0)
            {
                return "You are a friendly study assistant for a high school learner who currently has no flagged " +
                       "areas of concern. Congratulate them briefly and encourage them to keep up the good work. " +
                       "Keep responses to 2-3 sentences.";
            }

            var concernLines = concerns.Select(c =>
                $"- {c.Subject} (level: {c.Level}, topics: {string.Join(", ", c.Topics)})");

            return "You are a friendly study assistant for a high school learner. " +
                   "Only help with the learner's own flagged areas of concern, listed below. " +
                   "If asked about anything unrelated to these subjects/topics, politely decline and redirect " +
                   "the learner back to one of them. Keep responses encouraging, practical, and to 2-4 sentences.\n\n" +
                   "Areas of concern:\n" + string.Join("\n", concernLines);
        }

        private class GeminiRequest
        {
            [JsonPropertyName("system_instruction")]
            public GeminiContent SystemInstruction { get; set; } = new();

            [JsonPropertyName("contents")]
            public List<GeminiContent> Contents { get; set; } = new();
        }

        private class GeminiContent
        {
            [JsonPropertyName("role")]
            public string? Role { get; set; }

            [JsonPropertyName("parts")]
            public List<GeminiPart> Parts { get; set; } = new();
        }

        private class GeminiPart
        {
            [JsonPropertyName("text")]
            public string Text { get; set; } = string.Empty;
        }

        private class GeminiResponse
        {
            [JsonPropertyName("candidates")]
            public List<GeminiCandidate>? Candidates { get; set; }
        }

        private class GeminiCandidate
        {
            [JsonPropertyName("content")]
            public GeminiContent? Content { get; set; }
        }
    }
}
