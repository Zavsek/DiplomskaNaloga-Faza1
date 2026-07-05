using Microsoft.EntityFrameworkCore;
using Poskus3.Data;
using Poskus3.Entities;
using System.Collections.Concurrent;

namespace Poskus3.Services
{
    // Podatki o aktivni seji v pomnilniku
    public class ActiveSession
    {
        public int SessionId { get; init; }
        public int UserId { get; init; }
        public int QuizId { get; init; }
        public DateTime ExpiresAt { get; init; }
        public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
        public CancellationTokenSource TimerCts { get; init; } = new();
    }

    public class GameSessionService : IDisposable
    {
        // sessionId -> ActiveSession
        private readonly ConcurrentDictionary<int, ActiveSession> _activeSessions = new();
        // userId -> sessionId (samo ena aktivna seja na uporabnika)
        private readonly ConcurrentDictionary<int, int> _userActiveSession = new();

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<GameSessionService> _logger;

        public GameSessionService(IServiceScopeFactory scopeFactory, ILogger<GameSessionService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<GameSession> StartSessionAsync(int userId, int quizId)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var quiz = await db.Quizzes.FirstOrDefaultAsync(q => q.id == quizId)
                ?? throw new KeyNotFoundException($"Kviz z ID {quizId} ne obstaja.");

            // Zaključi morebitno obstoječo aktivno sejo tega uporabnika
            if (_userActiveSession.TryGetValue(userId, out var oldSessionId))
                await FinishSessionAsync(oldSessionId, expired: false);

            // Označi vse prejšnje seje tega uporabnika za ta kviz kot ne-latest
            await db.GameSessions
                .Where(gs => gs.userId == userId && gs.quizId == quizId && gs.isLatestAttempt)
                .ExecuteUpdateAsync(s => s.SetProperty(gs => gs.isLatestAttempt, false));

            var session = new GameSession
            {
                userId = userId,
                quizId = quizId,
                startedAt = DateTime.UtcNow,
                isLatestAttempt = true
            };
            db.GameSessions.Add(session);
            await db.SaveChangesAsync();

            var expiresAt = session.startedAt.Add(quiz.duration);
            var cts = new CancellationTokenSource();

            var active = new ActiveSession
            {
                SessionId = session.id,
                UserId = userId,
                QuizId = quizId,
                ExpiresAt = expiresAt,
                TimerCts = cts
            };

            _activeSessions[session.id] = active;
            _userActiveSession[userId] = session.id;

            // Zaženi timer za samodejno zaključitev
            var delay = expiresAt - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
                _ = Task.Delay(delay, cts.Token).ContinueWith(t =>
                {
                    if (!t.IsCanceled)
                        _ = FinishSessionAsync(session.id, expired: true);
                }, TaskScheduler.Default);
            else
                _ = FinishSessionAsync(session.id, expired: true);

            return session;
        }

        public async Task FinishSessionAsync(int sessionId, bool expired)
        {
            if (!_activeSessions.TryRemove(sessionId, out var active))
                return;

            active.TimerCts.Cancel();
            active.TimerCts.Dispose();

            _userActiveSession.TryRemove(active.UserId, out _);

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var session = await db.GameSessions.FindAsync(sessionId);
            if (session != null && !session.isFinished)
            {
                session.isFinished = true;
                session.finishedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            if (expired)
                _logger.LogInformation("Seja {SessionId} je potekla.", sessionId);
        }

        // Vrne aktivno sejo za uporabnika ali null
        public ActiveSession? GetActiveSessionForUser(int userId)
        {
            if (_userActiveSession.TryGetValue(userId, out var sessionId) &&
                _activeSessions.TryGetValue(sessionId, out var active))
                return active;
            return null;
        }

        // Vrne aktivno sejo po sessionId
        public ActiveSession? GetActiveSession(int sessionId)
        {
            _activeSessions.TryGetValue(sessionId, out var active);
            return active;
        }

        // Izračuna skupni napredek vseh aktivnih uporabnikov za dani kviz:
        // vrne delež vprašanj, na katera je vsaj eden od sočasnih uporabnikov odgovoril
        public async Task<QuizProgressDto> GetQuizProgressAsync(int quizId, int totalQuestions)
        {
            // Zberemo vse aktivne seje za ta kviz
            var sessionIds = _activeSessions.Values
                .Where(s => s.QuizId == quizId)
                .Select(s => s.SessionId)
                .ToList();

            if (sessionIds.Count == 0)
                return new QuizProgressDto(quizId, 0, 0, 0.0);

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Število edinstvenih vprašanj, na katera je odgovoril vsaj en aktivni uporabnik
            var answeredQuestions = await db.UserAnswers
                .Where(ua => sessionIds.Contains(ua.sessionId))
                .Select(ua => ua.questionId)
                .Distinct()
                .CountAsync();

            var activePlayers = sessionIds.Count;
            var progressPercent = totalQuestions > 0
                ? Math.Round((double)answeredQuestions / totalQuestions * 100, 1)
                : 0.0;

            return new QuizProgressDto(quizId, activePlayers, answeredQuestions, progressPercent);
        }

        public void Dispose()
        {
            foreach (var s in _activeSessions.Values)
            {
                s.TimerCts.Cancel();
                s.TimerCts.Dispose();
            }
        }
    }

    public record QuizProgressDto(int quizId, int activePlayers, int answeredQuestions, double progressPercent);
}
