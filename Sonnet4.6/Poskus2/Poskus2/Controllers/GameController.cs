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
    [Route("game")]
    public class GameController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly GameSessionService _gameService;
        private readonly JwtService _jwtService;

        public GameController(AppDbContext db, GameSessionService gameService, JwtService jwtService)
        {
            _db = db;
            _gameService = gameService;
            _jwtService = jwtService;
        }

        [HttpPost("start/{quizId:int}")]
        [Authorize]
        public async Task<IActionResult> StartGame(int quizId)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var quiz = await _db.Quizzes
                .Include(q => q.questions.OrderBy(qn => qn.orderIndex))
                .FirstOrDefaultAsync(q => q.id == quizId);

            if (quiz == null)
                return NotFound(new { message = "Kviz ne obstaja." });

            if (!quiz.questions.Any())
                return BadRequest(new { message = "Kviz nima vprašanj." });

            var session = await _gameService.GetOrCreateSessionAsync(quizId);
            if (session == null)
                return BadRequest(new { message = "Seja kviza je zaključena. Preizkusite malo kasneje." });

            var existing = await _db.UserGameSessions
                .FirstOrDefaultAsync(ugs => ugs.gameSessionId == session.id && ugs.userId == userId.Value);

            if (existing == null)
            {
                // Označi vse prejšnje seje tega uporabnika za isti kviz kot ne-zadnje
                var previousSessions = await _db.UserGameSessions
                    .Include(ugs => ugs.gameSession)
                    .Where(ugs =>
                        ugs.userId == userId.Value &&
                        ugs.gameSession.quizId == quizId &&
                        (ugs.isLatest == true || ugs.isLatest == null))
                    .ToListAsync();

                foreach (var prev in previousSessions)
                    prev.isLatest = false;

                existing = new UserGameSession
                {
                    gameSessionId = session.id,
                    userId = userId.Value,
                    joinedAt = DateTime.UtcNow,
                    isLatest = true
                };
                _db.UserGameSessions.Add(existing);
                await _db.SaveChangesAsync();
            }

            var firstQuestion = quiz.questions.OrderBy(q => q.orderIndex).First();

            var wsUrl = $"ws://{Request.Host}/game/ws/{session.id}";

            return Ok(new StartGameResponseDto
            {
                gameSessionId = session.id,
                quizId = quiz.id,
                quizTitle = quiz.title,
                endsAt = session.endsAt!.Value,
                totalQuestions = quiz.questions.Count,
                firstQuestion = new QuestionSendDto(firstQuestion.id, firstQuestion.questionText),
                wsUrl = wsUrl
            });
        }

        [Route("ws/{gameSessionId:int}")]
        public async Task HandleWebSocket(int gameSessionId)
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

            var session = await _db.GameSessions.FindAsync(gameSessionId);
            if (session == null)
            {
                HttpContext.Response.StatusCode = 404;
                return;
            }

            var participant = await _db.UserGameSessions
                .AnyAsync(ugs => ugs.gameSessionId == gameSessionId && ugs.userId == userId.Value);

            if (!participant)
            {
                HttpContext.Response.StatusCode = 403;
                return;
            }

            var ws = await HttpContext.WebSockets.AcceptWebSocketAsync();
            await _gameService.AddWebSocketAsync(gameSessionId, userId.Value, ws);
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
