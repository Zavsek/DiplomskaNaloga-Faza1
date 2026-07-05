using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Poskus1.Data;
using Poskus1.DTOs;
using Poskus1.Entities;
using Poskus1.Services;
using System.Security.Claims;
using System.Net.WebSockets;
using System.Text;

namespace Poskus1.Controllers
{
    [ApiController]
    [Authorize]
    public class GameController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly WebSocketManagerService _wsManager;

        public GameController(AppDbContext context, WebSocketManagerService wsManager)
        {
            _context = context;
            _wsManager = wsManager;
        }

        [HttpGet("/game/start/{quizId}")]
        public async Task<IActionResult> StartGame(int quizId)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var quiz = await _context.Quizzes
                .Include(q => q.questions)
                .FirstOrDefaultAsync(q => q.id == quizId);

            if (quiz == null)
            {
                return NotFound(new { message = "Quiz not found." });
            }

            var activeSession = await _context.QuizSessions
                .FirstOrDefaultAsync(s => s.UserId == userId && s.QuizId == quizId && !s.IsCompleted);

            if (activeSession != null)
            {
                if (activeSession.ExpiresAt <= DateTime.UtcNow)
                {
                    activeSession.IsCompleted = true;
                    await _context.SaveChangesAsync();
                }
                else
                {
                    // return the first question if already started
                    var q = quiz.questions.OrderBy(q => q.orderIndex).FirstOrDefault();
                    if (q == null) return NotFound(new { message = "Quiz has no questions." });
                    return Ok(new QuestionSendDto(q.id, q.questionText));
                }
            }

            var newSession = new QuizSession
            {
                UserId = userId,
                QuizId = quizId,
                StartTime = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(quiz.duration),
                IsCompleted = false
            };

            _context.QuizSessions.Add(newSession);
            await _context.SaveChangesAsync();

            var firstQuestion = quiz.questions.OrderBy(q => q.orderIndex).FirstOrDefault();
            if (firstQuestion == null) return NotFound(new { message = "Quiz has no questions." });

            return Ok(new QuestionSendDto(firstQuestion.id, firstQuestion.questionText));
        }

        [HttpPost("/api/answer")]
        public async Task<IActionResult> SubmitAnswer([FromBody] AnswerSubmitDto request)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var validAnswers = new[] { 'A', 'B', 'C', 'D' };
            var submittedUpper = char.ToUpper(request.answer);

            if (!validAnswers.Contains(submittedUpper))
            {
                return BadRequest(new { message = "Invalid answer. Must be A, B, C, or D." });
            }

            var question = await _context.Questions.FirstOrDefaultAsync(q => q.id == request.questionId);
            if (question == null) return NotFound(new { message = "Question not found." });

            var session = await _context.QuizSessions
                .Include(s => s.Answers)
                .FirstOrDefaultAsync(s => s.UserId == userId && s.QuizId == question.quizId && !s.IsCompleted);

            if (session == null)
            {
                return BadRequest(new { message = "No active session for this quiz." });
            }

            if (session.ExpiresAt <= DateTime.UtcNow)
            {
                session.IsCompleted = true;
                await _context.SaveChangesAsync();
                await _wsManager.SendTimeoutMessageAsync(userId, session.QuizId);
                return BadRequest(new { message = "Time is up! Quiz finished." });
            }

            var existingAnswer = session.Answers.FirstOrDefault(a => a.QuestionId == request.questionId);

            if (existingAnswer != null)
            {
                if (existingAnswer.ChangeCount >= 2)
                {
                    return BadRequest(new { message = "You can only change your answer twice." });
                }
                existingAnswer.SubmittedAnswer = submittedUpper;
                existingAnswer.ChangeCount++;
                existingAnswer.AnsweredAt = DateTime.UtcNow;
            }
            else
            {
                session.Answers.Add(new QuizAnswer
                {
                    QuestionId = request.questionId,
                    SubmittedAnswer = submittedUpper,
                    ChangeCount = 0,
                    AnsweredAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();

            // Broadcast progress to all users in this quiz
            await BroadcastProgressAsync(session.QuizId);

            return Ok(new { message = "Answer submitted successfully." });
        }

        [HttpGet("/api/question/{questionId}")]
        public async Task<IActionResult> GetQuestion(int questionId)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var question = await _context.Questions.FirstOrDefaultAsync(q => q.id == questionId);
            if (question == null) return NotFound(new { message = "Question not found." });

            var session = await _context.QuizSessions
                .FirstOrDefaultAsync(s => s.UserId == userId && s.QuizId == question.quizId && !s.IsCompleted);

            if (session == null)
            {
                return BadRequest(new { message = "No active session for this quiz." });
            }

            if (session.ExpiresAt <= DateTime.UtcNow)
            {
                session.IsCompleted = true;
                await _context.SaveChangesAsync();
                await _wsManager.SendTimeoutMessageAsync(userId, session.QuizId);
                return BadRequest(new { message = "Time is up! Quiz finished." });
            }

            return Ok(new QuestionSendDto(question.id, question.questionText));
        }

        [HttpGet("/game/ws")]
        public async Task GetWebSocket([FromQuery] int quizId)
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
                
                _wsManager.AddConnection(quizId, userId, webSocket);

                var buffer = new byte[1024 * 4];
                var receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                while (!receiveResult.CloseStatus.HasValue)
                {
                    receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                }

                _wsManager.RemoveConnection(quizId, userId);
                await webSocket.CloseAsync(receiveResult.CloseStatus.Value, receiveResult.CloseStatusDescription, CancellationToken.None);
            }
            else
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            }
        }

        private async Task BroadcastProgressAsync(int quizId)
        {
            var totalQuestions = await _context.Questions.CountAsync(q => q.quizId == quizId);
            if (totalQuestions == 0) return;

            var activeSessions = await _context.QuizSessions
                .Include(s => s.Answers)
                .Where(s => s.QuizId == quizId && !s.IsCompleted)
                .ToListAsync();

            var progressData = activeSessions.Select(s => new
            {
                userId = s.UserId,
                progress = Math.Round((double)s.Answers.Select(a => a.QuestionId).Distinct().Count() / totalQuestions, 2)
            }).ToList();

            await _wsManager.SendProgressUpdateAsync(quizId, progressData);
        }
    }
}
