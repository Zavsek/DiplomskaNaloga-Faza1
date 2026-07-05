using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Poskus3.Data;
using Poskus3.DTOs;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Poskus3.Services
{
    public class GlobalChatWebSocketService
    {
        private sealed class ChatClient
        {
            public required string ConnectionId { get; init; }
            public required int UserId { get; init; }
            public required string DisplayName { get; init; }
            public required WebSocket Socket { get; init; }
            public SemaphoreSlim SendLock { get; } = new(1, 1);
        }

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GlobalChatWebSocketService> _logger;
        private readonly ConcurrentDictionary<string, ChatClient> _clients = new();
        private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

        public GlobalChatWebSocketService(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            ILogger<GlobalChatWebSocketService> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task HandleAsync(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { message = "WebSocket request is required." });
                return;
            }

            var authResult = await AuthenticateAsync(context);
            if (authResult is null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { message = "Invalid or missing JWT token." });
                return;
            }

            var socket = await context.WebSockets.AcceptWebSocketAsync();
            var client = new ChatClient
            {
                ConnectionId = Guid.NewGuid().ToString("N"),
                UserId = authResult.Value.userId,
                DisplayName = authResult.Value.displayName,
                Socket = socket
            };
            _clients[client.ConnectionId] = client;

            await SendToClientAsync(client, new
            {
                type = "connected",
                message = "Connected to global chat.",
                userId = client.UserId,
                displayName = client.DisplayName
            });

            try
            {
                await ReceiveLoopAsync(client);
            }
            catch (WebSocketException ex)
            {
                _logger.LogWarning(ex, "WebSocket closed unexpectedly for user {UserId}", client.UserId);
            }
            finally
            {
                _clients.TryRemove(client.ConnectionId, out _);
                if (client.Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    try
                    {
                        await client.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None);
                    }
                    catch
                    {
                        // ignore close errors
                    }
                }
                client.SendLock.Dispose();
                client.Socket.Dispose();
            }
        }

        private async Task ReceiveLoopAsync(ChatClient client)
        {
            var buffer = new byte[4096];

            while (client.Socket.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult receiveResult;

                do
                {
                    receiveResult = await client.Socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (receiveResult.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    ms.Write(buffer, 0, receiveResult.Count);
                }
                while (!receiveResult.EndOfMessage);

                var payloadJson = Encoding.UTF8.GetString(ms.ToArray());
                ChatIncomingMessageDto? payload;
                try
                {
                    payload = JsonSerializer.Deserialize<ChatIncomingMessageDto>(payloadJson, _jsonOptions);
                }
                catch (JsonException)
                {
                    await SendToClientAsync(client, new { type = "error", message = "Invalid JSON payload." });
                    continue;
                }

                if (payload is null)
                {
                    await SendToClientAsync(client, new { type = "error", message = "Payload is missing." });
                    continue;
                }

                var includeStatistics = ShouldIncludeStatistics(payload.addStatistics);
                var messageText = payload.message?.Trim();
                if (string.IsNullOrWhiteSpace(messageText) && !includeStatistics)
                {
                    await SendToClientAsync(client, new { type = "error", message = "Message cannot be empty." });
                    continue;
                }

                object? statistics = null;
                if (includeStatistics)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var statsService = scope.ServiceProvider.GetRequiredService<UserStatisticsService>();
                    statistics = await statsService.BuildStatisticsAsync(client.UserId);
                }

                var outgoing = new
                {
                    type = "chatMessage",
                    sentAtUtc = DateTime.UtcNow,
                    sender = new
                    {
                        userId = client.UserId,
                        displayName = client.DisplayName
                    },
                    message = string.IsNullOrWhiteSpace(messageText) ? null : messageText,
                    statistics
                };

                await BroadcastAsync(outgoing);
            }
        }

        private async Task BroadcastAsync(object payload)
        {
            var deadConnections = new List<string>();
            foreach (var kvp in _clients)
            {
                var client = kvp.Value;
                if (client.Socket.State != WebSocketState.Open)
                {
                    deadConnections.Add(client.ConnectionId);
                    continue;
                }

                try
                {
                    await SendToClientAsync(client, payload);
                }
                catch
                {
                    deadConnections.Add(client.ConnectionId);
                }
            }

            foreach (var dead in deadConnections)
            {
                _clients.TryRemove(dead, out _);
            }
        }

        private async Task SendToClientAsync(ChatClient client, object payload)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, _jsonOptions);
            await client.SendLock.WaitAsync();
            try
            {
                if (client.Socket.State == WebSocketState.Open)
                {
                    await client.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
            finally
            {
                client.SendLock.Release();
            }
        }

        private async Task<(int userId, string displayName)?> AuthenticateAsync(HttpContext context)
        {
            var token = ExtractToken(context);
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            var jwtSection = _configuration.GetSection("Jwt");
            var secret = jwtSection["Secret"];
            var issuer = jwtSection["Issuer"];
            var audience = jwtSection["Audience"];
            if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience))
            {
                return null;
            }

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = issuer,
                ValidAudience = audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                ClockSkew = TimeSpan.Zero
            };

            ClaimsPrincipal principal;
            try
            {
                principal = new JwtSecurityTokenHandler().ValidateToken(token, validationParameters, out _);
            }
            catch
            {
                return null;
            }

            var userIdClaim = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var jtiClaim = principal.FindFirstValue(JwtRegisteredClaimNames.Jti);
            if (!int.TryParse(userIdClaim, out var userId) || string.IsNullOrWhiteSpace(jtiClaim))
            {
                return null;
            }

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await dbContext.Users.SingleOrDefaultAsync(u => u.id == userId);
            if (user is null || user.currentTokenJti != jtiClaim || user.currentTokenExpiresAtUtc is null || user.currentTokenExpiresAtUtc <= DateTime.UtcNow)
            {
                return null;
            }

            return (user.id, user.fullName);
        }

        private static string? ExtractToken(HttpContext context)
        {
            var authHeader = context.Request.Headers.Authorization.ToString();
            if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return authHeader["Bearer ".Length..].Trim();
            }

            if (context.Request.Query.TryGetValue("token", out var queryToken) && !string.IsNullOrWhiteSpace(queryToken))
            {
                return queryToken.ToString();
            }

            return null;
        }

        private static bool ShouldIncludeStatistics(JsonElement? addStatisticsElement)
        {
            if (!addStatisticsElement.HasValue)
            {
                return false;
            }

            var value = addStatisticsElement.Value;
            return value.ValueKind switch
            {
                JsonValueKind.False => false,
                JsonValueKind.Null => false,
                JsonValueKind.Undefined => false,
                _ => true
            };
        }
    }
}
