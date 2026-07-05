using Microsoft.EntityFrameworkCore;
using Poskus1.Data;
using Poskus1.DTOs;

namespace Poskus1.Services
{
    public class StatisticsService
    {
        private readonly AppDbContext _db;

        public StatisticsService(AppDbContext db)
        {
            _db = db;
        }

        public async Task<UserStatisticsDto> GetUserStatisticsAsync(int userId)
        {
            // Pridobi samo zadnje poskuse (isLatestAttempt = true) tega uporabnika
            var latestSessionIds = await _db.GameSessions
                .Where(gs => gs.userId == userId && gs.isLatestAttempt)
                .Select(gs => gs.id)
                .ToListAsync();

            if (latestSessionIds.Count == 0)
                return new UserStatisticsDto();

            // Pridobi vse odgovore iz zadnjih poskusov skupaj s podatki o vprašanju in seji
            var answers = await _db.UserAnswers
                .Include(ua => ua.question)
                .Include(ua => ua.session)
                .Where(ua => latestSessionIds.Contains(ua.sessionId))
                .OrderBy(ua => ua.sessionId)
                .ThenBy(ua => ua.answeredAt)
                .ToListAsync();

            if (answers.Count == 0)
                return new UserStatisticsDto();

            int questionsAnswered = answers.Count;

            // correctProcentage
            int correctCount = answers.Count(ua => ua.selectedAnswer == ua.question.answer);
            double correctProcentage = questionsAnswered > 0
                ? Math.Round((double)correctCount / questionsAnswered * 100, 2)
                : 0;

            // avgAnswerTime — čas od začetka seje do oddaje odgovora za vsako vprašanje
            // Za vsako sejo priskrbimo startedAt
            var sessionStartMap = answers
                .Select(ua => ua.session)
                .DistinctBy(s => s.id)
                .ToDictionary(s => s.id, s => s.startedAt);

            // Sortiramo odgovore znotraj vsake seje po answeredAt, da izračunamo čas med odgovori
            // Čas za 1. odgovor = answeredAt - sessionStart
            // Čas za vsak naslednji = razlika od prejšnjega answeredAt
            var answerTimes = new List<double>();
            var groupedBySessions = answers.GroupBy(ua => ua.sessionId);

            foreach (var group in groupedBySessions)
            {
                var sessionStart = sessionStartMap[group.Key];
                var sorted = group.OrderBy(ua => ua.answeredAt).ToList();
                DateTime prev = sessionStart;
                foreach (var ua in sorted)
                {
                    var elapsed = (ua.answeredAt - prev).TotalSeconds;
                    if (elapsed >= 0) answerTimes.Add(elapsed);
                    prev = ua.answeredAt;
                }
            }

            double avgAnwserTime = answerTimes.Count > 0
                ? Math.Round(answerTimes.Average(), 2)
                : 0;

            // mostCommonAnswer
            var answerCounts = answers
                .GroupBy(ua => ua.selectedAnswer)
                .ToDictionary(g => g.Key, g => g.Count());

            char mostCommonChar = answerCounts
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key)
                .First().Key;

            // longestStreak — zaporedni pravilni odgovori (po seji in answeredAt)
            int longestStreak = 0;
            int currentStreak = 0;
            foreach (var group in groupedBySessions)
            {
                var sorted = group.OrderBy(ua => ua.answeredAt).ToList();
                foreach (var ua in sorted)
                {
                    if (ua.selectedAnswer == ua.question.answer)
                    {
                        currentStreak++;
                        if (currentStreak > longestStreak) longestStreak = currentStreak;
                    }
                    else
                    {
                        currentStreak = 0;
                    }
                }
                // streak se prelomi med sejami
                currentStreak = 0;
            }

            // avgWastedTimeOnWrongAnswers — povprečen čas porabljen za napačno odgovorjena vprašanja
            var wastedTimes = new List<double>();
            foreach (var group in groupedBySessions)
            {
                var sessionStart = sessionStartMap[group.Key];
                var sorted = group.OrderBy(ua => ua.answeredAt).ToList();
                DateTime prev = sessionStart;
                foreach (var ua in sorted)
                {
                    var elapsed = (ua.answeredAt - prev).TotalSeconds;
                    if (elapsed >= 0 && ua.selectedAnswer != ua.question.answer)
                        wastedTimes.Add(elapsed);
                    prev = ua.answeredAt;
                }
            }

            double avgWastedTimeOnWrongAnswers = wastedTimes.Count > 0
                ? Math.Round(wastedTimes.Average(), 2)
                : 0;

            return new UserStatisticsDto
            {
                questionsAnswered = questionsAnswered,
                correctProcentage = correctProcentage,
                avgAnwserTime = avgAnwserTime,
                mostCommonAnwser = mostCommonChar.ToString(),
                longestStreak = longestStreak,
                avgWastedTimeOnWrongAnswers = avgWastedTimeOnWrongAnswers
            };
        }
    }
}
