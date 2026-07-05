using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Poskus3.Services
{
    public class ChatWebSocketManager
    {
        private readonly ConcurrentDictionary<WebSocket, int> _sockets = new();

        public void AddSocket(WebSocket socket, int userId)
        {
            _sockets.TryAdd(socket, userId);
        }

        public async Task RemoveSocketAsync(WebSocket socket)
        {
            _sockets.TryRemove(socket, out _);
            if (socket.State == WebSocketState.Open)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by server", CancellationToken.None);
            }
        }

        public async Task BroadcastAsync(object payload)
        {
            var json = JsonSerializer.Serialize(payload);
            var buffer = Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(buffer);

            foreach (var socket in _sockets.Keys)
            {
                if (socket.State == WebSocketState.Open)
                {
                    try
                    {
                        await socket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    catch { }
                }
            }
        }
    }
}