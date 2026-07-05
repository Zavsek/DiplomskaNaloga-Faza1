using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Poskus2.Data;
using Poskus2.DTOs;
using Poskus2.Entities;
using Poskus2.Services;
using System.Security.Claims;
using System.Net.WebSockets;
using System.Text;

namespace Poskus2.Controllers
{
    [Authorize]
    [ApiController]
    public class GameController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly QuizWebSocketManager _wsManager;

        public GameController(AppDbContext context, QuizWebSocketManager wsManager)
        {
            _context = context;
            _wsManager = wsManager;
        }

        private int GetUserId()
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                            ?? User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
            return int.Parse(userIdStr);
        }

        [HttpPost("/game/start/{quizId}")]
        public async Task<IActionResult> StartQuiz(int quizId)
        {
            var userId = GetUserId();
            var quiz = await _context.Quizzes
                .Include(q => q.questions.OrderBy(x => x.orderIndex))
                .FirstOrDefaultAsync(q => q.id == quizId);

            if (quiz == null) return NotFound("Quiz not found.");
            if (!quiz.questions.Any()) return BadRequest("Quiz has no questions.");

            var session = await _context.QuizSessions
                .FirstOrDefaultAsync(s => s.UserId == userId && s.QuizId == quizId && !s.IsFinished);

            if (session == null)
            {
                var now = DateTime.UtcNow;
                session = new QuizSession
                {
                    UserId = userId,
                    QuizId = quizId,
                    StartTime = now,
                    LastInteractionTime = now,
                    EndTime = now.Add(quiz.duration),
                    IsFinished = false
                };
                _context.QuizSessions.Add(session);
                await _context.SaveChangesAsync();
            }
            else if (session.EndTime <= DateTime.UtcNow)
            {
                session.IsFinished = true;
                await _context.SaveChangesAsync();
                return BadRequest("Time is up for this quiz.");
            }

            session.LastInteractionTime = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var firstQuestion = quiz.questions.First();
            return Ok(new QuestionSendDto(firstQuestion.id, firstQuestion.questionText));
        }

        [HttpPost("/api/answer")]
        public async Task<IActionResult> SubmitAnswer([FromBody] AnswerDto dto)
        {
            var userId = GetUserId();
            var question = await _context.Questions.Include(q => q.quiz).FirstOrDefaultAsync(q => q.id == dto.questionId);
            if (question == null) return NotFound("Question not found.");

            if (dto.answer != 'A' && dto.answer != 'B' && dto.answer != 'C' && dto.answer != 'D')
            {
                return BadRequest("Invalid answer. Must be A, B, C, or D.");
            }

            var session = await _context.QuizSessions
                .Include(s => s.Answers)
                .FirstOrDefaultAsync(s => s.UserId == userId && s.QuizId == question.quizId && !s.IsFinished);

            if (session == null) return BadRequest("No active session for this quiz.");

            if (session.EndTime <= DateTime.UtcNow)
            {
                session.IsFinished = true;
                await _context.SaveChangesAsync();
                return BadRequest("Time is up. Quiz is finished.");
            }

            var now = DateTime.UtcNow;
            var timeSpent = (now - session.LastInteractionTime).TotalMilliseconds;
            session.LastInteractionTime = now;

            var existingAnswer = session.Answers.FirstOrDefault(a => a.QuestionId == dto.questionId);
            if (existingAnswer != null)
            {
                if (existingAnswer.EditCount >= 2)
                {
                    return BadRequest("You can only edit your answer twice.");
                }
                existingAnswer.SelectedAnswer = dto.answer;
                existingAnswer.EditCount++;
                existingAnswer.TimeSpentMs += timeSpent;
                existingAnswer.UpdatedAt = now;
            }
            else
            {
                _context.QuizSessionAnswers.Add(new QuizSessionAnswer
                {
                    QuizSessionId = session.Id,
                    QuestionId = dto.questionId,
                    SelectedAnswer = dto.answer,
                    EditCount = 0,
                    TimeSpentMs = timeSpent > 0 ? timeSpent : 0,
                    UpdatedAt = now
                });
            }

            await _context.SaveChangesAsync();

            // Broadcast progress to all users in this quiz
            var totalQuestions = await _context.Questions.CountAsync(q => q.quizId == question.quizId);
            
            var activeSessions = await _context.QuizSessions
                .Include(s => s.Answers)
                .Where(s => s.QuizId == question.quizId && !s.IsFinished)
                .ToListAsync();

            var progressData = activeSessions.Select(s => new
            {
                UserId = s.UserId,
                Progress = totalQuestions > 0 ? (double)s.Answers.Count / totalQuestions : 0
            });

            await _wsManager.BroadcastProgressAsync(question.quizId, progressData);

            return Ok(new { message = "Answer submitted successfully." });
        }

        [HttpGet("/api/question/{questionId}")]
        public async Task<IActionResult> GetQuestion(int questionId)
        {
            var userId = GetUserId();
            var question = await _context.Questions.FirstOrDefaultAsync(q => q.id == questionId);
            if (question == null) return NotFound("Question not found.");

            // Verify the user has an active session for this quiz
            var session = await _context.QuizSessions
                .FirstOrDefaultAsync(s => s.UserId == userId && s.QuizId == question.quizId && !s.IsFinished);

            if (session == null) return BadRequest("No active session for this quiz.");
            if (session.EndTime <= DateTime.UtcNow)
            {
                session.IsFinished = true;
                await _context.SaveChangesAsync();
                return BadRequest("Time is up. Quiz is finished.");
            }

            session.LastInteractionTime = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new QuestionSendDto(question.id, question.questionText));
        }

        [AllowAnonymous]
        [HttpGet("/game/ws/{quizId}")]
        public async Task ConnectWebSocket(int quizId, [FromQuery] string token)
        {
            if (HttpContext.WebSockets.IsWebSocketRequest)
            {
                // Authenticate token manually since AllowAnonymous is used (WS API doesn't easily send auth headers)
                if (string.IsNullOrEmpty(token))
                {
                    HttpContext.Response.StatusCode = 401;
                    return;
                }

                var tokenHandler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                var jwtSection = HttpContext.RequestServices.GetRequiredService<IConfiguration>().GetSection("Jwt");
                var secret = jwtSection["Secret"];

                try
                {
                    var validationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = jwtSection["Issuer"],
                        ValidAudience = jwtSection["Audience"],
                        IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret))
                    };

                    var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);
                    var userIdStr = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                                    ?? principal.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value;
                    var jti = principal.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;
                    
                    if (int.TryParse(userIdStr, out var userId) && jti != null)
                    {
                        var dbContext = HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                        var user = await dbContext.Users.FindAsync(userId);
                        
                        if (user == null || user.ActiveTokenId != jti)
                        {
                            HttpContext.Response.StatusCode = 401;
                            return;
                        }

                        var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();
                        _wsManager.AddSocket(quizId, userId, socket);

                        var buffer = new byte[1024 * 4];
                        while (socket.State == WebSocketState.Open)
                        {
                            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by client", CancellationToken.None);
                            }
                        }

                        _wsManager.RemoveSocket(quizId, userId);
                    }
                    else
                    {
                        HttpContext.Response.StatusCode = 401;
                    }
                }
                catch
                {
                    HttpContext.Response.StatusCode = 401;
                }
            }
            else
            {
                HttpContext.Response.StatusCode = 400;
            }
        }
    }
}