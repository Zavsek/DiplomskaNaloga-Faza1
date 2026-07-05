using Microsoft.EntityFrameworkCore;
using Poskus2.Data;
using Poskus2.DTOs;

namespace Poskus2.Services
{
    public class StatisticsService
    {
        private readonly AppDbContext _db;

        public StatisticsService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<UserStatisticsDto> ComputeForUserAsync(int userId)
        {
            // Samo zadnji poskus vsakega kviza (isLatest = true ali NULL = legacy)
            var sessions = await _db.UserGameSessions
                .Where(ugs =>
                    ugs.userId == userId &&
                    (ugs.isLatest == true || ugs.isLatest == null))
                .Select(ugs => ugs.id)
                .ToListAsync();

            if (sessions.Count == 0)
                return EmptyStats();

            var answers = await _db.UserAnswers
                .Include(ua => ua.question)
                .Where(ua => sessions.Contains(ua.userGameSessionId))
                .ToListAsync();

            if (answers.Count == 0)
                return EmptyStats();

            int questionsAnswered = answers.Count;

            int correctCount = answers.Count(a => a.answer == a.question.answer);
            double correctProcentage = Math.Round((double)correctCount / questionsAnswered * 100, 2);

            // avgAnwserTime: povprečen čas od joinedAt seje do firstAnsweredAt odgovora.
            // Za odgovore brez firstAnsweredAt (legacy) se čas ne upošteva v povprečju.
            var joinedAtMap = await _db.UserGameSessions
                .Where(ugs => sessions.Contains(ugs.id))
                .ToDictionaryAsync(ugs => ugs.id, ugs => ugs.joinedAt);

            var answerTimes = answers
                .Where(a => a.firstAnsweredAt.HasValue)
                .Select(a => (a.firstAnsweredAt!.Value - joinedAtMap[a.userGameSessionId]).TotalSeconds)
                .Where(t => t >= 0)
                .ToList();

            double avgAnwserTime = answerTimes.Count > 0
                ? Math.Round(answerTimes.Average(), 2)
                : 0;

            // mostCommonAnwser
            var mostCommon = answers
                .GroupBy(a => a.answer)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .First()
                .Key
                .ToString();

            // longestStreak: zaporedni pravilni odgovori (sortirani po answeredAt)
            var sortedAnswers = answers.OrderBy(a => a.answeredAt).ToList();
            int longestStreak = 0;
            int currentStreak = 0;
            foreach (var a in sortedAnswers)
            {
                if (a.answer == a.question.answer)
                {
                    currentStreak++;
                    if (currentStreak > longestStreak) longestStreak = currentStreak;
                }
                else
                {
                    currentStreak = 0;
                }
            }

            // avgWastedTimeOnWrongAnswers: povprečen čas za napačno odgovorjena vprašanja
            var wrongAnswerTimes = answers
                .Where(a => a.answer != a.question.answer && a.firstAnsweredAt.HasValue)
                .Select(a => (a.firstAnsweredAt!.Value - joinedAtMap[a.userGameSessionId]).TotalSeconds)
                .Where(t => t >= 0)
                .ToList();

            double avgWastedTimeOnWrongAnswers = wrongAnswerTimes.Count > 0
                ? Math.Round(wrongAnswerTimes.Average(), 2)
                : 0;

            return new UserStatisticsDto
            {
                questionsAnswered = questionsAnswered,
                correctProcentage = correctProcentage,
                avgAnwserTime = avgAnwserTime,
                mostCommonAnwser = mostCommon,
                longestStreak = longestStreak,
                avgWastedTimeOnWrongAnswers = avgWastedTimeOnWrongAnswers
            };
        }

        private static UserStatisticsDto EmptyStats() => new()
        {
            questionsAnswered = 0,
            correctProcentage = 0,
            avgAnwserTime = 0,
            mostCommonAnwser = "-",
            longestStreak = 0,
            avgWastedTimeOnWrongAnswers = 0
        };
    }
}
