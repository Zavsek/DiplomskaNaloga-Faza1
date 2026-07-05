using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Poskus2.Services
{
    public class QuizWebSocketManager
    {
        // QuizId -> (UserId -> WebSocket)
        private readonly ConcurrentDictionary<int, ConcurrentDictionary<int, WebSocket>> _sockets = new();

        // Global Chat Sockets
        private readonly ConcurrentDictionary<int, WebSocket> _chatSockets = new();

        public void AddChatSocket(int userId, WebSocket socket)
        {
            _chatSockets.AddOrUpdate(userId, socket, (_, __) => socket);
        }

        public void RemoveChatSocket(int userId)
        {
            _chatSockets.TryRemove(userId, out _);
        }

        public async Task BroadcastChatMessageAsync(object messageObj)
        {
            var message = JsonSerializer.Serialize(messageObj, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var bytes = Encoding.UTF8.GetBytes(message);
            var buffer = new ArraySegment<byte>(bytes);

            var tasks = _chatSockets.Values
                .Where(s => s.State == WebSocketState.Open)
                .Select(s => s.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None));
            
            await Task.WhenAll(tasks);
        }

        public void AddSocket(int quizId, int userId, WebSocket socket)
        {
            var quizSockets = _sockets.GetOrAdd(quizId, _ => new ConcurrentDictionary<int, WebSocket>());
            quizSockets.AddOrUpdate(userId, socket, (_, __) => socket);
        }

        public void RemoveSocket(int quizId, int userId)
        {
            if (_sockets.TryGetValue(quizId, out var quizSockets))
            {
                quizSockets.TryRemove(userId, out _);
            }
        }

        public async Task BroadcastProgressAsync(int quizId, object progressData)
        {
            if (_sockets.TryGetValue(quizId, out var quizSockets))
            {
                var message = JsonSerializer.Serialize(new { type = "progress", data = progressData });
                var bytes = Encoding.UTF8.GetBytes(message);
                var buffer = new ArraySegment<byte>(bytes);

                var tasks = quizSockets.Values
                    .Where(s => s.State == WebSocketState.Open)
                    .Select(s => s.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None));
                
                await Task.WhenAll(tasks);
            }
        }

        public async Task SendTimeUpAsync(int quizId, int userId)
        {
            if (_sockets.TryGetValue(quizId, out var quizSockets) && quizSockets.TryGetValue(userId, out var socket))
            {
                if (socket.State == WebSocketState.Open)
                {
                    var message = JsonSerializer.Serialize(new { type = "timeup", message = "Time is up! Quiz finished." });
                    var bytes = Encoding.UTF8.GetBytes(message);
                    await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
        }
    }
}