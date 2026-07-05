using Poskus2.DTOs.Chat;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Poskus2.Services
{
    public class GlobalChatService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ConcurrentDictionary<Guid, ChatConnection> _connections = new();

        public GlobalChatService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task HandleSocketAsync(int userId, string userName, WebSocket socket, CancellationToken cancellationToken)
        {
            var connection = new ChatConnection(Guid.NewGuid(), userId, userName, socket);
            _connections[connection.id] = connection;

            try
            {
                await ReceiveLoopAsync(connection, cancellationToken);
            }
            finally
            {
                _connections.TryRemove(connection.id, out _);
                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", CancellationToken.None);
                }
            }
        }

        private async Task ReceiveLoopAsync(ChatConnection connection, CancellationToken cancellationToken)
        {
            var buffer = new byte[8 * 1024];
            while (!cancellationToken.IsCancellationRequested && connection.socket.State == WebSocketState.Open)
            {
                var message = await ReadMessageAsync(connection.socket, buffer, cancellationToken);
                if (message is null)
                {
                    break;
                }

                ChatIncomingPayloadDto? payload;
                try
                {
                    payload = JsonSerializer.Deserialize<ChatIncomingPayloadDto>(message);
                }
                catch
                {
                    await SendDirectAsync(connection.socket, new { type = "chatError", message = "Invalid JSON payload." }, cancellationToken);
                    continue;
                }

                var hasMessage = !string.IsNullOrWhiteSpace(payload?.message);
                var hasStatisticsRequest = payload?.addStatistics.HasValue == true && payload.addStatistics.Value.ValueKind != JsonValueKind.Null;

                if (!hasMessage && !hasStatisticsRequest)
                {
                    await SendDirectAsync(connection.socket, new
                    {
                        type = "chatError",
                        message = "Payload must contain message and/or addStatistics."
                    }, cancellationToken);
                    continue;
                }

                object? statistics = null;
                if (hasStatisticsRequest)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var statsService = scope.ServiceProvider.GetRequiredService<UserStatisticsService>();
                    statistics = await statsService.BuildForUserAsync(connection.userId, cancellationToken);
                }

                var outgoing = new
                {
                    type = "chatMessage",
                    userId = connection.userId,
                    fullName = connection.userName,
                    message = hasMessage ? payload!.message!.Trim() : null,
                    statistics,
                    sentAtUtc = DateTimeOffset.UtcNow
                };

                await BroadcastAsync(outgoing, cancellationToken);
            }
        }

        private async Task BroadcastAsync(object payload, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            var deadConnections = new List<Guid>();

            foreach (var entry in _connections)
            {
                if (entry.Value.socket.State != WebSocketState.Open)
                {
                    deadConnections.Add(entry.Key);
                    continue;
                }

                await entry.Value.socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
            }

            foreach (var deadConnection in deadConnections)
            {
                _connections.TryRemove(deadConnection, out _);
            }
        }

        private static async Task SendDirectAsync(WebSocket socket, object payload, CancellationToken cancellationToken)
        {
            if (socket.State != WebSocketState.Open)
            {
                return;
            }

            var json = JsonSerializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
        }

        private static async Task<string?> ReadMessageAsync(WebSocket socket, byte[] buffer, CancellationToken cancellationToken)
        {
            var chunk = new ArraySegment<byte>(buffer);
            using var ms = new MemoryStream();

            while (true)
            {
                var result = await socket.ReceiveAsync(chunk, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                ms.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    break;
                }
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private sealed record ChatConnection(Guid id, int userId, string userName, WebSocket socket);
    }
}
