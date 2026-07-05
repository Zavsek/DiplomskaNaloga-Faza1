using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Poskus2.Data;
using Poskus2.DTOs;
using Poskus2.DTOs.Game;
using Poskus2.Entities;
using Poskus2.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Poskus2.Controllers
{
    [ApiController]
    [Authorize]
    public class GameController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        private readonly QuizProgressNotifier _quizProgressNotifier;

        public GameController(AppDbContext dbContext, QuizProgressNotifier quizProgressNotifier)
        {
            _dbContext = dbContext;
            _quizProgressNotifier = quizProgressNotifier;
        }

        [HttpPost("/game/start/{quiz:int}")]
        public async Task<IActionResult> StartGame([FromRoute] int quiz)
        {
            var userId = GetUserId();
            if (userId is null)
            {
                return Unauthorized(new { message = "Missing user identity." });
            }

            var quizEntity = await _dbContext.Quizzes
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.id == quiz);
            if (quizEntity is null)
            {
                return NotFound(new { message = "Quiz not found." });
            }

            var questionList = await _dbContext.Questions
                .Where(q => q.quizId == quiz)
                .OrderBy(q => q.orderIndex)
                .Select(q => new { q.id, q.questionText })
                .ToListAsync();

            if (questionList.Count == 0)
            {
                return BadRequest(new { message = "Quiz has no questions." });
            }

            var now = DateTimeOffset.UtcNow;
            var session = await _dbContext.GameSessions
                .Where(gs => gs.userId == userId && gs.quizId == quiz && gs.status == GameSessionStatus.InProgress)
                .OrderByDescending(gs => gs.startedAtUtc)
                .FirstOrDefaultAsync();

            if (session is null)
            {
                session = new GameSession
                {
                    userId = userId.Value,
                    quizId = quiz,
                    status = GameSessionStatus.InProgress,
                    startedAtUtc = now,
                    endsAtUtc = now.Add(quizEntity.duration)
                };
                _dbContext.GameSessions.Add(session);
                await _dbContext.SaveChangesAsync();
            }
            else if (session.endsAtUtc <= now)
            {
                session.status = GameSessionStatus.Finished;
                session.completedAtUtc = now;
                await _dbContext.SaveChangesAsync();
                return StatusCode(StatusCodes.Status410Gone, new
                {
                    message = "Quiz time has expired.",
                    sessionId = session.id,
                    endedAtUtc = session.completedAtUtc
                });
            }

            var answeredQuestionIds = await _dbContext.GameAnswers
                .Where(a => a.gameSessionId == session.id)
                .Select(a => a.questionId)
                .ToHashSetAsync();

            var nextQuestion = questionList.FirstOrDefault(q => !answeredQuestionIds.Contains(q.id));
            if (nextQuestion is null)
            {
                session.status = GameSessionStatus.Finished;
                session.completedAtUtc = now;
                await _dbContext.SaveChangesAsync();
                await _quizProgressNotifier.BroadcastProgressAsync(quiz);

                return Ok(new
                {
                    message = "Quiz completed.",
                    sessionId = session.id,
                    endedAtUtc = session.completedAtUtc
                });
            }

            await _quizProgressNotifier.BroadcastProgressAsync(quiz);

            return Ok(new
            {
                sessionId = session.id,
                quizId = quiz,
                endsAtUtc = session.endsAtUtc,
                question = new QuestionSendDto(nextQuestion.id, nextQuestion.questionText)
            });
        }

        [HttpPost("/api/answer")]
        public async Task<IActionResult> SubmitAnswer([FromBody] AnswerRequestDto request)
        {
            var userId = GetUserId();
            if (userId is null)
            {
                return Unauthorized(new { message = "Missing user identity." });
            }

            if (!TryParseAnswer(request.answer, out var normalizedAnswer))
            {
                return BadRequest(new { message = "Answer must be one of: A, B, C or D." });
            }

            var question = await _dbContext.Questions
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.id == request.questionId);
            if (question is null)
            {
                return NotFound(new { message = "Question not found." });
            }

            var now = DateTimeOffset.UtcNow;
            var session = await _dbContext.GameSessions
                .Where(gs => gs.userId == userId && gs.quizId == question.quizId && gs.status == GameSessionStatus.InProgress)
                .OrderByDescending(gs => gs.startedAtUtc)
                .FirstOrDefaultAsync();

            if (session is null)
            {
                return Conflict(new { message = "No active game session for this quiz. Start game first." });
            }

            if (session.endsAtUtc <= now)
            {
                session.status = GameSessionStatus.Finished;
                session.completedAtUtc = now;
                await _dbContext.SaveChangesAsync();
                await _quizProgressNotifier.BroadcastProgressAsync(question.quizId);
                return StatusCode(StatusCodes.Status410Gone, new { message = "Quiz time has expired." });
            }

            var existingAnswer = await _dbContext.GameAnswers
                .FirstOrDefaultAsync(a => a.gameSessionId == session.id && a.questionId == request.questionId);
            var isCorrect = char.ToUpperInvariant(question.answer) == normalizedAnswer;

            if (existingAnswer is null)
            {
                existingAnswer = new GameAnswer
                {
                    gameSessionId = session.id,
                    questionId = request.questionId,
                    selectedAnswer = normalizedAnswer,
                    correctionCount = 0,
                    answeredAtUtc = now
                };
                _dbContext.GameAnswers.Add(existingAnswer);
            }
            else
            {
                var isChangingAnswer = existingAnswer.selectedAnswer != normalizedAnswer;
                if (isChangingAnswer && existingAnswer.correctionCount >= 2)
                {
                    return BadRequest(new { message = "Answer can be corrected at most two times." });
                }

                if (isChangingAnswer)
                {
                    existingAnswer.selectedAnswer = normalizedAnswer;
                    existingAnswer.correctionCount += 1;
                }

                existingAnswer.answeredAtUtc = now;
            }

            _dbContext.GameAnswerAttempts.Add(new GameAnswerAttempt
            {
                gameSessionId = session.id,
                questionId = request.questionId,
                selectedAnswer = normalizedAnswer,
                isCorrect = isCorrect,
                submittedAtUtc = now
            });

            await _dbContext.SaveChangesAsync();

            var totalQuestionCount = await _dbContext.Questions.CountAsync(q => q.quizId == question.quizId);
            var answeredCount = await _dbContext.GameAnswers.CountAsync(a => a.gameSessionId == session.id);

            if (answeredCount >= totalQuestionCount)
            {
                session.status = GameSessionStatus.Finished;
                session.completedAtUtc = now;
                await _dbContext.SaveChangesAsync();
            }

            await _quizProgressNotifier.BroadcastProgressAsync(question.quizId);

            return Ok(new
            {
                message = "Answer saved.",
                questionId = request.questionId,
                answer = existingAnswer.selectedAnswer,
                correctionCount = existingAnswer.correctionCount,
                correctionsRemaining = Math.Max(0, 2 - existingAnswer.correctionCount),
                quizFinished = session.status == GameSessionStatus.Finished
            });
        }

        [HttpGet("/api/question/{questionId:int}")]
        public async Task<IActionResult> GetQuestion([FromRoute] int questionId)
        {
            var userId = GetUserId();
            if (userId is null)
            {
                return Unauthorized(new { message = "Missing user identity." });
            }

            var question = await _dbContext.Questions
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.id == questionId);
            if (question is null)
            {
                return NotFound(new { message = "Question not found." });
            }

            var now = DateTimeOffset.UtcNow;
            var session = await _dbContext.GameSessions
                .Where(gs => gs.userId == userId &&
                             gs.quizId == question.quizId &&
                             gs.status == GameSessionStatus.InProgress)
                .OrderByDescending(gs => gs.startedAtUtc)
                .FirstOrDefaultAsync();

            if (session is null)
            {
                return Conflict(new { message = "No active game session for this question's quiz." });
            }

            if (session.endsAtUtc <= now)
            {
                session.status = GameSessionStatus.Finished;
                session.completedAtUtc = now;
                await _dbContext.SaveChangesAsync();
                await _quizProgressNotifier.BroadcastProgressAsync(question.quizId);
                return StatusCode(StatusCodes.Status410Gone, new { message = "Quiz time has expired." });
            }

            var existingAnswer = await _dbContext.GameAnswers
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.gameSessionId == session.id && a.questionId == questionId);

            return Ok(new
            {
                sessionId = session.id,
                endsAtUtc = session.endsAtUtc,
                question = new QuestionSendDto(question.id, question.questionText),
                previousAnswer = existingAnswer is null ? null : existingAnswer.selectedAnswer.ToString(),
                correctionCount = existingAnswer?.correctionCount ?? 0,
                correctionsRemaining = existingAnswer is null ? 2 : Math.Max(0, 2 - existingAnswer.correctionCount)
            });
        }

        private int? GetUserId()
        {
            var sub = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
            return int.TryParse(sub, out var userId) ? userId : null;
        }

        private static bool TryParseAnswer(string answer, out char normalized)
        {
            normalized = '\0';
            if (string.IsNullOrWhiteSpace(answer))
            {
                return false;
            }

            var candidate = char.ToUpperInvariant(answer.Trim()[0]);
            if (candidate is not ('A' or 'B' or 'C' or 'D'))
            {
                return false;
            }

            normalized = candidate;
            return true;
        }
    }
}
