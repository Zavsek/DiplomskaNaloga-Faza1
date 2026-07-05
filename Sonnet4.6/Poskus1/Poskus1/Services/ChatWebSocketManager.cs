using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Poskus1.Services
{
    // Singleton: upravlja vse aktivne WebSocket povezave na /chat
    public sealed class ChatWebSocketManager
    {
        private readonly ConcurrentBag<WebSocket> _clients = new();

        public void AddClient(WebSocket ws)
        {
            _clients.Add(ws);
        }

        public async Task BroadcastAsync(object payload)
        {
            var json = JsonSerializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            var buffer = new ArraySegment<byte>(bytes);

            var deadSockets = new List<WebSocket>();

            foreach (var ws in _clients)
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

            if (deadSockets.Count > 0)
            {
                var alive = _clients.Where(ws => !deadSockets.Contains(ws)).ToList();
                while (_clients.TryTake(out _)) { }
                foreach (var ws in alive) _clients.Add(ws);
            }
        }

        // Drži WebSocket odprt dokler klient ne zapre povezave
        public async Task ListenUntilClosedAsync(WebSocket ws)
        {
            var buffer = new byte[256];
            while (ws.State == WebSocketState.Open)
            {
                try
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                        break;
                }
                catch
                {
                    break;
                }
            }

            if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
            {
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Goodbye", CancellationToken.None); }
                catch { /* ignore */ }
            }
        }
    }
}
