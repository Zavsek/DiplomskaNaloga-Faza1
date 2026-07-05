using Microsoft.EntityFrameworkCore;
using Poskus2.Data;
using Poskus2.DTOs.Statistics;

namespace Poskus2.Services
{
    public class UserStatisticsService
    {
        private readonly AppDbContext _dbContext;

        public UserStatisticsService(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<UserStatisticsDto> BuildForUserAsync(int userId, CancellationToken cancellationToken = default)
        {
            var latestSessionIds = await _dbContext.GameSessions
                .AsNoTracking()
                .Where(gs => gs.userId == userId)
                .GroupBy(gs => gs.quizId)
                .Select(g => g
                    .OrderByDescending(x => x.startedAtUtc)
                    .ThenByDescending(x => x.id)
                    .Select(x => x.id)
                    .First())
                .ToListAsync(cancellationToken);

            if (latestSessionIds.Count == 0)
            {
                return new UserStatisticsDto();
            }

            var sessions = await _dbContext.GameSessions
                .AsNoTracking()
                .Where(gs => latestSessionIds.Contains(gs.id))
                .ToDictionaryAsync(gs => gs.id, cancellationToken);
            var latestQuizIds = sessions.Values.Select(s => s.quizId).Distinct().ToList();

            var attempts = await _dbContext.GameAnswerAttempts
                .AsNoTracking()
                .Where(a => latestSessionIds.Contains(a.gameSessionId))
                .OrderBy(a => a.submittedAtUtc)
                .ThenBy(a => a.id)
                .Select(a => new
                {
                    a.id,
                    a.gameSessionId,
                    a.questionId,
                    a.selectedAnswer,
                    a.isCorrect,
                    a.submittedAtUtc
                })
                .ToListAsync(cancellationToken);

            if (attempts.Count == 0)
            {
                return new UserStatisticsDto();
            }

            var finalAnswers = await _dbContext.GameAnswers
                .AsNoTracking()
                .Where(a => latestSessionIds.Contains(a.gameSessionId))
                .Select(a => new { a.gameSessionId, a.questionId, a.selectedAnswer })
                .ToListAsync(cancellationToken);

            var questionCorrectMap = await _dbContext.Questions
                .AsNoTracking()
                .Where(q => latestQuizIds.Contains(q.quizId))
                .Select(q => new { q.id, q.answer })
                .ToDictionaryAsync(x => x.id, x => char.ToUpperInvariant(x.answer), cancellationToken);

            var wrongFinalQuestions = finalAnswers
                .Where(a => questionCorrectMap.TryGetValue(a.questionId, out var correct) &&
                            char.ToUpperInvariant(a.selectedAnswer) != correct)
                .Select(a => (a.gameSessionId, a.questionId))
                .ToHashSet();

            var totalAttempts = attempts.Count;
            var correctAttempts = attempts.Count(a => a.isCorrect);
            var correctPercentage = totalAttempts == 0 ? 0 : (double)correctAttempts / totalAttempts * 100d;

            var streak = 0;
            var maxStreak = 0;
            foreach (var attempt in attempts)
            {
                if (attempt.isCorrect)
                {
                    streak++;
                    if (streak > maxStreak)
                    {
                        maxStreak = streak;
                    }
                }
                else
                {
                    streak = 0;
                }
            }

            var mostCommonAnswer = attempts
                .GroupBy(a => char.ToUpperInvariant(a.selectedAnswer))
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .Select(g => g.Key.ToString())
                .FirstOrDefault();

            var answerDurations = new List<double>();
            var perAttemptDuration = new Dictionary<int, double>();
            foreach (var group in attempts.GroupBy(a => a.gameSessionId))
            {
                if (!sessions.TryGetValue(group.Key, out var session))
                {
                    continue;
                }

                DateTimeOffset previous = session.startedAtUtc;
                foreach (var attempt in group.OrderBy(x => x.submittedAtUtc).ThenBy(x => x.id))
                {
                    var duration = (attempt.submittedAtUtc - previous).TotalSeconds;
                    if (duration < 0)
                    {
                        duration = 0;
                    }

                    answerDurations.Add(duration);
                    perAttemptDuration[attempt.id] = duration;
                    previous = attempt.submittedAtUtc;
                }
            }

            var averageAnswerTime = answerDurations.Count == 0 ? 0 : answerDurations.Average();

            var wrongQuestionDurations = attempts
                .Where(a => wrongFinalQuestions.Contains((a.gameSessionId, a.questionId)))
                .GroupBy(a => (a.gameSessionId, a.questionId))
                .Select(g => g.Sum(attempt => perAttemptDuration.GetValueOrDefault(attempt.id, 0)))
                .ToList();

            var averageWastedTimeOnWrongAnswers = wrongQuestionDurations.Count == 0
                ? 0
                : wrongQuestionDurations.Average();

            return new UserStatisticsDto
            {
                questionsAnswered = totalAttempts,
                correctProcentage = Math.Round(correctPercentage, 2),
                avgAnwserTime = Math.Round(averageAnswerTime, 2),
                mostCommonAnwser = mostCommonAnswer,
                longestStreak = maxStreak,
                avgWastedTimeOnWrongAnswers = Math.Round(averageWastedTimeOnWrongAnswers, 2)
            };
        }
    }
}
