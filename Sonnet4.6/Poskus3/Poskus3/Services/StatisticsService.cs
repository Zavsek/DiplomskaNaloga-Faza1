using Microsoft.EntityFrameworkCore;
using Poskus3.Data;

namespace Poskus3.Services
{
    public class UserStatsDto
    {
        public int questionsAnswered { get; set; }
        public double correctProcentage { get; set; }
        public double avgAnswerTime { get; set; }
        public string mostCommonAnswer { get; set; } = "-";
        public int longestStreak { get; set; }
        public double avgWastedTimeOnWrongAnswers { get; set; }
    }

    public class StatisticsService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public StatisticsService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task<UserStatsDto> ComputeForUserAsync(int userId)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Pridobi vse odgovore iz zadnjih poskusov vsakega kviza tega uporabnika
            // UserAnswer -> GameSession (isLatestAttempt=true, userId=userId) -> Question (za pravilni odgovor)
            var answers = await db.UserAnswers
                .Include(ua => ua.session)
                .Include(ua => ua.question)
                .Where(ua =>
                    ua.session.userId == userId &&
                    ua.session.isLatestAttempt)
                .OrderBy(ua => ua.session.quizId)
                .ThenBy(ua => ua.question.orderIndex)
                .Select(ua => new
                {
                    ua.answer,
                    ua.answeredAt,
                    sessionStartedAt = ua.session.startedAt,
                    correctAnswer = ua.question.answer,
                    orderIndex = ua.question.orderIndex
                })
                .ToListAsync();

            if (answers.Count == 0)
                return new UserStatsDto();

            // 1. questionsAnswered
            int questionsAnswered = answers.Count;

            // 2. correctProcentage
            int correct = answers.Count(a => a.answer == a.correctAnswer);
            double correctProcentage = Math.Round((double)correct / questionsAnswered * 100, 2);

            // 3. avgAnswerTime — povprečje (answeredAt - sessionStartedAt) za vsak odgovor v sekundah
            //    Ker se answeredAt nastavi ob oddaji in sessionStartedAt ob začetku,
            //    kot proxy za čas na vprašanje vzamemo razliko med zaporednima odgovoroma znotraj seje.
            //    Za prvo vprašanje v seji je čas = answeredAt - sessionStartedAt.
            //    Za vsako naslednje: čas = answeredAt[i] - answeredAt[i-1].
            var answerTimes = new List<double>();
            // Grupiramo po seji
            var bySessions = answers
                .GroupBy(a => new { a.sessionStartedAt })
                .ToList();

            foreach (var sessionGroup in bySessions)
            {
                var sessionAnswers = sessionGroup.OrderBy(a => a.answeredAt).ToList();
                for (int i = 0; i < sessionAnswers.Count; i++)
                {
                    double seconds = i == 0
                        ? (sessionAnswers[i].answeredAt - sessionAnswers[i].sessionStartedAt).TotalSeconds
                        : (sessionAnswers[i].answeredAt - sessionAnswers[i - 1].answeredAt).TotalSeconds;
                    if (seconds >= 0)
                        answerTimes.Add(seconds);
                }
            }

            double avgAnswerTime = answerTimes.Count > 0
                ? Math.Round(answerTimes.Average(), 2)
                : 0.0;

            // 4. mostCommonAnswer
            var mostCommon = answers
                .GroupBy(a => a.answer)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .First();
            string mostCommonAnswer = mostCommon.Key.ToString();

            // 5. longestStreak — zaporedni pravilni odgovori (po orderIndex znotraj iste seje)
            int longestStreak = 0;
            foreach (var sessionGroup in bySessions)
            {
                var sessionAnswers = sessionGroup.OrderBy(a => a.orderIndex).ToList();
                int currentStreak = 0;
                foreach (var a in sessionAnswers)
                {
                    if (a.answer == a.correctAnswer)
                    {
                        currentStreak++;
                        if (currentStreak > longestStreak)
                            longestStreak = currentStreak;
                    }
                    else
                    {
                        currentStreak = 0;
                    }
                }
            }

            // 6. avgWastedTimeOnWrongAnswers — povprečen čas za napačno odgovorjena vprašanja
            var wrongTimes = new List<double>();
            foreach (var sessionGroup in bySessions)
            {
                var sessionAnswers = sessionGroup.OrderBy(a => a.answeredAt).ToList();
                for (int i = 0; i < sessionAnswers.Count; i++)
                {
                    if (sessionAnswers[i].answer != sessionAnswers[i].correctAnswer)
                    {
                        double seconds = i == 0
                            ? (sessionAnswers[i].answeredAt - sessionAnswers[i].sessionStartedAt).TotalSeconds
                            : (sessionAnswers[i].answeredAt - sessionAnswers[i - 1].answeredAt).TotalSeconds;
                        if (seconds >= 0)
                            wrongTimes.Add(seconds);
                    }
                }
            }

            double avgWastedTimeOnWrongAnswers = wrongTimes.Count > 0
                ? Math.Round(wrongTimes.Average(), 2)
                : 0.0;

            return new UserStatsDto
            {
                questionsAnswered = questionsAnswered,
                correctProcentage = correctProcentage,
                avgAnswerTime = avgAnswerTime,
                mostCommonAnswer = mostCommonAnswer,
                longestStreak = longestStreak,
                avgWastedTimeOnWrongAnswers = avgWastedTimeOnWrongAnswers
            };
        }
    }
}
