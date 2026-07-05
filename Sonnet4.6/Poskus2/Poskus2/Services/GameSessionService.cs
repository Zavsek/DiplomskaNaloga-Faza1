using Microsoft.EntityFrameworkCore;
using Poskus2.Data;
using Poskus2.DTOs;
using Poskus2.Entities;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Poskus2.Services
{
    public class ActiveSessionState
    {
        public int GameSessionId { get; set; }
        public int QuizId { get; set; }
        public DateTime EndsAt { get; set; }
        public bool IsFinished { get; set; }
        public ConcurrentDictionary<int, WebSocket> Connections { get; } = new();
        public Timer? ExpiryTimer { get; set; }
    }

    public class GameSessionService : IDisposable
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ConcurrentDictionary<int, ActiveSessionState> _activeSessions = new();

        public GameSessionService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task<GameSession?> GetOrCreateSessionAsync(int quizId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var existing = await db.GameSessions
                .Include(s => s.quiz)
                .FirstOrDefaultAsync(s => s.quizId == quizId && !s.isFinished);

            if (existing != null)
            {
                if (existing.endsAt.HasValue && DateTime.UtcNow >= existing.endsAt.Value)
                {
                    existing.isFinished = true;
                    await db.SaveChangesAsync();
                    return null;
                }
                EnsureTimerRegistered(existing.id, existing.quizId, existing.endsAt!.Value);
                return existing;
            }

            var quiz = await db.Quizzes.FindAsync(quizId);
            if (quiz == null) return null;

            var session = new GameSession
            {
                quizId = quizId,
                startedAt = DateTime.UtcNow,
                endsAt = DateTime.UtcNow.Add(quiz.duration),
                isFinished = false
            };

            db.GameSessions.Add(session);
            await db.SaveChangesAsync();

            EnsureTimerRegistered(session.id, quizId, session.endsAt!.Value);

            return session;
        }

        private void EnsureTimerRegistered(int gameSessionId, int quizId, DateTime endsAt)
        {
            _activeSessions.GetOrAdd(gameSessionId, _ =>
            {
                var state = new ActiveSessionState
                {
                    GameSessionId = gameSessionId,
                    QuizId = quizId,
                    EndsAt = endsAt
                };

                var delay = endsAt - DateTime.UtcNow;
                if (delay <= TimeSpan.Zero)
                {
                    _ = FinishSessionAsync(gameSessionId);
                }
                else
                {
                    state.ExpiryTimer = new Timer(
                        _ => _ = FinishSessionAsync(gameSessionId),
                        null,
                        (long)delay.TotalMilliseconds,
                        Timeout.Infinite
                    );
                }

                return state;
            });
        }

        private async Task FinishSessionAsync(int gameSessionId)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var session = await db.GameSessions.FindAsync(gameSessionId);
            if (session == null || session.isFinished) return;

            session.isFinished = true;
            await db.SaveChangesAsync();

            if (_activeSessions.TryGetValue(gameSessionId, out var state))
            {
                state.IsFinished = true;
                state.ExpiryTimer?.Dispose();
                await BroadcastFinishedAsync(state);
            }
        }

        public async Task AddWebSocketAsync(int gameSessionId, int userId, WebSocket ws)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (!_activeSessions.TryGetValue(gameSessionId, out var state))
            {
                var session = await db.GameSessions.FindAsync(gameSessionId);
                if (session == null) return;
                EnsureTimerRegistered(gameSessionId, session.quizId, session.endsAt ?? DateTime.UtcNow);
                _activeSessions.TryGetValue(gameSessionId, out state);
                if (state == null) return;
            }

            if (state.IsFinished)
            {
                var finished = new WsFinishedMessage { gameSessionId = gameSessionId, finishedAt = state.EndsAt };
                await SendMessageAsync(ws, finished);
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Kviz je zaključen.", CancellationToken.None);
                return;
            }

            state.Connections[userId] = ws;
            await SendProgressUpdateAsync(gameSessionId);

            try
            {
                var buffer = new byte[256];
                while (ws.State == WebSocketState.Open)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                        break;
                }
            }
            finally
            {
                state.Connections.TryRemove(userId, out _);
                if (ws.State == WebSocketState.Open)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnected", CancellationToken.None);
            }
        }

        public async Task SendProgressUpdateAsync(int gameSessionId)
        {
            if (!_activeSessions.TryGetValue(gameSessionId, out var state)) return;
            if (state.Connections.IsEmpty) return;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var totalQuestions = await db.Questions.CountAsync(q => q.quizId == state.QuizId);

            var participants = await db.UserGameSessions
                .Include(ugs => ugs.user)
                .Include(ugs => ugs.answers)
                .Where(ugs => ugs.gameSessionId == gameSessionId)
                .ToListAsync();

            var progressList = participants.Select(p => new ParticipantProgress
            {
                userId = p.userId,
                fullName = p.user.fullName,
                answeredQuestions = p.answers.Count,
                progressPercent = totalQuestions > 0
                    ? Math.Round((double)p.answers.Count / totalQuestions * 100, 1)
                    : 0
            }).ToList();

            var msg = new WsProgressMessage
            {
                gameSessionId = gameSessionId,
                totalParticipants = participants.Count,
                totalQuestions = totalQuestions,
                averageProgress = progressList.Count > 0
                    ? Math.Round(progressList.Average(x => x.progressPercent), 1)
                    : 0,
                participants = progressList
            };

            await BroadcastAsync(state, msg);
        }

        private async Task BroadcastFinishedAsync(ActiveSessionState state)
        {
            var msg = new WsFinishedMessage
            {
                gameSessionId = state.GameSessionId,
                finishedAt = state.EndsAt
            };

            await BroadcastAsync(state, msg);

            foreach (var (_, ws) in state.Connections.ToArray())
            {
                if (ws.State == WebSocketState.Open)
                {
                    try
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Kviz je zaključen.", CancellationToken.None);
                    }
                    catch { /* ignore disconnect errors */ }
                }
            }
        }

        private async Task BroadcastAsync<T>(ActiveSessionState state, T message)
        {
            var json = JsonSerializer.Serialize(message);
            var bytes = Encoding.UTF8.GetBytes(json);
            var tasks = new List<Task>();

            foreach (var (userId, ws) in state.Connections.ToArray())
            {
                if (ws.State == WebSocketState.Open)
                    tasks.Add(SendRawAsync(ws, bytes));
                else
                    state.Connections.TryRemove(userId, out _);
            }

            if (tasks.Count > 0)
                await Task.WhenAll(tasks);
        }

        private static async Task SendMessageAsync<T>(WebSocket ws, T message)
        {
            var json = JsonSerializer.Serialize(message);
            var bytes = Encoding.UTF8.GetBytes(json);
            await SendRawAsync(ws, bytes);
        }

        private static Task SendRawAsync(WebSocket ws, byte[] bytes)
            => ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);

        public bool IsSessionFinished(int gameSessionId)
        {
            if (_activeSessions.TryGetValue(gameSessionId, out var state))
                return state.IsFinished;
            return false;
        }

        public void Dispose()
        {
            foreach (var (_, state) in _activeSessions)
                state.ExpiryTimer?.Dispose();
        }
    }
}
