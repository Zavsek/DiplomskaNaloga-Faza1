using Poskus2.DTOs;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Poskus2.Services
{
    public class ChatService : IDisposable
    {
        private readonly ConcurrentDictionary<int, WebSocket> _connections = new();

        public void AddConnection(int userId, WebSocket ws)
        {
            _connections[userId] = ws;
        }

        public void RemoveConnection(int userId)
        {
            _connections.TryRemove(userId, out _);
        }

        public async Task BroadcastAsync(ChatMessageBroadcastDto message)
        {
            var json = JsonSerializer.Serialize(message);
            var bytes = Encoding.UTF8.GetBytes(json);
            var tasks = new List<Task>();

            foreach (var (userId, ws) in _connections.ToArray())
            {
                if (ws.State == WebSocketState.Open)
                    tasks.Add(SendRawAsync(ws, bytes));
                else
                    _connections.TryRemove(userId, out _);
            }

            if (tasks.Count > 0)
                await Task.WhenAll(tasks);
        }

        public async Task ListenUntilCloseAsync(int userId, WebSocket ws)
        {
            AddConnection(userId, ws);
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
                RemoveConnection(userId);
                if (ws.State == WebSocketState.Open)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnected", CancellationToken.None);
            }
        }

        private static Task SendRawAsync(WebSocket ws, byte[] bytes)
            => ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);

        public void Dispose()
        {
            // connections are closed by clients or server shutdown
        }
    }
}
