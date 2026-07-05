using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Poskus1.DTOs;
using Poskus1.Services;
using System.Security.Claims;

namespace Poskus1.Controllers
{
    [ApiController]
    [Route("chat")]
    [Authorize]
    public class ChatController : ControllerBase
    {
        private readonly ChatService _chatService;

        public ChatController(ChatService chatService)
        {
            _chatService = chatService;
        }

        [HttpPost]
        public async Task<IActionResult> SendMessage([FromBody] ChatSendDto dto)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized(new { message = "Neveljavna seja." });

            var senderName = User.FindFirstValue("fullName") ?? "Neznan uporabnik";

            var (success, message) = await _chatService.SendMessageAsync(userId.Value, senderName, dto);

            if (!success) return BadRequest(new { message });
            return Ok(new { message });
        }

        private int? GetUserId()
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? User.FindFirstValue("sub");
            if (int.TryParse(sub, out var id)) return id;
            return null;
        }
    }
}
