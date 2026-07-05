using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Poskus1.Data;
using Poskus1.DTOs;
using Poskus1.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Poskus1.Controllers
{
    [ApiController]
    [Authorize]
    [Route("chat")]
    public class ChatController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly IChatConnectionManager _chatManager;
        private readonly IUserStatisticsService _userStatisticsService;

        public ChatController(
            AppDbContext dbContext,
            IChatConnectionManager chatManager,
            IUserStatisticsService userStatisticsService)
        {
            _dbContext = dbContext;
            _chatManager = chatManager;
            _userStatisticsService = userStatisticsService;
        }

        [HttpGet]
        public async Task Get()
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await HttpContext.Response.WriteAsJsonAsync(new { message = "Endpoint /chat podpira samo WebSocket povezave." });
                return;
            }

            var tokenValidation = await ValidateCurrentTokenAsync();
            if (!tokenValidation.isValid)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await HttpContext.Response.WriteAsJsonAsync(new { message = tokenValidation.errorMessage });
                return;
            }

            var user = await _dbContext.Users
                .AsNoTracking()
                .FirstAsync(u => u.id == tokenValidation.userId);

            using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            var connection = _chatManager.AddConnection(user.id, user.fullName, webSocket);

            var joinMessage = JsonSerializer.Serialize(new
            {
                type = "system",
                timestampUtc = DateTime.UtcNow,
                message = $"{user.fullName} je vstopil v globalni chat.",
                connectedUsers = _chatManager.GetConnectedClients().Count
            });
            await _chatManager.BroadcastAsync(joinMessage, HttpContext.RequestAborted);

            try
            {
                await ReceiveLoopAsync(connection, HttpContext.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                // Pričakovano ob (nenadnem) odklopu ali prekinitvi zahteve.
            }
            catch (WebSocketException)
            {
                // Nenadno/nepravilno zaprtje povezave ne sme sesuti obdelave zahteve.
            }
            finally
            {
                await _chatManager.RemoveConnectionAsync(connection.connectionId);
                var leaveMessage = JsonSerializer.Serialize(new
                {
                    type = "system",
                    timestampUtc = DateTime.UtcNow,
                    message = $"{user.fullName} je zapustil globalni chat.",
                    connectedUsers = _chatManager.GetConnectedClients().Count
                });
                await _chatManager.BroadcastAsync(leaveMessage, CancellationToken.None);
            }
        }

        private async Task ReceiveLoopAsync(ChatClientConnection connection, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && connection.socket.State == WebSocketState.Open)
            {
                var receivedText = await ReceiveTextMessageAsync(connection.socket, cancellationToken);
                if (receivedText is null)
                {
                    break;
                }

                ChatMessageRequestDto? incomingMessage;
                try
                {
                    incomingMessage = JsonSerializer.Deserialize<ChatMessageRequestDto>(receivedText);
                }
                catch
                {
                    var invalidJsonMessage = JsonSerializer.Serialize(new
                    {
                        type = "error",
                        message = "Neveljaven JSON format chat sporočila."
                    });
                    await _chatManager.SendToConnectionAsync(connection, invalidJsonMessage, cancellationToken);
                    continue;
                }

                if (incomingMessage is null)
                {
                    continue;
                }

                var hasMessageText = !string.IsNullOrWhiteSpace(incomingMessage.message);
                var includeStatistics = incomingMessage.addStatistics.HasValue;
                if (!hasMessageText && !includeStatistics)
                {
                    var emptyMessageError = JsonSerializer.Serialize(new
                    {
                        type = "error",
                        message = "Chat sporočilo ne sme biti prazno. Pošlji message in/ali addStatistics."
                    });
                    await _chatManager.SendToConnectionAsync(connection, emptyMessageError, cancellationToken);
                    continue;
                }

                UserStatisticsDto? statistics = null;
                if (includeStatistics)
                {
                    statistics = await _userStatisticsService.BuildUserStatisticsAsync(connection.userId, cancellationToken);
                }

                var outgoingPayload = JsonSerializer.Serialize(new
                {
                    type = "chat",
                    timestampUtc = DateTime.UtcNow,
                    sender = new
                    {
                        connection.userId,
                        fullName = connection.displayName
                    },
                    message = hasMessageText ? incomingMessage.message!.Trim() : null,
                    statistics
                });

                await _chatManager.BroadcastAsync(outgoingPayload, cancellationToken);
            }
        }

        private static async Task<string?> ReceiveTextMessageAsync(WebSocket socket, CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            using var memory = new MemoryStream();

            while (true)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                memory.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    break;
                }
            }

            return Encoding.UTF8.GetString(memory.ToArray());
        }

        private async Task<(bool isValid, int userId, string? errorMessage)> ValidateCurrentTokenAsync()
        {
            var subClaim = User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(subClaim, out var userId))
            {
                return (false, 0, "Neveljaven uporabniški identifikator v tokenu.");
            }

            var jwtId = User.FindFirstValue(JwtRegisteredClaimNames.Jti);
            if (string.IsNullOrWhiteSpace(jwtId))
            {
                return (false, 0, "Token nima identifikatorja (JTI).");
            }

            var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.id == userId);
            if (user is null)
            {
                return (false, 0, "Uporabnik ne obstaja.");
            }

            var isCurrentTokenValid =
                user.currentJwtId == jwtId &&
                user.currentJwtExpiresAtUtc.HasValue &&
                user.currentJwtExpiresAtUtc.Value > DateTime.UtcNow;

            if (!isCurrentTokenValid)
            {
                return (false, 0, "Token je razveljavljen zaradi nove prijave ali je potekel.");
            }

            return (true, userId, null);
        }
    }
}
