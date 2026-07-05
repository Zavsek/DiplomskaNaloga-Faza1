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
    [Route("api")]
    [Authorize]
    public class QuizController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly GameSessionService _gameService;

        public QuizController(AppDbContext db, GameSessionService gameService)
        {
            _db = db;
            _gameService = gameService;
        }

        [HttpPost("answer")]
        public async Task<IActionResult> SubmitAnswer([FromBody] AnswerSubmitDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var validAnswers = new[] { "A", "B", "C", "D" };
            var answerUpper = dto.answer?.ToUpper() ?? "";
            if (!validAnswers.Contains(answerUpper))
                return BadRequest(new { message = "Odgovor mora biti A, B, C ali D." });

            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var question = await _db.Questions.FindAsync(dto.questionId);
            if (question == null)
                return NotFound(new { message = "Vprašanje ne obstaja." });

            var userSession = await _db.UserGameSessions
                .Include(ugs => ugs.answers)
                .Include(ugs => ugs.gameSession)
                .FirstOrDefaultAsync(ugs =>
                    ugs.userId == userId.Value &&
                    ugs.gameSession.quizId == question.quizId &&
                    !ugs.gameSession.isFinished);

            if (userSession == null)
                return BadRequest(new { message = "Nimate aktivne seje za ta kviz. Najprej začnite igro." });

            if (userSession.gameSession.isFinished ||
                (userSession.gameSession.endsAt.HasValue && DateTime.UtcNow >= userSession.gameSession.endsAt.Value))
                return BadRequest(new { message = "Čas kviza je potekel. Reševanje ni več dovoljeno." });

            if (_gameService.IsSessionFinished(userSession.gameSessionId))
                return BadRequest(new { message = "Čas kviza je potekel. Reševanje ni več dovoljeno." });

            var existingAnswer = userSession.answers.FirstOrDefault(a => a.questionId == dto.questionId);

            if (existingAnswer == null)
            {
                var now = DateTime.UtcNow;
                var newAnswer = new UserAnswer
                {
                    userGameSessionId = userSession.id,
                    questionId = dto.questionId,
                    answer = char.Parse(answerUpper),
                    editCount = 0,
                    firstAnsweredAt = now,
                    answeredAt = now
                };
                _db.UserAnswers.Add(newAnswer);
                await _db.SaveChangesAsync();

                await _gameService.SendProgressUpdateAsync(userSession.gameSessionId);

                var totalQ = await _db.Questions.CountAsync(q => q.quizId == question.quizId);
                return Ok(new AnswerResultDto
                {
                    accepted = true,
                    message = "Odgovor sprejet.",
                    editCount = 0,
                    answeredQuestions = userSession.answers.Count + 1,
                    totalQuestions = totalQ
                });
            }

            if (existingAnswer.editCount >= 2)
                return BadRequest(new AnswerResultDto
                {
                    accepted = false,
                    message = "Odgovora ni mogoče več popravljati. Dosežena je maksimalna meja popravkov (2).",
                    editCount = existingAnswer.editCount,
                    answeredQuestions = userSession.answers.Count,
                    totalQuestions = await _db.Questions.CountAsync(q => q.quizId == question.quizId)
                });

            existingAnswer.answer = char.Parse(answerUpper);
            existingAnswer.editCount++;
            existingAnswer.answeredAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await _gameService.SendProgressUpdateAsync(userSession.gameSessionId);

            return Ok(new AnswerResultDto
            {
                accepted = true,
                message = $"Odgovor posodobljen. Ostalo popravkov: {2 - existingAnswer.editCount}.",
                editCount = existingAnswer.editCount,
                answeredQuestions = userSession.answers.Count,
                totalQuestions = await _db.Questions.CountAsync(q => q.quizId == question.quizId)
            });
        }

        [HttpGet("question/{questionId:int}")]
        public async Task<IActionResult> GetQuestion(int questionId)
        {
            var userId = GetUserId();
            if (userId == null) return Unauthorized();

            var question = await _db.Questions.FindAsync(questionId);
            if (question == null)
                return NotFound(new { message = "Vprašanje ne obstaja." });

            var userSession = await _db.UserGameSessions
                .Include(ugs => ugs.answers)
                .Include(ugs => ugs.gameSession)
                .ThenInclude(gs => gs.quiz)
                .ThenInclude(q => q.questions)
                .FirstOrDefaultAsync(ugs =>
                    ugs.userId == userId.Value &&
                    ugs.gameSession.quizId == question.quizId);

            if (userSession == null)
                return BadRequest(new { message = "Nimate seje za ta kviz." });

            var existingAnswer = userSession.answers.FirstOrDefault(a => a.questionId == questionId);
            var totalQuestions = userSession.gameSession.quiz.questions.Count;

            return Ok(new QuestionDetailDto
            {
                questionId = question.id,
                question = question.questionText,
                orderIndex = question.orderIndex,
                totalQuestions = totalQuestions,
                yourAnswer = existingAnswer?.answer,
                editCount = existingAnswer?.editCount ?? 0,
                canEdit = existingAnswer == null || existingAnswer.editCount < 2
            });
        }

        private int? GetUserId()
        {
            var sub = User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            return int.TryParse(sub, out var id) ? id : null;
        }
    }
}
