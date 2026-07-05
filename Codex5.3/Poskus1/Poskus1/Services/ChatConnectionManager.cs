using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace Poskus1.Services
{
    public sealed class ChatClientConnection
    {
        public string connectionId { get; init; } = string.Empty;
        public int userId { get; init; }
        public string displayName { get; init; } = string.Empty;
        public WebSocket socket { get; init; } = null!;

        // Serializira pošiljanje na isti socket. WebSocket ne dovoli sočasnih SendAsync klicev.
        internal SemaphoreSlim sendLock { get; } = new SemaphoreSlim(1, 1);
    }

    public interface IChatConnectionManager
    {
        ChatClientConnection AddConnection(int userId, string displayName, WebSocket socket);
        Task RemoveConnectionAsync(string connectionId);
        Task<bool> SendToConnectionAsync(ChatClientConnection connection, string payload, CancellationToken cancellationToken = default);
        Task BroadcastAsync(string payload, CancellationToken cancellationToken = default);
        IReadOnlyCollection<ChatClientConnection> GetConnectedClients();
    }

    public class ChatConnectionManager : IChatConnectionManager
    {
        // Zgornja meja za posamezno omrežno operacijo, da napol mrtev socket ne visi v nedogled.
        private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(2);

        private readonly ConcurrentDictionary<string, ChatClientConnection> _connections = new();

        public ChatClientConnection AddConnection(int userId, string displayName, WebSocket socket)
        {
            var connection = new ChatClientConnection
            {
                connectionId = Guid.NewGuid().ToString("N"),
                userId = userId,
                displayName = displayName,
                socket = socket
            };

            _connections[connection.connectionId] = connection;
            return connection;
        }

        public async Task RemoveConnectionAsync(string connectionId)
        {
            if (!_connections.TryRemove(connectionId, out var connection))
            {
                return;
            }

            try
            {
                if (connection.socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    using var cts = new CancellationTokenSource(CloseTimeout);
                    await connection.socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", cts.Token);
                }
            }
            catch
            {
                // Nenadno/pokvarjeno zapiranje ne sme vplivati na ostale odjemalce.
            }
        }

        public async Task<bool> SendToConnectionAsync(ChatClientConnection connection, string payload, CancellationToken cancellationToken = default)
        {
            if (connection.socket.State != WebSocketState.Open)
            {
                return false;
            }

            var bytes = Encoding.UTF8.GetBytes(payload);
            var acquired = false;
            try
            {
                await connection.sendLock.WaitAsync(cancellationToken);
                acquired = true;

                if (connection.socket.State != WebSocketState.Open)
                {
                    return false;
                }

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(SendTimeout);
                await connection.socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);
                return true;
            }
            catch
            {
                // Neuspešno pošiljanje (odklop, timeout, pokvarjen socket) obravnavamo kot mrtvo povezavo.
                return false;
            }
            finally
            {
                if (acquired)
                {
                    connection.sendLock.Release();
                }
            }
        }

        public async Task BroadcastAsync(string payload, CancellationToken cancellationToken = default)
        {
            var snapshot = _connections.Values.ToList();
            if (snapshot.Count == 0)
            {
                return;
            }

            // Pošiljamo vzporedno in namenoma NE vežemo dostave na cancellationToken pošiljatelja,
            // da nenaden odklop enega odjemalca ne prekine dostave ostalim. Vsak send je omejen s SendTimeout.
            var sendTasks = snapshot.Select(async entry =>
            {
                var delivered = await SendToConnectionAsync(entry, payload, CancellationToken.None);
                return delivered ? null : entry.connectionId;
            });

            var results = await Task.WhenAll(sendTasks);

            foreach (var failedId in results)
            {
                if (failedId is not null)
                {
                    await RemoveConnectionAsync(failedId);
                }
            }
        }

        public IReadOnlyCollection<ChatClientConnection> GetConnectedClients()
        {
            return _connections.Values.ToList();
        }
    }
}
