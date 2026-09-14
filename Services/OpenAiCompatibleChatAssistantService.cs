using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ARIS1.Services
{
    // Talks to any OpenAI-compatible /chat/completions endpoint. Defaults to Groq's free tier;
    // Cerebras, OpenRouter, GitHub Models or a local Ollama are config-only swaps
    // (ChatAssistant:BaseUrl / ChatAssistant:Model). Falls back to the rule-based bot on any failure.
    public class OpenAiCompatibleChatAssistantService : IChatAssistantService
    {
        private readonly HttpClient _httpClient;
        private readonly RuleBasedChatAssistantService _fallback;
        private readonly ILogger<OpenAiCompatibleChatAssistantService> _logger;
        private readonly string _apiKey;
        private readonly string _baseUrl;
        private readonly string _model;

        public OpenAiCompatibleChatAssistantService(
            HttpClient httpClient,
            IConfiguration configuration,
            RuleBasedChatAssistantService fallback,
            ILogger<OpenAiCompatibleChatAssistantService> logger)
        {
            _httpClient = httpClient;
            _fallback = fallback;
            _logger = logger;
            _apiKey = configuration["ChatAssistant:ApiKey"]
                ?? throw new InvalidOperationException("ChatAssistant:ApiKey is not configured.");
            _baseUrl = (configuration["ChatAssistant:BaseUrl"] ?? "https://api.groq.com/openai/v1").TrimEnd('/');
            _model = configuration["ChatAssistant:Model"] ?? "openai/gpt-oss-120b";
        }

        public async Task<string> GetResponseAsync(string userInput, IReadOnlyList<ChatConcern> concerns)
        {
            var request = new ChatRequest
            {
                Model = _model,
                MaxTokens = 300,
                Temperature = 0.5,
                Messages = new List<ChatMessage>
                {
                    new() { Role = "system", Content = BuildSystemInstruction(concerns) },
                    new() { Role = "user", Content = userInput }
                }
            };

            try
            {
                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
                {
                    Content = JsonContent.Create(request)
                };
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

                using var response = await _httpClient.SendAsync(httpRequest);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Chat assistant request failed with {StatusCode}: {Body}",
                        (int)response.StatusCode, body.Length > 500 ? body[..500] : body);
                    return await _fallback.GetResponseAsync(userInput, concerns);
                }

                var result = await response.Content.ReadFromJsonAsync<ChatResponse>();
                var text = result?.Choices?.FirstOrDefault()?.Message?.Content;
                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogWarning("Chat assistant returned an empty response.");
                    return await _fallback.GetResponseAsync(userInput, concerns);
                }

                return text.Trim();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Chat assistant request could not reach {BaseUrl}.", _baseUrl);
                return await _fallback.GetResponseAsync(userInput, concerns);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "Chat assistant request timed out.");
                return await _fallback.GetResponseAsync(userInput, concerns);
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

        private class ChatRequest
        {
            [JsonPropertyName("model")]
            public string Model { get; set; } = string.Empty;

            [JsonPropertyName("messages")]
            public List<ChatMessage> Messages { get; set; } = new();

            [JsonPropertyName("max_tokens")]
            public int MaxTokens { get; set; }

            [JsonPropertyName("temperature")]
            public double Temperature { get; set; }
        }

        private class ChatMessage
        {
            [JsonPropertyName("role")]
            public string Role { get; set; } = string.Empty;

            [JsonPropertyName("content")]
            public string? Content { get; set; }
        }

        private class ChatResponse
        {
            [JsonPropertyName("choices")]
            public List<ChatChoice>? Choices { get; set; }
        }

        private class ChatChoice
        {
            [JsonPropertyName("message")]
            public ChatMessage? Message { get; set; }
        }
    }
}
