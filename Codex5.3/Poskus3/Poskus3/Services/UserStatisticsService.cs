using Microsoft.EntityFrameworkCore;
using Poskus3.Data;

namespace Poskus3.Services
{
    public class UserStatisticsService
    {
        private readonly AppDbContext _dbContext;

        public UserStatisticsService(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<object> BuildStatisticsAsync(int userId)
        {
            var userSessions = await _dbContext.GameSessions
                .Where(gs => gs.userId == userId)
                .Select(gs => new { gs.id, gs.quizId, gs.startedAtUtc })
                .ToListAsync();

            var latestSessionIds = userSessions
                .GroupBy(gs => gs.quizId)
                .Select(g => g.OrderByDescending(x => x.startedAtUtc).ThenByDescending(x => x.id).First().id)
                .ToList();

            if (latestSessionIds.Count == 0)
            {
                return EmptyStatistics();
            }

            var latestAnswers = await _dbContext.AnswerSubmissions
                .Where(s => latestSessionIds.Contains(s.gameSessionId))
                .Join(
                    _dbContext.Questions,
                    s => s.questionId,
                    q => q.id,
                    (s, q) => new
                    {
                        s.id,
                        s.gameSessionId,
                        s.questionId,
                        selectedAnswer = s.selectedAnswer,
                        responseTimeMs = s.responseTimeMs,
                        s.submittedAtUtc,
                        correctAnswer = q.answer
                    })
                .OrderBy(x => x.submittedAtUtc)
                .ThenBy(x => x.id)
                .ToListAsync();

            if (latestAnswers.Count == 0)
            {
                return EmptyStatistics();
            }

            var questionsAnswered = latestAnswers.Count;
            var correctCount = latestAnswers.Count(x => x.selectedAnswer == x.correctAnswer);
            var correctPercentage = Math.Round((double)correctCount / questionsAnswered * 100.0, 2);
            var avgAnswerTimeSeconds = Math.Round(latestAnswers.Average(x => x.responseTimeMs) / 1000.0, 2);

            var mostCommonAnswer = latestAnswers
                .GroupBy(x => x.selectedAnswer)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .Select(g => g.Key.ToString())
                .FirstOrDefault();

            var longestStreak = 0;
            var currentStreak = 0;
            foreach (var submission in latestAnswers)
            {
                if (submission.selectedAnswer == submission.correctAnswer)
                {
                    currentStreak++;
                    if (currentStreak > longestStreak)
                    {
                        longestStreak = currentStreak;
                    }
                }
                else
                {
                    currentStreak = 0;
                }
            }

            var wastedTimeOnWrongQuestions = latestAnswers
                .GroupBy(x => new { x.gameSessionId, x.questionId })
                .Select(g => new
                {
                    totalTimeMs = g.Sum(x => x.responseTimeMs),
                    finalIsWrong = g.OrderBy(x => x.submittedAtUtc).ThenBy(x => x.id).Last().selectedAnswer != g.OrderBy(x => x.submittedAtUtc).ThenBy(x => x.id).Last().correctAnswer
                })
                .Where(x => x.finalIsWrong)
                .Select(x => x.totalTimeMs)
                .ToList();

            var avgWastedTimeSeconds = wastedTimeOnWrongQuestions.Count == 0
                ? 0.0
                : Math.Round(wastedTimeOnWrongQuestions.Average() / 1000.0, 2);

            return new
            {
                questionsAnswered,
                correctProcentage = correctPercentage,
                avgAnwserTime = avgAnswerTimeSeconds,
                mostCommonAnwser = mostCommonAnswer,
                longestStreak,
                avgWastedTimeOnWrongAnswers = avgWastedTimeSeconds
            };
        }

        private static object EmptyStatistics() => new
        {
            questionsAnswered = 0,
            correctProcentage = 0.0,
            avgAnwserTime = 0.0,
            mostCommonAnwser = (string?)null,
            longestStreak = 0,
            avgWastedTimeOnWrongAnswers = 0.0
        };
    }
}
