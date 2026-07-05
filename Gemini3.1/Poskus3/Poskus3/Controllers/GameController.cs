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
        private readonly AppDbContext _context;
        private readonly GameWebSocketManager _wsManager;

        public GameController(AppDbContext context, GameWebSocketManager wsManager)
        {
            _context = context;
            _wsManager = wsManager;
        }

        [HttpPost("/game/start/{quiz}")]
        public async Task<IActionResult> StartQuiz(int quiz)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out var userId)) return Unauthorized();

            var quizEntity = await _context.Quizzes
                .Include(q => q.questions)
                .FirstOrDefaultAsync(q => q.id == quiz);

            if (quizEntity == null) return NotFound(new { message = "Quiz not found." });

            var activeSession = await _context.QuizSessions
                .FirstOrDefaultAsync(s => s.userId == userId && s.quizId == quiz && !s.isFinished);

            if (activeSession != null && activeSession.endTime <= DateTime.UtcNow)
            {
                activeSession.isFinished = true;
                await _context.SaveChangesAsync();
                activeSession = null;
            }

            if (activeSession == null)
            {
                activeSession = new QuizSession
                {
                    userId = userId,
                    quizId = quiz,
                    startTime = DateTime.UtcNow,
                    endTime = DateTime.UtcNow.Add(quizEntity.duration),
                    lastActionTime = DateTime.UtcNow,
                    isFinished = false
                };
                _context.QuizSessions.Add(activeSession);
                await _context.SaveChangesAsync();
            }

            _wsManager.SetUserQuizSession(userId, quiz);

            var firstQuestion = quizEntity.questions.OrderBy(q => q.orderIndex).FirstOrDefault();
            if (firstQuestion == null) return NotFound(new { message = "No questions in this quiz." });

            return Ok(new QuestionSendDto(firstQuestion.id, firstQuestion.questionText));
        }

        [HttpPost("/api/answer")]
        public async Task<IActionResult> SubmitAnswer([FromBody] AnswerSubmitDto dto)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out var userId)) return Unauthorized();

            var question = await _context.Questions
                .Include(q => q.quiz)
                .ThenInclude(qz => qz.questions)
                .FirstOrDefaultAsync(q => q.id == dto.QuestionId);

            if (question == null) return NotFound(new { message = "Question not found." });

            var session = await _context.QuizSessions
                .Include(s => s.answers)
                .FirstOrDefaultAsync(s => s.userId == userId && s.quizId == question.quizId && !s.isFinished);

            if (session == null) return BadRequest(new { message = "No active session for this quiz." });

            if (session.endTime <= DateTime.UtcNow)
            {
                session.isFinished = true;
                await _context.SaveChangesAsync();
                return BadRequest(new { message = "Quiz time has expired." });
            }

            if (session.lastActionTime == default) session.lastActionTime = session.startTime;
            var now = DateTime.UtcNow;
            var actionTime = now - session.lastActionTime;
            session.lastActionTime = now;

            var existingAnswer = session.answers.FirstOrDefault(a => a.questionId == dto.QuestionId);
            if (existingAnswer != null)
            {
                if (existingAnswer.correctionCount >= 2)
                {
                    return BadRequest(new { message = "Maximum corrections (2) reached for this question." });
                }
                existingAnswer.submittedAnswer = dto.Answer;
                existingAnswer.correctionCount++;
                existingAnswer.updatedAt = now;
                existingAnswer.timeSpent += actionTime;
            }
            else
            {
                var newAnswer = new QuizSessionAnswer
                {
                    questionId = dto.QuestionId,
                    submittedAnswer = dto.Answer,
                    updatedAt = now,
                    timeSpent = actionTime
                };
                session.answers.Add(newAnswer);
            }

            await _context.SaveChangesAsync();

            var answeredCount = session.answers.Select(a => a.questionId).Distinct().Count();
            var totalQuestions = question.quiz.questions.Count;

            await _wsManager.BroadcastProgressAsync(question.quizId, userId, answeredCount, totalQuestions);

            return Ok(new { message = "Answer saved successfully." });
        }

        [HttpGet("/api/question/{questionId}")]
        public async Task<IActionResult> GetQuestion(int questionId)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(userIdStr, out var userId)) return Unauthorized();

            var question = await _context.Questions
                .Include(q => q.quiz)
                .FirstOrDefaultAsync(q => q.id == questionId);

            if (question == null) return NotFound(new { message = "Question not found." });

            var session = await _context.QuizSessions
                .FirstOrDefaultAsync(s => s.userId == userId && s.quizId == question.quizId && !s.isFinished);

            if (session == null) return BadRequest(new { message = "No active session for this quiz." });

            if (session.endTime <= DateTime.UtcNow)
            {
                session.isFinished = true;
                await _context.SaveChangesAsync();
                return BadRequest(new { message = "Quiz time has expired." });
            }

            return Ok(new QuestionSendDto(question.id, question.questionText));
        }
    }
}