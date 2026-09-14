namespace ARIS1.Services
{
    public record ChatConcern(string Subject, string Level, List<string> Topics);

    // Everything the assistant knows about the learner: their grade and current-year enrolled
    // subjects define the CAPS scope; flagged concerns are the priorities within it.
    public record ChatContext(int Grade, IReadOnlyList<string> EnrolledSubjects, IReadOnlyList<ChatConcern> Concerns);

    public interface IChatAssistantService
    {
        Task<string> GetResponseAsync(string userInput, ChatContext context);
    }
}
