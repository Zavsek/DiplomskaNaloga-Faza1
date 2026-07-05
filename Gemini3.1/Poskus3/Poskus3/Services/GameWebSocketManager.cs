using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Poskus3.Services
{
    public class GameWebSocketManager
    {
        // Key: UserId, Value: List of active WebSockets for that user
        private readonly ConcurrentDictionary<int, ConcurrentBag<WebSocket>> _sockets = new();
        // Key: UserId, Value: Current QuizId they are taking
        private readonly ConcurrentDictionary<int, int> _userQuizSessions = new();

        public void AddSocket(int userId, WebSocket socket)
        {
            var bag = _sockets.GetOrAdd(userId, _ => new ConcurrentBag<WebSocket>());
            bag.Add(socket);
        }

        public async Task RemoveSocketAsync(int userId, WebSocket socket)
        {
            if (_sockets.TryGetValue(userId, out var bag))
            {
                // In ConcurrentBag we can't easily remove a specific item, 
                // but we handle cleanup during send if closed.
                // We'll just gracefully close if needed.
                if (socket.State == WebSocketState.Open)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by server", CancellationToken.None);
                }
            }
        }

        public void SetUserQuizSession(int userId, int quizId)
        {
            _userQuizSessions.AddOrUpdate(userId, quizId, (_, _) => quizId);
        }

        public void RemoveUserQuizSession(int userId)
        {
            _userQuizSessions.TryRemove(userId, out _);
        }

        public async Task BroadcastProgressAsync(int quizId, int currentUserId, int answeredQuestions, int totalQuestions)
        {
            var message = new
            {
                type = "ProgressUpdate",
                userId = currentUserId,
                answered = answeredQuestions,
                total = totalQuestions,
                percent = totalQuestions > 0 ? (double)answeredQuestions / totalQuestions : 0
            };

            var json = JsonSerializer.Serialize(message);
            var buffer = Encoding.UTF8.GetBytes(json);

            // Find all users in the same quiz
            var relevantUsers = _userQuizSessions.Where(kvp => kvp.Value == quizId).Select(kvp => kvp.Key).ToList();

            foreach (var uId in relevantUsers)
            {
                if (_sockets.TryGetValue(uId, out var userSockets))
                {
                    foreach (var socket in userSockets)
                    {
                        if (socket.State == WebSocketState.Open)
                        {
                            try
                            {
                                await socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
                            }
                            catch
                            {
                                // Ignore send errors (e.g. disconnected)
                            }
                        }
                    }
                }
            }
        }

        public async Task NotifyTimeoutAsync(int userId, int quizId)
        {
            RemoveUserQuizSession(userId);

            var message = new
            {
                type = "QuizTimeout",
                quizId = quizId,
                message = "Time is up! The quiz has ended."
            };

            var json = JsonSerializer.Serialize(message);
            var buffer = Encoding.UTF8.GetBytes(json);

            if (_sockets.TryGetValue(userId, out var userSockets))
            {
                foreach (var socket in userSockets)
                {
                    if (socket.State == WebSocketState.Open)
                    {
                        try
                        {
                            await socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
                        }
                        catch
                        {
                            // Ignore
                        }
                    }
                }
            }
        }
    }
}