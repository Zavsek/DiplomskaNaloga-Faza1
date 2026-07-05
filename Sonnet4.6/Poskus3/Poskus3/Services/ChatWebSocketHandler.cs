using Microsoft.EntityFrameworkCore;
using Poskus3.Data;
using Poskus3.Entities;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Poskus3.Services
{
    public class ChatWebSocketHandler
    {
        // Vse aktivne WebSocket povezave: connectionId -> (WebSocket, userId, fullName)
        private readonly ConcurrentDictionary<string, (WebSocket ws, int userId, string fullName)> _connections = new();

        private readonly JwtService _jwtService;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly StatisticsService _statsService;

        public ChatWebSocketHandler(
            JwtService jwtService,
            IServiceScopeFactory scopeFactory,
            StatisticsService statsService)
        {
            _jwtService = jwtService;
            _scopeFactory = scopeFactory;
            _statsService = statsService;
        }

        public async Task HandleAsync(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new { message = "Zahtevana je WebSocket povezava." });
                return;
            }

            var token = context.Request.Query["token"].FirstOrDefault()
                ?? ExtractBearer(context.Request.Headers.Authorization.FirstOrDefault());

            if (string.IsNullOrEmpty(token))
            {
                context.Response.StatusCode = 401;
                return;
            }

            var principal = _jwtService.ValidateToken(token);
            if (principal == null)
            {
                context.Response.StatusCode = 401;
                return;
            }

            var subClaim = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? principal.FindFirst("sub")?.Value;
            if (!int.TryParse(subClaim, out var userId))
            {
                context.Response.StatusCode = 401;
                return;
            }

            // Preveri JTI
            var decoded = _jwtService.DecodeToken(token);
            if (decoded == null)
            {
                context.Response.StatusCode = 401;
                return;
            }

            string fullName;
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var user = await db.Users.FindAsync(userId);
                if (user == null || user.currentTokenJti != decoded.Id)
                {
                    context.Response.StatusCode = 401;
                    return;
                }
                fullName = user.fullName;
            }

            using var ws = await context.WebSockets.AcceptWebSocketAsync();
            var connectionId = Guid.NewGuid().ToString();
            _connections[connectionId] = (ws, userId, fullName);

            try
            {
                await ReceiveLoop(ws, connectionId, userId, fullName);
            }
            finally
            {
                _connections.TryRemove(connectionId, out _);
            }
        }

        private async Task ReceiveLoop(WebSocket ws, string connectionId, int userId, string fullName)
        {
            var buffer = new byte[4096];

            while (ws.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                using var ms = new System.IO.MemoryStream();

                do
                {
                    try
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    }
                    catch (WebSocketException)
                    {
                        return;
                    }

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Zaprt.", CancellationToken.None);
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var rawJson = Encoding.UTF8.GetString(ms.ToArray());
                await ProcessMessageAsync(ws, connectionId, userId, fullName, rawJson);
            }
        }

        private async Task ProcessMessageAsync(WebSocket ws, string connectionId, int userId, string fullName, string rawJson)
        {
            ChatIncomingDto? incoming;
            try
            {
                incoming = JsonSerializer.Deserialize<ChatIncomingDto>(rawJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                await SendToOne(ws, new { error = "Neveljaven JSON format." });
                return;
            }

            if (incoming == null || (string.IsNullOrWhiteSpace(incoming.message) && !incoming.addStatistics))
            {
                await SendToOne(ws, new { error = "Sporočilo ne sme biti prazno." });
                return;
            }

            // Izračunaj statistiko po potrebi
            UserStatsDto? stats = null;
            if (incoming.addStatistics)
                stats = await _statsService.ComputeForUserAsync(userId);

            // Shrani sporočilo v bazo
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.ChatMessages.Add(new ChatMessage
                {
                    userId = userId,
                    message = string.IsNullOrWhiteSpace(incoming.message) ? null : incoming.message.Trim(),
                    hasStatistics = incoming.addStatistics,
                    sentAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }

            // Sestavi broadcast payload
            var broadcast = new
            {
                from = fullName,
                userId,
                sentAt = DateTime.UtcNow,
                message = string.IsNullOrWhiteSpace(incoming.message) ? null : incoming.message.Trim(),
                statistics = stats
            };

            await BroadcastAsync(broadcast);
        }

        private async Task BroadcastAsync(object payload)
        {
            var json = JsonSerializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(bytes);

            var tasks = _connections.Values
                .Where(c => c.ws.State == WebSocketState.Open)
                .Select(c => c.ws.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None));

            await Task.WhenAll(tasks);
        }

        private static async Task SendToOne(WebSocket ws, object payload)
        {
            if (ws.State != WebSocketState.Open) return;
            var json = JsonSerializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private static string? ExtractBearer(string? header)
        {
            if (header != null && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return header["Bearer ".Length..].Trim();
            return null;
        }
    }

    public class ChatIncomingDto
    {
        public string? message { get; set; }
        public bool addStatistics { get; set; } = false;
    }
}
