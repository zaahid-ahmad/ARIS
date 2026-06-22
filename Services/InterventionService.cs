using ARIS1.Data;
using ARIS1.Models;
using Microsoft.EntityFrameworkCore;

namespace ARIS1.Services
{
    public class InterventionService
    {
        private readonly AppDbContext _dbContext;

        public InterventionService(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task GenerateInterventions(int learnerId, int questionId, float marksAwarded, float maxMark)
        {
            float percentage = (marksAwarded / maxMark) * 100;

            var question = await _dbContext.AssessmentQuestions
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.QuestionId == questionId);

            if (question == null) return;

            // Determine level and message
            string level;
            string message;
            string color;

            if (percentage <= 30)
            {
                level = "Critical";
                color = "red";
                message = $"Critical attention in {question.Topic}";
            }
            else if (percentage <= 55)
            {
                level = "Attention";
                color = "orange";
                message = $"Attention needed in {question.Topic}";
            }
            else if (percentage <= 65)
            {
                level = "Focus";
                color = "yellow";
                message = $"Focus on {question.Topic}";
            }
            else if (percentage <= 79)
            {
                level = "Minor";
                color = "lightgreen";
                message = $"Minor improvements needed in {question.Topic}";
            }
            else
            {
                level = "WellDone";
                color = "green";
                message = $"Well done in {question.Topic}";
            }

            // Check if intervention already exists
            var existing = await _dbContext.Interventions
                .FirstOrDefaultAsync(i =>
                    i.LearnerId == learnerId &&
                    i.QuestionId == questionId);

            if (existing != null)
            {
                // Update existing intervention
                existing.PercentageScore = percentage;
                existing.Level = level;
                existing.Message = message;
                existing.CreatedDate = DateTime.Now;
                existing.IsResolved = false;
                _dbContext.Interventions.Update(existing);
            }
            else
            {
                // Create new intervention
                var intervention = new Intervention
                {
                    LearnerId = learnerId,
                    QuestionId = questionId,
                    Topic = question.Topic,
                    PercentageScore = percentage,
                    Level = level,
                    Message = message,
                    CreatedDate = DateTime.Now,
                    IsResolved = false
                };
                _dbContext.Interventions.Add(intervention);
            }

            await _dbContext.SaveChangesAsync();
        }
    }
}