using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Poskus1.Services
{
    public class ChatWebSocketManagerService
    {
        private readonly ConcurrentDictionary<int, WebSocket> _sockets = new();

        public void AddConnection(int userId, WebSocket socket)
        {
            _sockets[userId] = socket;
        }

        public void RemoveConnection(int userId)
        {
            _sockets.TryRemove(userId, out _);
        }

        public async Task BroadcastMessageAsync(object messageObj)
        {
            var json = JsonSerializer.Serialize(messageObj);
            var bytes = Encoding.UTF8.GetBytes(json);

            foreach (var kvp in _sockets)
            {
                var socket = kvp.Value;
                if (socket.State == WebSocketState.Open)
                {
                    try
                    {
                        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    catch { /* ignore */ }
                }
            }
        }
    }
}
