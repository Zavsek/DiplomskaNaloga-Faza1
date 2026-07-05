using Microsoft.EntityFrameworkCore;
using Poskus1.Data;
using Poskus1.DTOs;

namespace Poskus1.Services
{
    public interface IUserStatisticsService
    {
        Task<UserStatisticsDto> BuildUserStatisticsAsync(int userId, CancellationToken cancellationToken = default);
    }

    public class UserStatisticsService : IUserStatisticsService
    {
        private readonly AppDbContext _dbContext;

        public UserStatisticsService(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<UserStatisticsDto> BuildUserStatisticsAsync(int userId, CancellationToken cancellationToken = default)
        {
            var latestSessionIds = await _dbContext.GameSessions
                .AsNoTracking()
                .Where(gs => gs.userId == userId)
                .GroupBy(gs => gs.quizId)
                .Select(group => group
                    .OrderByDescending(x => x.startedAtUtc)
                    .ThenByDescending(x => x.id)
                    .Select(x => x.id)
                    .First())
                .ToListAsync(cancellationToken);

            if (latestSessionIds.Count == 0)
            {
                return new UserStatisticsDto
                {
                    questionsAnswered = 0,
                    correctProcentage = 0,
                    avgAnwserTime = 0,
                    mostCommonAnwser = null,
                    longestStreak = 0,
                    avgWastedTimeOnWrongAnswers = 0
                };
            }

            var sessions = await _dbContext.GameSessions
                .AsNoTracking()
                .Where(gs => latestSessionIds.Contains(gs.id))
                .Include(gs => gs.answers)
                    .ThenInclude(answer => answer.question)
                .ToListAsync(cancellationToken);

            var attempts = await _dbContext.GameAnswerAttempts
                .AsNoTracking()
                .Where(attempt => latestSessionIds.Contains(attempt.gameSessionId))
                .Include(attempt => attempt.question)
                .ToListAsync(cancellationToken);

            if (attempts.Count == 0)
            {
                return new UserStatisticsDto
                {
                    questionsAnswered = 0,
                    correctProcentage = 0,
                    avgAnwserTime = 0,
                    mostCommonAnwser = null,
                    longestStreak = 0,
                    avgWastedTimeOnWrongAnswers = 0
                };
            }

            var finalAnswers = sessions
                .SelectMany(session => session.answers.Select(answer => new
                {
                    Session = session,
                    Answer = answer
                }))
                .ToList();

            var perAnswerDurations = new List<double>(attempts.Count);
            var wastedDurations = new List<double>();
            var sequenceWithCorrectness = new List<bool>(attempts.Count);

            foreach (var group in attempts.GroupBy(x => x.gameSessionId))
            {
                var session = sessions.First(x => x.id == group.Key);
                var orderedAttempts = group
                    .OrderBy(x => x.submittedAtUtc)
                    .ThenBy(x => x.id)
                    .ToList();

                DateTime previousCheckpoint = session.startedAtUtc;
                foreach (var attempt in orderedAttempts)
                {
                    var questionStartedAt = previousCheckpoint;
                    var firstSubmitDelta = (attempt.submittedAtUtc - questionStartedAt).TotalSeconds;
                    if (firstSubmitDelta < 0)
                    {
                        firstSubmitDelta = 0;
                    }

                    perAnswerDurations.Add(firstSubmitDelta);
                    previousCheckpoint = attempt.submittedAtUtc;

                    var isCorrect = char.ToUpperInvariant(attempt.submittedAnswer) == char.ToUpperInvariant(attempt.question.answer);
                    sequenceWithCorrectness.Add(isCorrect);
                }
            }

            foreach (var wrongFinalAnswer in finalAnswers.Where(x =>
                char.ToUpperInvariant(x.Answer.selectedAnswer) != char.ToUpperInvariant(x.Answer.question.answer)))
            {
                var questionAttempts = attempts
                    .Where(a => a.gameSessionId == wrongFinalAnswer.Session.id && a.questionId == wrongFinalAnswer.Answer.questionId)
                    .OrderBy(a => a.submittedAtUtc)
                    .ThenBy(a => a.id)
                    .ToList();

                if (questionAttempts.Count == 0)
                {
                    continue;
                }

                var spentSeconds = (wrongFinalAnswer.Answer.lastUpdatedAtUtc - questionAttempts[0].submittedAtUtc).TotalSeconds;
                if (spentSeconds < 0)
                {
                    spentSeconds = 0;
                }

                wastedDurations.Add(spentSeconds);
            }

            var questionsAnswered = attempts.Count;
            var correctCount = finalAnswers.Count(x =>
                char.ToUpperInvariant(x.Answer.selectedAnswer) == char.ToUpperInvariant(x.Answer.question.answer));

            var avgAnswerTime = perAnswerDurations.Count == 0 ? 0 : perAnswerDurations.Average();
            var avgWastedWrong = wastedDurations.Count == 0 ? 0 : wastedDurations.Average();

            var mostCommon = attempts
                .GroupBy(x => char.ToUpperInvariant(x.submittedAnswer))
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .Select(g => g.Key.ToString())
                .FirstOrDefault();

            var longestStreak = 0;
            var runningStreak = 0;
            foreach (var isCorrect in sequenceWithCorrectness)
            {
                if (isCorrect)
                {
                    runningStreak++;
                    if (runningStreak > longestStreak)
                    {
                        longestStreak = runningStreak;
                    }
                }
                else
                {
                    runningStreak = 0;
                }
            }

            return new UserStatisticsDto
            {
                questionsAnswered = questionsAnswered,
                correctProcentage = finalAnswers.Count == 0 ? 0 : Math.Round((double)correctCount / finalAnswers.Count * 100, 2),
                avgAnwserTime = Math.Round(avgAnswerTime, 2),
                mostCommonAnwser = mostCommon,
                longestStreak = longestStreak,
                avgWastedTimeOnWrongAnswers = Math.Round(avgWastedWrong, 2)
            };
        }
    }
}
