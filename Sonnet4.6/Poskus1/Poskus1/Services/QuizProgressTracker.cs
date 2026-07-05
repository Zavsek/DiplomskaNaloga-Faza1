using Microsoft.EntityFrameworkCore;
using Poskus1.Data;
using Poskus1.DTOs;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Poskus1.Services
{
    // Aktivna seja v memoriju — eden zapis na aktivno igro posameznega uporabnika
    public sealed class ActiveSession
    {
        public int SessionId { get; init; }
        public int UserId { get; init; }
        public int QuizId { get; init; }
        public int TotalQuestions { get; init; }
        public DateTime ExpiresAt { get; init; }
        // število odgovorjenih vprašanj v tej seji (posodobi GameService)
        public int AnsweredCount { get; set; } = 0;
    }

    // Singleton: živi med celotnim časom delovanja aplikacije
    public sealed class QuizProgressTracker : IDisposable
    {
        // sessionId -> ActiveSession
        private readonly ConcurrentDictionary<int, ActiveSession> _sessions = new();

        // quizId -> množica WebSocket klientov ki poslušajo napredek tega kviza
        private readonly ConcurrentDictionary<int, ConcurrentBag<WebSocket>> _wsClients = new();

        // Pohranjeni timerji za avtomatski konec seje: sessionId -> Timer
        private readonly ConcurrentDictionary<int, Timer> _timers = new();

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<QuizProgressTracker> _logger;

        public QuizProgressTracker(IServiceScopeFactory scopeFactory, ILogger<QuizProgressTracker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        // Registracija nove aktivne seje in zagon timerja
        public void RegisterSession(ActiveSession session)
        {
            _sessions[session.SessionId] = session;

            var delay = session.ExpiresAt - DateTime.UtcNow;
            if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;

            var timer = new Timer(
                callback: async _ => await ExpireSessionAsync(session.SessionId),
                state: null,
                dueTime: delay,
                period: Timeout.InfiniteTimeSpan
            );
            _timers[session.SessionId] = timer;
        }

        public bool TryGetSession(int sessionId, out ActiveSession? session)
            => _sessions.TryGetValue(sessionId, out session);

        public bool IsSessionActive(int sessionId)
            => _sessions.ContainsKey(sessionId);

        // Posodobi število odgovorjenih vprašanj in pošlji broadcast vsem poslušalcem
        public async Task UpdateAnswerCountAsync(int sessionId, int newCount)
        {
            if (!_sessions.TryGetValue(sessionId, out var session)) return;
            session.AnsweredCount = newCount;
            await BroadcastProgressAsync(session.QuizId);
        }

        // Registracija WebSocket klienta ki želi prejemati napredek za določen kviz
        public void AddWebSocketClient(int quizId, WebSocket ws)
        {
            _wsClients.GetOrAdd(quizId, _ => new ConcurrentBag<WebSocket>()).Add(ws);
        }

        // Vrni napredek za prikaz
        public QuizProgressBroadcastDto? GetProgress(int quizId, string quizTitle, int totalQuestions)
        {
            var activeSessions = _sessions.Values
                .Where(s => s.QuizId == quizId)
                .ToList();

            if (activeSessions.Count == 0) return null;

            double avgProgress = activeSessions.Count > 0
                ? activeSessions.Average(s => totalQuestions > 0
                    ? (double)s.AnsweredCount / totalQuestions * 100
                    : 0)
                : 0;

            return new QuizProgressBroadcastDto
            {
                quizId = quizId,
                quizTitle = quizTitle,
                totalQuestions = totalQuestions,
                activePlayers = activeSessions.Count,
                averageProgressPercent = Math.Round(avgProgress, 1)
            };
        }

        private async Task ExpireSessionAsync(int sessionId)
        {
            if (!_sessions.TryRemove(sessionId, out var session))
                return;

            if (_timers.TryRemove(sessionId, out var timer))
                await timer.DisposeAsync();

            _logger.LogInformation("Seja {SessionId} za kviz {QuizId} je potekla.", sessionId, session.QuizId);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var dbSession = await db.GameSessions.FindAsync(sessionId);
            if (dbSession != null && !dbSession.isFinished)
            {
                dbSession.isFinished = true;
                dbSession.finishedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            // obvesti WS kliente da je kviz koncan
            await BroadcastQuizEndedAsync(session.QuizId, sessionId);
        }

        // Pošlje napredek vsem WS klientom za ta kviz
        private async Task BroadcastProgressAsync(int quizId)
        {
            if (!_wsClients.TryGetValue(quizId, out var clients)) return;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var quiz = await db.Quizzes
                .Include(q => q.questions)
                .FirstOrDefaultAsync(q => q.id == quizId);

            if (quiz == null) return;

            var dto = GetProgress(quizId, quiz.title, quiz.questions.Count);
            if (dto == null) return;

            var payload = JsonSerializer.Serialize(new { type = "progress", data = dto });
            await SendToClientsAsync(clients, payload);
        }

        private async Task BroadcastQuizEndedAsync(int quizId, int sessionId)
        {
            if (!_wsClients.TryGetValue(quizId, out var clients)) return;

            var payload = JsonSerializer.Serialize(new
            {
                type = "quiz_ended",
                data = new { quizId, sessionId, message = "Čas kviza je potekel. Kviz je zaključen." }
            });
            await SendToClientsAsync(clients, payload);
        }

        private static async Task SendToClientsAsync(ConcurrentBag<WebSocket> clients, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            var buffer = new ArraySegment<byte>(bytes);
            var deadSockets = new List<WebSocket>();

            foreach (var ws in clients)
            {
                try
                {
                    if (ws.State == WebSocketState.Open)
                        await ws.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
                    else
                        deadSockets.Add(ws);
                }
                catch
                {
                    deadSockets.Add(ws);
                }
            }

            // pocisti zaprte povezave — ConcurrentBag ne podpira Remove,
            // zato zamenjamo bag brez mrtvih socketov
            if (deadSockets.Count > 0)
            {
                var alive = clients.Where(ws => !deadSockets.Contains(ws)).ToList();
                // ker je ConcurrentBag immutable glede brisanja, items prenesemo v nov bag
                while (clients.TryTake(out _)) { }
                foreach (var ws in alive) clients.Add(ws);
            }
        }

        // Vrne kopijo aktivnih sej za določen kviz (za GameService)
        public IReadOnlyList<ActiveSession> GetActiveSessionsForQuiz(int quizId)
            => _sessions.Values.Where(s => s.QuizId == quizId).ToList();

        public void Dispose()
        {
            foreach (var timer in _timers.Values)
                timer.Dispose();
        }
    }
}
