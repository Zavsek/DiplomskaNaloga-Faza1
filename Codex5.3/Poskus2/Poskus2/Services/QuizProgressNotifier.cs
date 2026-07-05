using Microsoft.EntityFrameworkCore;
using Poskus2.Data;
using Poskus2.Entities;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Poskus2.Services
{
    public class QuizProgressNotifier
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ConcurrentDictionary<int, ConcurrentDictionary<Guid, QuizSocketConnection>> _connections = new();

        public QuizProgressNotifier(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task HandleSocketAsync(int quizId, int userId, WebSocket socket, CancellationToken cancellationToken)
        {
            var connection = new QuizSocketConnection(Guid.NewGuid(), userId, quizId, socket);
            var quizConnections = _connections.GetOrAdd(quizId, _ => new ConcurrentDictionary<Guid, QuizSocketConnection>());
            quizConnections[connection.Id] = connection;

            try
            {
                await SendSnapshotForConnectionAsync(connection, cancellationToken);
                await ReceiveLoopAsync(socket, cancellationToken);
            }
            finally
            {
                if (_connections.TryGetValue(quizId, out var existingConnections))
                {
                    existingConnections.TryRemove(connection.Id, out _);
                    if (existingConnections.IsEmpty)
                    {
                        _connections.TryRemove(quizId, out _);
                    }
                }

                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", CancellationToken.None);
                }
            }
        }

        public async Task BroadcastProgressAsync(int quizId, CancellationToken cancellationToken = default)
        {
            if (!_connections.TryGetValue(quizId, out var quizConnections) || quizConnections.IsEmpty)
            {
                return;
            }

            var connectionSnapshot = quizConnections.Values.ToList();
            foreach (var connection in connectionSnapshot)
            {
                if (connection.Socket.State != WebSocketState.Open)
                {
                    quizConnections.TryRemove(connection.Id, out _);
                    continue;
                }

                await SendSnapshotForConnectionAsync(connection, cancellationToken);
            }
        }

        private async Task SendSnapshotForConnectionAsync(QuizSocketConnection connection, CancellationToken cancellationToken)
        {
            if (connection.Socket.State != WebSocketState.Open)
            {
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTimeOffset.UtcNow;

            var totalQuestions = await dbContext.Questions.CountAsync(q => q.quizId == connection.QuizId, cancellationToken);
            var activeSessions = await dbContext.GameSessions
                .Where(gs => gs.quizId == connection.QuizId &&
                             gs.status == GameSessionStatus.InProgress &&
                             gs.endsAtUtc > now)
                .Select(gs => new { gs.id, gs.userId })
                .ToListAsync(cancellationToken);

            var sessionIds = activeSessions.Select(s => s.id).ToList();
            var answeredCounts = sessionIds.Count == 0
                ? new Dictionary<int, int>()
                : await dbContext.GameAnswers
                    .Where(ga => sessionIds.Contains(ga.gameSessionId))
                    .GroupBy(ga => ga.gameSessionId)
                    .Select(g => new { gameSessionId = g.Key, count = g.Count() })
                    .ToDictionaryAsync(x => x.gameSessionId, x => x.count, cancellationToken);

            var otherSessions = activeSessions.Where(s => s.userId != connection.UserId).ToList();
            var otherAnswered = otherSessions.Sum(s => answeredCounts.GetValueOrDefault(s.id, 0));
            var otherPossible = totalQuestions * otherSessions.Count;
            var othersAnsweredShare = otherPossible == 0 ? 0d : (double)otherAnswered / otherPossible;

            var payloadJson = JsonSerializer.Serialize(new
            {
                type = "quizProgress",
                quizId = connection.QuizId,
                othersActivePlayers = otherSessions.Count,
                othersAnsweredShare,
                timestampUtc = now
            });

            var bytes = Encoding.UTF8.GetBytes(payloadJson);
            await connection.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
        }

        private static async Task ReceiveLoopAsync(WebSocket socket, CancellationToken cancellationToken)
        {
            var buffer = new byte[1024];
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }

        private sealed record QuizSocketConnection(Guid Id, int UserId, int QuizId, WebSocket Socket);
    }
}
