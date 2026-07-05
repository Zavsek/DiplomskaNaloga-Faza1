using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Poskus1.DTOs;
using Poskus1.Services;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Poskus1.Controllers
{
    [ApiController]
    [Authorize]
    public class ChatController : ControllerBase
    {
        private readonly ChatWebSocketManagerService _chatWsManager;
        private readonly IServiceScopeFactory _scopeFactory;

        public ChatController(ChatWebSocketManagerService chatWsManager, IServiceScopeFactory scopeFactory)
        {
            _chatWsManager = chatWsManager;
            _scopeFactory = scopeFactory;
        }

        [HttpGet("/chat")]
        public async Task GetChatWebSocket()
        {
            if (HttpContext.WebSockets.IsWebSocketRequest)
            {
                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (userIdStr == null)
                {
                    HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                var userId = int.Parse(userIdStr);
                using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();

                _chatWsManager.AddConnection(userId, webSocket);

                var buffer = new byte[1024 * 8];
                try
                {
                    var receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    while (!receiveResult.CloseStatus.HasValue)
                    {
                        if (receiveResult.MessageType == WebSocketMessageType.Text)
                        {
                            var receivedText = Encoding.UTF8.GetString(buffer, 0, receiveResult.Count);
                            ChatMessageRequestDto? incomingMsg = null;
                            try
                            {
                                incomingMsg = JsonSerializer.Deserialize<ChatMessageRequestDto>(receivedText);
                            }
                            catch { /* invalid json */ }

                            if (incomingMsg != null && (!string.IsNullOrWhiteSpace(incomingMsg.message) || incomingMsg.addStatistics == true))
                            {
                                UserStatisticsDto? stats = null;

                                if (incomingMsg.addStatistics == true)
                                {
                                    using var scope = _scopeFactory.CreateScope();
                                    var statsService = scope.ServiceProvider.GetRequiredService<StatisticsService>();
                                    stats = await statsService.CalculateUserStatisticsAsync(userId);
                                }

                                var outgoingMsg = new ChatMessageResponseDto
                                {
                                    userId = userId,
                                    message = string.IsNullOrWhiteSpace(incomingMsg.message) ? null : incomingMsg.message,
                                    statistics = stats,
                                    timestamp = DateTime.UtcNow
                                };

                                await _chatWsManager.BroadcastMessageAsync(outgoingMsg);
                            }
                        }

                        receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    }

                    await webSocket.CloseAsync(receiveResult.CloseStatus.Value, receiveResult.CloseStatusDescription, CancellationToken.None);
                }
                finally
                {
                    _chatWsManager.RemoveConnection(userId);
                }
            }
            else
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            }
        }
    }
}
