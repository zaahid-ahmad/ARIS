namespace ARIS1.Services
{
    public record ChatConcern(string Subject, string Level, List<string> Topics);

    public interface IChatAssistantService
    {
        Task<string> GetResponseAsync(string userInput, IReadOnlyList<ChatConcern> concerns);
    }
}
