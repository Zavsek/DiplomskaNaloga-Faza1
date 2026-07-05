using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Poskus1.DTOs;
using Poskus1.Services;
using System.Security.Claims;

namespace Poskus1.Controllers
{
    [ApiController]
    [Authorize]
    public class GameController : ControllerBase
    {
        private readonly GameService _gameService;

        public GameController(GameService gameService)
        {
            _gameService = gameService;
        }

        [HttpPost("game/start/{quiz}")]
        public async Task<IActionResult> StartGame([FromRoute] int quiz)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized(new { message = "Neveljavna seja." });

            var (success, result, message) = await _gameService.StartGameAsync(quiz, userId.Value);

            if (!success) return BadRequest(new { message });
            return Ok(result);
        }

        [HttpPost("api/answer")]
        public async Task<IActionResult> SubmitAnswer([FromBody] AnswerSubmitDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var userId = GetUserId();
            if (userId == null) return Unauthorized(new { message = "Neveljavna seja." });

            var (success, result, message) = await _gameService.SubmitAnswerAsync(userId.Value, dto);

            if (!success) return BadRequest(new { message });
            return Ok(result);
        }

        [HttpGet("api/question/{questionId}")]
        public async Task<IActionResult> GetQuestion([FromRoute] int questionId)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized(new { message = "Neveljavna seja." });

            var (success, result, message) = await _gameService.GetQuestionAsync(userId.Value, questionId);

            if (!success) return BadRequest(new { message });
            return Ok(result);
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
