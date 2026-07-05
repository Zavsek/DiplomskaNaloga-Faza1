using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Poskus2.Data;
using Poskus2.DTOs;
using Poskus2.Entities;
using Poskus2.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Poskus2.Controllers
{
    [ApiController]
    [Route("chat")]
    public class ChatController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ChatService _chatService;
        private readonly StatisticsService _statisticsService;
        private readonly JwtService _jwtService;

        public ChatController(
            AppDbContext db,
            ChatService chatService,
            StatisticsService statisticsService,
            JwtService jwtService)
        {
            _db = db;
            _chatService = chatService;
            _statisticsService = statisticsService;
            _jwtService = jwtService;
        }

        /// <summary>
        /// WebSocket endpoint: ws://host/chat?token=...
        /// Klient se poveže in prejema broadcast sporočila v realnem času.
        /// </summary>
        [Route("")]
        public async Task ConnectWebSocket()
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
            {
                HttpContext.Response.StatusCode = 400;
                return;
            }

            var userId = GetUserIdFromQuery();
            if (userId == null)
            {
                HttpContext.Response.StatusCode = 401;
                return;
            }

            var ws = await HttpContext.WebSockets.AcceptWebSocketAsync();
            await _chatService.ListenUntilCloseAsync(userId.Value, ws);
        }

        /// <summary>
        /// POST /chat/message — pošlje sporočilo v globalni chat (zahteva JWT Bearer).
        /// Telo mora vsebovati vsaj eno od: message (string) ali addStatistics (bool true).
        /// </summary>
        [HttpPost("message")]
        [Authorize]
        public async Task<IActionResult> SendMessage([FromBody] SendChatMessageDto dto)
        {
            var hasText = !string.IsNullOrWhiteSpace(dto.message);
            var wantsStats = dto.addStatistics == true;

            if (!hasText && !wantsStats)
                return BadRequest(new { message = "Sporočilo ne sme biti prazno. Pošljite besedilo ali vključite statistiko." });

            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var user = await _db.Users.FindAsync(userId.Value);
            if (user == null) return Unauthorized();

            UserStatisticsDto? stats = null;
            if (wantsStats)
                stats = await _statisticsService.ComputeForUserAsync(userId.Value);

            var chatMsg = new ChatMessage
            {
                userId = userId.Value,
                message = hasText ? dto.message!.Trim() : null,
                includesStatistics = wantsStats,
                sentAt = DateTime.UtcNow
            };
            _db.ChatMessages.Add(chatMsg);
            await _db.SaveChangesAsync();

            var broadcast = new ChatMessageBroadcastDto
            {
                type = "chat",
                messageId = chatMsg.id,
                userId = user.id,
                fullName = user.fullName,
                message = chatMsg.message,
                statistics = stats,
                sentAt = chatMsg.sentAt
            };

            await _chatService.BroadcastAsync(broadcast);

            return Ok(new { messageId = chatMsg.id, sentAt = chatMsg.sentAt });
        }

        private int? GetUserId()
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            return int.TryParse(sub, out var id) ? id : null;
        }

        private int? GetUserIdFromQuery()
        {
            var token = HttpContext.Request.Query["token"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(token)) return null;

            var info = _jwtService.ValidateToken(token);
            if (!info.valid || info.email == null) return null;

            var user = _db.Users.FirstOrDefault(u => u.email == info.email);
            if (user == null || user.activeTokenJti != info.jti) return null;

            return user.id;
        }
    }
}
