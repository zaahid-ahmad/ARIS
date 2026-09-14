using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

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
        private readonly double _temperature;
        private readonly string? _reasoningEffort;

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
            // Low temperature by default keeps replies close to the CAPS rules in the system prompt.
            _temperature = double.TryParse(configuration["ChatAssistant:Temperature"],
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t)
                ? t
                : 0.2;
            // gpt-oss models are reasoning models: "low" keeps hidden reasoning tokens (which count toward
            // max_tokens and the free tier's tokens-per-minute limit) small. Other providers/models may not
            // accept the parameter, so it's only sent when configured or when using gpt-oss.
            _reasoningEffort = configuration["ChatAssistant:ReasoningEffort"]
                ?? (_model.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase) ? "low" : null);
        }

        private const int MaxRateLimitWaitSeconds = 5;

        public async Task<string> GetResponseAsync(string userInput, ChatContext context)
        {
            var request = new ChatRequest
            {
                Model = _model,
                MaxTokens = 600,
                Temperature = _temperature,
                ReasoningEffort = _reasoningEffort,
                Messages = new List<ChatMessage>
                {
                    new() { Role = "system", Content = CapsSystemPrompt.Build(context) },
                    new() { Role = "user", Content = userInput }
                }
            };

            try
            {
                var response = await SendAsync(request);

                // Free-tier tokens-per-minute limit: if it resets within a few seconds, wait and retry once
                // rather than dropping straight to the rule-based bot.
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(MaxRateLimitWaitSeconds + 1);
                    if (wait <= TimeSpan.FromSeconds(MaxRateLimitWaitSeconds))
                    {
                        _logger.LogInformation("Chat assistant rate-limited; retrying in {Seconds:0.#}s.", wait.TotalSeconds);
                        response.Dispose();
                        await Task.Delay(wait);
                        response = await SendAsync(request);
                    }
                }

                using var _ = response;
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Chat assistant request failed with {StatusCode}: {Body}",
                        (int)response.StatusCode, body.Length > 500 ? body[..500] : body);
                    return await _fallback.GetResponseAsync(userInput, context);
                }

                var result = await response.Content.ReadFromJsonAsync<ChatResponse>();
                var choice = result?.Choices?.FirstOrDefault();
                var text = choice?.Message?.Content;
                if (choice?.FinishReason == "length")
                {
                    _logger.LogWarning("Chat assistant reply was cut off by the max_tokens limit.");
                }
                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogWarning("Chat assistant returned an empty response.");
                    return await _fallback.GetResponseAsync(userInput, context);
                }

                return StripMarkdown(text);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Chat assistant request could not reach {BaseUrl}.", _baseUrl);
                return await _fallback.GetResponseAsync(userInput, context);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "Chat assistant request timed out.");
                return await _fallback.GetResponseAsync(userInput, context);
            }
        }

        private async Task<HttpResponseMessage> SendAsync(ChatRequest request)
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
            {
                Content = JsonContent.Create(request)
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            return await _httpClient.SendAsync(httpRequest);
        }

        // The chat bubble renders raw text, so remove markdown/LaTeX markers the model sometimes emits
        // despite the plain-text instruction.
        private static string StripMarkdown(string text)
        {
            text = Regex.Replace(text, @"\*\*(.+?)\*\*", "$1");
            text = Regex.Replace(text, @"__(.+?)__", "$1");
            text = Regex.Replace(text, @"^\s{0,3}#{1,6}\s+", "", RegexOptions.Multiline);
            text = text.Replace(@"\(", "").Replace(@"\)", "").Replace(@"\[", "").Replace(@"\]", "");
            return text.Trim();
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

            [JsonPropertyName("reasoning_effort")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? ReasoningEffort { get; set; }
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

            [JsonPropertyName("finish_reason")]
            public string? FinishReason { get; set; }
        }
    }
}
