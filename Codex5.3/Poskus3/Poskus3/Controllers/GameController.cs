using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Poskus3.Data;
using Poskus3.DTOs;
using Poskus3.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Poskus3.Controllers
{
    [ApiController]
    [Authorize]
    [Route("")]
    public class GameController : ControllerBase
    {
        private readonly AppDbContext _dbContext;

        public GameController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpPost("game/start/{quiz:int}")]
        public async Task<IActionResult> StartGame([FromRoute] int quiz)
        {
            var userId = GetUserId();
            if (userId is null)
            {
                return Unauthorized(new { message = "Invalid token subject." });
            }

            var quizEntity = await _dbContext.Quizzes
                .Include(q => q.questions)
                .SingleOrDefaultAsync(q => q.id == quiz);

            if (quizEntity is null || quizEntity.questions.Count == 0)
            {
                return NotFound(new { message = "Quiz not found or has no questions." });
            }

            var now = DateTime.UtcNow;
            var session = await _dbContext.GameSessions
                .Where(gs => gs.userId == userId && gs.quizId == quiz && gs.completedAtUtc == null)
                .OrderByDescending(gs => gs.startedAtUtc)
                .FirstOrDefaultAsync();

            if (session is not null && session.expiresAtUtc <= now)
            {
                session.completedAtUtc = now;
                session.completionReason = "TimeExpired";
                await _dbContext.SaveChangesAsync();
                session = null;
            }

            if (session is null)
            {
                session = new GameSession
                {
                    userId = userId.Value,
                    quizId = quizEntity.id,
                    startedAtUtc = now,
                    lastInteractionAtUtc = now,
                    expiresAtUtc = now.Add(quizEntity.duration)
                };

                _dbContext.GameSessions.Add(session);
                await _dbContext.SaveChangesAsync();
            }

            var answeredQuestionIds = await _dbContext.GameSessionAnswers
                .Where(a => a.gameSessionId == session.id)
                .Select(a => a.questionId)
                .ToListAsync();

            var nextQuestion = quizEntity.questions
                .OrderBy(q => q.orderIndex)
                .ThenBy(q => q.id)
                .FirstOrDefault(q => !answeredQuestionIds.Contains(q.id));

            if (nextQuestion is null)
            {
                session.completedAtUtc = now;
                session.completionReason = "Completed";
                await _dbContext.SaveChangesAsync();
                return Ok(new
                {
                    isCompleted = true,
                    message = "Quiz already completed."
                });
            }

            return Ok(new
            {
                isCompleted = false,
                sessionId = session.id,
                expiresAtUtc = session.expiresAtUtc,
                question = new QuestionSendDto(nextQuestion.id, nextQuestion.questionText)
            });
        }

        [HttpPost("api/answer")]
        public async Task<IActionResult> SubmitAnswer([FromBody] AnswerSubmitRequestDto request)
        {
            var userId = GetUserId();
            if (userId is null)
            {
                return Unauthorized(new { message = "Invalid token subject." });
            }

            var parsedAnswer = request.answer.Trim().ToUpperInvariant();
            if (parsedAnswer.Length != 1 || !"ABCD".Contains(parsedAnswer[0]))
            {
                return BadRequest(new { message = "Only A, B, C or D are valid answers." });
            }

            var question = await _dbContext.Questions.SingleOrDefaultAsync(q => q.id == request.questionId);
            if (question is null)
            {
                return NotFound(new { message = "Question not found." });
            }

            var now = DateTime.UtcNow;
            var session = await _dbContext.GameSessions
                .Where(gs => gs.userId == userId && gs.quizId == question.quizId && gs.completedAtUtc == null)
                .OrderByDescending(gs => gs.startedAtUtc)
                .FirstOrDefaultAsync();

            if (session is null)
            {
                return BadRequest(new { message = "No active game session for this quiz." });
            }

            if (session.expiresAtUtc <= now)
            {
                session.completedAtUtc = now;
                session.completionReason = "TimeExpired";
                await _dbContext.SaveChangesAsync();
                return BadRequest(new { isCompleted = true, message = "Quiz time has expired. Quiz is finished." });
            }

            var answer = await _dbContext.GameSessionAnswers
                .SingleOrDefaultAsync(a => a.gameSessionId == session.id && a.questionId == request.questionId);
            var previousInteraction = session.lastInteractionAtUtc ?? session.startedAtUtc;
            if (previousInteraction > now)
            {
                previousInteraction = now;
            }
            var responseTimeMs = (int)Math.Max(0, (now - previousInteraction).TotalMilliseconds);

            if (answer is null)
            {
                answer = new GameSessionAnswer
                {
                    gameSessionId = session.id,
                    questionId = request.questionId,
                    selectedAnswer = parsedAnswer[0],
                    correctionCount = 0,
                    answeredAtUtc = now
                };
                _dbContext.GameSessionAnswers.Add(answer);
            }
            else if (answer.selectedAnswer != parsedAnswer[0])
            {
                if (answer.correctionCount >= 2)
                {
                    return BadRequest(new { message = "Answer can be corrected only two times." });
                }

                answer.selectedAnswer = parsedAnswer[0];
                answer.correctionCount += 1;
                answer.answeredAtUtc = now;
            }

            var submission = new AnswerSubmission
            {
                gameSessionId = session.id,
                questionId = request.questionId,
                selectedAnswer = parsedAnswer[0],
                submittedAtUtc = now,
                responseTimeMs = responseTimeMs
            };
            _dbContext.AnswerSubmissions.Add(submission);
            session.lastInteractionAtUtc = now;

            await _dbContext.SaveChangesAsync();

            var quizQuestionIds = await _dbContext.Questions
                .Where(q => q.quizId == question.quizId)
                .OrderBy(q => q.orderIndex)
                .ThenBy(q => q.id)
                .Select(q => q.id)
                .ToListAsync();

            var answeredIds = await _dbContext.GameSessionAnswers
                .Where(a => a.gameSessionId == session.id)
                .Select(a => a.questionId)
                .ToListAsync();

            var nextQuestionId = quizQuestionIds.FirstOrDefault(id => !answeredIds.Contains(id));
            if (nextQuestionId == 0)
            {
                session.completedAtUtc = DateTime.UtcNow;
                session.completionReason = "Completed";
                await _dbContext.SaveChangesAsync();
                return Ok(new { isCompleted = true, message = "Quiz finished successfully." });
            }

            var nextQuestion = await _dbContext.Questions.SingleAsync(q => q.id == nextQuestionId);
            return Ok(new
            {
                isCompleted = false,
                question = new QuestionSendDto(nextQuestion.id, nextQuestion.questionText)
            });
        }

        [HttpGet("api/question/{questionId:int}")]
        public async Task<IActionResult> GetAnsweredQuestion([FromRoute] int questionId)
        {
            var userId = GetUserId();
            if (userId is null)
            {
                return Unauthorized(new { message = "Invalid token subject." });
            }

            var question = await _dbContext.Questions.SingleOrDefaultAsync(q => q.id == questionId);
            if (question is null)
            {
                return NotFound(new { message = "Question not found." });
            }

            var session = await _dbContext.GameSessions
                .Where(gs => gs.userId == userId && gs.quizId == question.quizId)
                .OrderByDescending(gs => gs.startedAtUtc)
                .FirstOrDefaultAsync();

            if (session is null)
            {
                return BadRequest(new { message = "No game session found for this quiz." });
            }

            var answer = await _dbContext.GameSessionAnswers
                .SingleOrDefaultAsync(a => a.gameSessionId == session.id && a.questionId == questionId);

            if (answer is null)
            {
                return BadRequest(new { message = "Question has not been answered yet." });
            }

            var isExpired = session.completedAtUtc is not null || session.expiresAtUtc <= DateTime.UtcNow;
            var correctionsRemaining = Math.Max(0, 2 - answer.correctionCount);

            return Ok(new
            {
                isCompleted = isExpired,
                question = new QuestionSendDto(question.id, question.questionText),
                currentAnswer = answer.selectedAnswer,
                correctionsRemaining,
                canEditAnswer = !isExpired && correctionsRemaining > 0
            });
        }

        [HttpGet("api/game/progress/{quizId:int}")]
        public async Task<IActionResult> GetLiveProgress([FromRoute] int quizId)
        {
            var userId = GetUserId();
            if (userId is null)
            {
                return Unauthorized(new { message = "Invalid token subject." });
            }

            var now = DateTime.UtcNow;
            var mySession = await _dbContext.GameSessions
                .Where(gs => gs.userId == userId && gs.quizId == quizId && gs.completedAtUtc == null && gs.expiresAtUtc > now)
                .OrderByDescending(gs => gs.startedAtUtc)
                .FirstOrDefaultAsync();

            if (mySession is null)
            {
                var lastSession = await _dbContext.GameSessions
                    .Where(gs => gs.userId == userId && gs.quizId == quizId)
                    .OrderByDescending(gs => gs.startedAtUtc)
                    .FirstOrDefaultAsync();

                if (lastSession is not null)
                {
                    return Ok(new
                    {
                        quizId,
                        isCompleted = true,
                        completionReason = lastSession.completionReason ?? "Completed",
                        completedAtUtc = lastSession.completedAtUtc
                    });
                }

                return BadRequest(new { message = "No game session found for this quiz." });
            }

            var questionIds = await _dbContext.Questions
                .Where(q => q.quizId == quizId)
                .Select(q => q.id)
                .ToListAsync();

            if (questionIds.Count == 0)
            {
                return NotFound(new { message = "Quiz has no questions." });
            }

            var myAnsweredCount = await _dbContext.GameSessionAnswers
                .Where(a => a.gameSessionId == mySession.id)
                .CountAsync();

            var otherSessionIds = await _dbContext.GameSessions
                .Where(gs => gs.quizId == quizId && gs.userId != userId && gs.completedAtUtc == null && gs.expiresAtUtc > now)
                .Select(gs => gs.id)
                .ToListAsync();

            var othersCount = otherSessionIds.Count;
            var answersByOthers = othersCount == 0
                ? new Dictionary<int, int>()
                : await _dbContext.GameSessionAnswers
                    .Where(a => otherSessionIds.Contains(a.gameSessionId))
                    .GroupBy(a => a.questionId)
                    .Select(g => new { questionId = g.Key, count = g.Count() })
                    .ToDictionaryAsync(x => x.questionId, x => x.count);

            var perQuestionShare = questionIds.Select(questionId =>
            {
                var answered = answersByOthers.GetValueOrDefault(questionId, 0);
                var percent = othersCount == 0 ? 0.0 : Math.Round((double)answered / othersCount * 100.0, 2);
                return new
                {
                    questionId,
                    answeredByOthersPercent = percent
                };
            });

            var totalPossibleAnswers = othersCount * questionIds.Count;
            var totalAnswersByOthers = answersByOthers.Values.Sum();
            var overallOthersAnsweredPercent = totalPossibleAnswers == 0
                ? 0.0
                : Math.Round((double)totalAnswersByOthers / totalPossibleAnswers * 100.0, 2);

            var myProgressPercent = Math.Round((double)myAnsweredCount / questionIds.Count * 100.0, 2);

            return Ok(new
            {
                quizId,
                isCompleted = false,
                activeOtherUsers = othersCount,
                myProgressPercent,
                overallOthersAnsweredPercent,
                perQuestionShare
            });
        }

        private int? GetUserId()
        {
            var subClaim = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

            return int.TryParse(subClaim, out var userId) ? userId : null;
        }
    }
}
