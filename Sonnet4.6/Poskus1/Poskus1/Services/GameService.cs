using Microsoft.EntityFrameworkCore;
using Poskus1.Data;
using Poskus1.DTOs;
using Poskus1.Entities;

namespace Poskus1.Services
{
    public class GameService
    {
        private readonly AppDbContext _db;
        private readonly QuizProgressTracker _tracker;

        public GameService(AppDbContext db, QuizProgressTracker tracker)
        {
            _db = db;
            _tracker = tracker;
        }

        public async Task<(bool success, object? result, string message)> StartGameAsync(int quizId, int userId)
        {
            var quiz = await _db.Quizzes
                .Include(q => q.questions.OrderBy(q => q.orderIndex))
                .FirstOrDefaultAsync(q => q.id == quizId);

            if (quiz == null)
                return (false, null, "Kviz ne obstaja.");

            if (quiz.questions.Count == 0)
                return (false, null, "Kviz ne vsebuje vprašanj.");

            // Preveri ali ima uporabnik že aktivno sejo za ta kviz
            var existingSession = await _db.GameSessions
                .FirstOrDefaultAsync(gs => gs.userId == userId && gs.quizId == quizId && !gs.isFinished);

            if (existingSession != null)
            {
                if (_tracker.IsSessionActive(existingSession.id))
                    return (false, null, "Že imate aktivno sejo za ta kviz.");

                // seja obstaja v bazi ampak timerja ni (npr. po restartu) — zaključi jo
                existingSession.isFinished = true;
                existingSession.finishedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            // Vse prejšnje seje tega kviza tega uporabnika označimo kot ne-zadnji-poskus
            await _db.GameSessions
                .Where(gs => gs.userId == userId && gs.quizId == quizId && gs.isLatestAttempt)
                .ExecuteUpdateAsync(s => s.SetProperty(gs => gs.isLatestAttempt, false));

            var now = DateTime.UtcNow;
            var session = new GameSession
            {
                userId = userId,
                quizId = quizId,
                startedAt = now,
                isFinished = false,
                isLatestAttempt = true
            };
            _db.GameSessions.Add(session);
            await _db.SaveChangesAsync();

            var activeSession = new ActiveSession
            {
                SessionId = session.id,
                UserId = userId,
                QuizId = quizId,
                TotalQuestions = quiz.questions.Count,
                ExpiresAt = now + quiz.duration,
                AnsweredCount = 0
            };
            _tracker.RegisterSession(activeSession);

            var firstQuestion = quiz.questions.First();
            return (true, new GameStartResponseDto
            {
                sessionId = session.id,
                quizTitle = quiz.title,
                totalQuestions = quiz.questions.Count,
                remainingSeconds = (activeSession.ExpiresAt - DateTime.UtcNow).TotalSeconds,
                firstQuestion = new QuestionSendDto(firstQuestion.id, firstQuestion.questionText)
            }, "Kviz je začet.");
        }

        public async Task<(bool success, object? result, string message)> SubmitAnswerAsync(
            int userId, AnswerSubmitDto dto)
        {
            var session = await GetActiveSessionForUserAsync(userId);
            if (session == null)
                return (false, null, "Nimate aktivne seje. Začnite kviz.");

            if (!_tracker.IsSessionActive(session.id))
                return (false, null, "Čas kviza je potekel. Kviz je zaključen.");

            var question = await _db.Questions
                .Include(q => q.quiz)
                .FirstOrDefaultAsync(q => q.id == dto.questionId);

            if (question == null || question.quizId != session.quizId)
                return (false, null, "Vprašanje ne pripada temu kvizu.");

            var selectedChar = dto.answer[0];
            var existing = await _db.UserAnswers
                .FirstOrDefaultAsync(ua => ua.sessionId == session.id && ua.questionId == dto.questionId);

            int editsRemaining;

            if (existing == null)
            {
                // nov odgovor
                _db.UserAnswers.Add(new UserAnswer
                {
                    sessionId = session.id,
                    questionId = dto.questionId,
                    selectedAnswer = selectedChar,
                    editCount = 0,
                    answeredAt = DateTime.UtcNow
                });
                editsRemaining = 2;
            }
            else
            {
                if (existing.editCount >= 2)
                    return (false, null, "Tega odgovora ne morete več popraviti (dosežena meja 2 popravkov).");

                existing.selectedAnswer = selectedChar;
                existing.editCount++;
                existing.answeredAt = DateTime.UtcNow;
                editsRemaining = 2 - existing.editCount;
            }

            await _db.SaveChangesAsync();

            // posodobi progress v trackerju
            var answeredCount = await _db.UserAnswers
                .CountAsync(ua => ua.sessionId == session.id);
            await _tracker.UpdateAnswerCountAsync(session.id, answeredCount);

            // poišči naslednje neodgovorjeno vprašanje
            var quiz = await _db.Quizzes
                .Include(q => q.questions.OrderBy(q => q.orderIndex))
                .FirstAsync(q => q.id == session.quizId);

            var answeredIds = await _db.UserAnswers
                .Where(ua => ua.sessionId == session.id)
                .Select(ua => ua.questionId)
                .ToListAsync();

            var nextQuestion = quiz.questions
                .FirstOrDefault(q => !answeredIds.Contains(q.id));

            return (true, new AnswerResultDto
            {
                accepted = true,
                message = nextQuestion == null ? "Vsa vprašanja so odgovorjena!" : "Odgovor sprejet.",
                editsRemaining = editsRemaining,
                nextQuestion = nextQuestion != null
                    ? new QuestionSendDto(nextQuestion.id, nextQuestion.questionText)
                    : null
            }, "OK");
        }

        public async Task<(bool success, object? result, string message)> GetQuestionAsync(
            int userId, int questionId)
        {
            var session = await GetActiveSessionForUserAsync(userId);
            if (session == null)
                return (false, null, "Nimate aktivne seje. Začnite kviz.");

            if (!_tracker.IsSessionActive(session.id))
                return (false, null, "Čas kviza je potekel. Kviz je zaključen.");

            var question = await _db.Questions
                .FirstOrDefaultAsync(q => q.id == questionId && q.quizId == session.quizId);

            if (question == null)
                return (false, null, "Vprašanje ne obstaja ali ne pripada vašemu kvizu.");

            var existing = await _db.UserAnswers
                .FirstOrDefaultAsync(ua => ua.sessionId == session.id && ua.questionId == questionId);

            return (true, new QuestionResponseDto
            {
                question = new QuestionSendDto(question.id, question.questionText),
                yourAnswer = existing?.selectedAnswer.ToString(),
                editsRemaining = existing != null ? Math.Max(0, 2 - existing.editCount) : 2
            }, "OK");
        }

        private async Task<GameSession?> GetActiveSessionForUserAsync(int userId)
        {
            return await _db.GameSessions
                .Where(gs => gs.userId == userId && !gs.isFinished)
                .OrderByDescending(gs => gs.startedAt)
                .FirstOrDefaultAsync();
        }
    }
}
