using Microsoft.EntityFrameworkCore;
using Poskus1.Data;
using Poskus1.DTOs;
using System.Linq;

namespace Poskus1.Services
{
    public class StatisticsService
    {
        private readonly AppDbContext _context;

        public StatisticsService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<UserStatisticsDto> CalculateUserStatisticsAsync(int userId)
        {
            var sessions = await _context.QuizSessions
                .Include(s => s.Answers)
                .ThenInclude(a => a.Question)
                .Where(s => s.UserId == userId)
                .ToListAsync();

            // Spremeni še kviz da se v statistiki upošteva samo zadnja poskus kviza
            var latestSessions = sessions
                .GroupBy(s => s.QuizId)
                .Select(g => g.OrderByDescending(s => s.StartTime).First())
                .ToList();

            var allValidAnswers = latestSessions.SelectMany(s => s.Answers).ToList();

            if (!allValidAnswers.Any())
            {
                return new UserStatisticsDto(); // empty stats
            }

            int totalAnswers = allValidAnswers.Count;
            int correctAnswersCount = allValidAnswers.Count(a => char.ToUpper(a.SubmittedAnswer) == char.ToUpper(a.Question.answer));
            
            double correctProcentage = (double)correctAnswersCount / totalAnswers;

            // Most common answer
            var mostCommonAnswer = allValidAnswers
                .GroupBy(a => char.ToUpper(a.SubmittedAnswer))
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault();

            // Longest streak
            // We order all answers globally by AnsweredAt across the latest sessions
            var chronologicalAnswers = allValidAnswers.OrderBy(a => a.AnsweredAt).ToList();
            int longestStreak = 0;
            int currentStreak = 0;
            foreach (var a in chronologicalAnswers)
            {
                if (char.ToUpper(a.SubmittedAnswer) == char.ToUpper(a.Question.answer))
                {
                    currentStreak++;
                    if (currentStreak > longestStreak) longestStreak = currentStreak;
                }
                else
                {
                    currentStreak = 0;
                }
            }

            // Average answer time & average wasted time on wrong answers
            double totalAnswerTimeMs = 0;
            double wastedTimeMs = 0;
            int wrongAnswersCount = totalAnswers - correctAnswersCount;

            foreach (var session in latestSessions)
            {
                var sessionAnswers = session.Answers.OrderBy(a => a.AnsweredAt).ToList();
                DateTime lastTime = session.StartTime;

                foreach (var answer in sessionAnswers)
                {
                    double timeTaken = (answer.AnsweredAt - lastTime).TotalMilliseconds;
                    if (timeTaken < 0) timeTaken = 0;

                    totalAnswerTimeMs += timeTaken;

                    if (char.ToUpper(answer.SubmittedAnswer) != char.ToUpper(answer.Question.answer))
                    {
                        wastedTimeMs += timeTaken;
                    }

                    lastTime = answer.AnsweredAt;
                }
            }

            double avgAnswerTime = totalAnswers > 0 ? totalAnswerTimeMs / totalAnswers : 0;
            double avgWastedTime = wrongAnswersCount > 0 ? wastedTimeMs / wrongAnswersCount : 0;

            return new UserStatisticsDto
            {
                questionsAnswered = totalAnswers,
                correctProcentage = correctProcentage,
                avgAnwserTime = avgAnswerTime,
                mostCommonAnwser = mostCommonAnswer,
                longestStreak = longestStreak,
                avgWastedTimeOnWrongAnswers = avgWastedTime
            };
        }
    }
}
