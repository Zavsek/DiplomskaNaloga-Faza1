using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Poskus1.Services
{
    public class WebSocketManagerService
    {
        // quizId -> (userId -> WebSocket)
        private readonly ConcurrentDictionary<int, ConcurrentDictionary<int, WebSocket>> _quizConnections = new();

        public void AddConnection(int quizId, int userId, WebSocket socket)
        {
            var quizDict = _quizConnections.GetOrAdd(quizId, _ => new ConcurrentDictionary<int, WebSocket>());
            quizDict[userId] = socket;
        }

        public void RemoveConnection(int quizId, int userId)
        {
            if (_quizConnections.TryGetValue(quizId, out var quizDict))
            {
                quizDict.TryRemove(userId, out _);
            }
        }

        public async Task SendProgressUpdateAsync(int quizId, object progressData)
        {
            if (_quizConnections.TryGetValue(quizId, out var quizDict))
            {
                var json = JsonSerializer.Serialize(new { type = "progress", data = progressData });
                var bytes = Encoding.UTF8.GetBytes(json);
                
                foreach (var kvp in quizDict)
                {
                    var socket = kvp.Value;
                    if (socket.State == WebSocketState.Open)
                    {
                        try
                        {
                            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                        }
                        catch { /* Ignore closed sockets */ }
                    }
                }
            }
        }

        public async Task SendTimeoutMessageAsync(int userId, int quizId)
        {
            if (_quizConnections.TryGetValue(quizId, out var quizDict) && quizDict.TryGetValue(userId, out var socket))
            {
                if (socket.State == WebSocketState.Open)
                {
                    var json = JsonSerializer.Serialize(new { type = "timeout", message = "Time is up! Quiz finished." });
                    var bytes = Encoding.UTF8.GetBytes(json);
                    try
                    {
                        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    catch { /* Ignore closed sockets */ }
                }
            }
        }
    }
}
