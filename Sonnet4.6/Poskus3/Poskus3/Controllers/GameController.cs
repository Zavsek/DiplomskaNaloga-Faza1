using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Poskus3.Data;
using Poskus3.DTOs;
using Poskus3.Entities;
using Poskus3.Services;
using System.Security.Claims;

namespace Poskus3.Controllers
{
    [ApiController]
    [Authorize]
    public class GameController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly GameSessionService _sessionService;

        public GameController(AppDbContext db, GameSessionService sessionService)
        {
            _db = db;
            _sessionService = sessionService;
        }

        // Pomožna metoda: pridobi userId iz JWT
        private int GetUserId() =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub")
                ?? throw new UnauthorizedAccessException());

        // POST /game/start/{quiz}
        [HttpPost("game/start/{quiz:int}")]
        public async Task<IActionResult> StartGame(int quiz)
        {
            int userId;
            try { userId = GetUserId(); }
            catch { return Unauthorized(new { message = "Neveljaven token." }); }

            var quizEntity = await _db.Quizzes
                .Include(q => q.questions)
                .FirstOrDefaultAsync(q => q.id == quiz);

            if (quizEntity == null)
                return NotFound(new { message = $"Kviz z ID {quiz} ne obstaja." });

            if (!quizEntity.questions.Any())
                return BadRequest(new { message = "Kviz nima vprašanj." });

            GameSession session;
            try
            {
                session = await _sessionService.StartSessionAsync(userId, quiz);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }

            var active = _sessionService.GetActiveSession(session.id)!;
            var firstQuestion = quizEntity.questions.OrderBy(q => q.orderIndex).First();

            return Ok(new
            {
                sessionId = session.id,
                quizTitle = quizEntity.title,
                totalQuestions = quizEntity.questions.Count,
                expiresAt = active.ExpiresAt,
                durationSeconds = (int)quizEntity.duration.TotalSeconds,
                firstQuestion = new QuestionSendDto(firstQuestion.id, firstQuestion.questionText)
            });
        }

        // POST /api/answer
        [HttpPost("api/answer")]
        public async Task<IActionResult> SubmitAnswer([FromBody] AnswerDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (!dto.IsValid())
                return BadRequest(new { message = "Neveljaven odgovor. Dovoljene vrednosti so A, B, C ali D." });

            int userId;
            try { userId = GetUserId(); }
            catch { return Unauthorized(new { message = "Neveljaven token." }); }

            var active = _sessionService.GetActiveSessionForUser(userId);
            if (active == null)
                return BadRequest(new { message = "Nimaš aktivne seje. Najprej začni kviz." });

            if (active.IsExpired)
            {
                await _sessionService.FinishSessionAsync(active.SessionId, expired: true);
                return BadRequest(new { message = "Čas kviza je potekel. Seja je zaključena." });
            }

            var question = await _db.Questions
                .FirstOrDefaultAsync(q => q.id == dto.questionId && q.quizId == active.QuizId);

            if (question == null)
                return NotFound(new { message = "Vprašanje ne pripada temu kvizu." });

            var existing = await _db.UserAnswers
                .FirstOrDefaultAsync(ua => ua.sessionId == active.SessionId && ua.questionId == dto.questionId);

            if (existing != null)
            {
                if (existing.editCount >= 2)
                    return BadRequest(new { message = "Odgovor je bil že dvakrat popravljen. Nadaljnje spremembe niso dovoljene." });

                existing.answer = dto.answer;
                existing.editCount++;
                existing.answeredAt = DateTime.UtcNow;
            }
            else
            {
                _db.UserAnswers.Add(new Entities.UserAnswer
                {
                    sessionId = active.SessionId,
                    questionId = dto.questionId,
                    answer = dto.answer,
                    editCount = 0,
                    answeredAt = DateTime.UtcNow
                });
            }

            await _db.SaveChangesAsync();

            // Pridobi skupni napredek kviza
            var quiz = await _db.Quizzes.Include(q => q.questions).FirstAsync(q => q.id == active.QuizId);
            var progress = await _sessionService.GetQuizProgressAsync(active.QuizId, quiz.questions.Count);

            return Ok(new
            {
                message = "Odgovor shranjen.",
                questionId = dto.questionId,
                quizProgress = progress
            });
        }

        // GET /api/question/{questionId}
        [HttpGet("api/question/{questionId:int}")]
        public async Task<IActionResult> GetQuestion(int questionId)
        {
            int userId;
            try { userId = GetUserId(); }
            catch { return Unauthorized(new { message = "Neveljaven token." }); }

            var active = _sessionService.GetActiveSessionForUser(userId);
            if (active == null)
                return BadRequest(new { message = "Nimaš aktivne seje." });

            if (active.IsExpired)
            {
                await _sessionService.FinishSessionAsync(active.SessionId, expired: true);
                return BadRequest(new { message = "Čas kviza je potekel. Seja je zaključena." });
            }

            var question = await _db.Questions
                .FirstOrDefaultAsync(q => q.id == questionId && q.quizId == active.QuizId);

            if (question == null)
                return NotFound(new { message = "Vprašanje ne pripada temu kvizu ali ne obstaja." });

            var existingAnswer = await _db.UserAnswers
                .FirstOrDefaultAsync(ua => ua.sessionId == active.SessionId && ua.questionId == questionId);

            return Ok(new
            {
                question = new QuestionSendDto(question.id, question.questionText),
                currentAnswer = existingAnswer?.answer,
                editsRemaining = existingAnswer != null ? Math.Max(0, 2 - existingAnswer.editCount) : 2
            });
        }
    }

    public class AnswerDto
    {
        public int questionId { get; set; }
        public char answer { get; set; }

        public bool IsValid() => answer == 'A' || answer == 'B' || answer == 'C' || answer == 'D';
    }
}
