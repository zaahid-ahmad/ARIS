namespace ARIS1.Services
{
    public class RuleBasedChatAssistantService : IChatAssistantService
    {
        private static readonly string[] Greetings = { "hi", "hello", "hey", "help", "start", "sup" };

        private static readonly Dictionary<string, string> LevelTips = new()
        {
            ["Critical"] = "You're at a critical level in {0}. Go back to the core definitions and worked examples before attempting harder problems — build the foundation first.",
            ["Attention"] = "{0} needs attention. Revisit the topic notes, then work through a few guided practice questions before trying past papers.",
            ["Focus"] = "You're close on {0} — focus your practice on the specific question types you're missing rather than reviewing everything again.",
            ["Minor"] = "Only minor gaps left in {0}. A quick review of your mistakes on past attempts should close them.",
            ["WellDone"] = "You're doing well in {0} — keep practicing to maintain it, and consider helping revise with a study partner."
        };

        public Task<string> GetResponseAsync(string userInput, IReadOnlyList<ChatConcern> concerns)
        {
            var input = userInput.Trim().ToLowerInvariant();

            if (concerns.Count == 0)
            {
                return Task.FromResult(
                    "You don't have any flagged areas of concern right now — nice work! Ask me about a specific subject once one comes up.");
            }

            if (Greetings.Any(g => input == g || input.Contains(g)))
            {
                var subjectList = string.Join(", ", concerns.Select(c => c.Subject).Distinct());
                return Task.FromResult(
                    $"Hi! I can help with your current areas of concern: {subjectList}. Ask me about one of them, or click an area on the left.");
            }

            var match = concerns
                .Where(c => input.Contains(c.Subject.ToLowerInvariant())
                            || c.Topics.Any(t => input.Contains(t.ToLowerInvariant())))
                .OrderByDescending(c => GetLevelScore(c.Level))
                .FirstOrDefault();

            if (match != null)
            {
                var topicText = match.Topics.Count > 0 ? string.Join(", ", match.Topics) : match.Subject;
                var template = LevelTips.TryGetValue(match.Level, out var t)
                    ? t
                    : "Keep working on {0} — steady practice is the fastest way to improve.";
                return Task.FromResult(string.Format(template, topicText));
            }

            var subjects = string.Join(", ", concerns.Select(c => c.Subject).Distinct());
            return Task.FromResult(
                $"I can only help with your current subjects: {subjects}. Try asking about one of those, or select an area of concern on the left.");
        }

        private static int GetLevelScore(string level) => level switch
        {
            "Critical" => 4,
            "Attention" => 3,
            "Focus" => 2,
            "Minor" => 1,
            _ => 0
        };
    }
}
