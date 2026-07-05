using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Poskus1.Data;
using Poskus1.DTOs;
using Poskus1.Entities;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Poskus1.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api")]
    public class GameController : ControllerBase
    {
        private const int MaxAnswerCorrections = 2;
        private readonly AppDbContext _dbContext;

        public GameController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpPost("/game/start/{quiz:int}")]
        public async Task<IActionResult> StartGame([FromRoute] int quiz)
        {
            var authResult = await ValidateCurrentTokenAsync();
            if (!authResult.isValid)
            {
                return Unauthorized(new { message = authResult.errorMessage });
            }

            var userId = authResult.userId;
            var nowUtc = DateTime.UtcNow;

            var selectedQuiz = await _dbContext.Quizzes
                .Include(q => q.questions)
                .FirstOrDefaultAsync(q => q.id == quiz);

            if (selectedQuiz is null)
            {
                return NotFound(new { message = "Izbrani kviz ne obstaja." });
            }

            if (selectedQuiz.questions.Count == 0)
            {
                return BadRequest(new { message = "Izbrani kviz nima vprašanj." });
            }

            var openSessions = await _dbContext.GameSessions
                .Where(gs => gs.userId == userId && gs.status == GameSessionStatus.InProgress)
                .ToListAsync();

            foreach (var openSession in openSessions)
            {
                openSession.status = GameSessionStatus.Completed;
                openSession.completedAtUtc = nowUtc;
                openSession.completionReason = "ReplacedByNewSession";
            }

            var newSession = new GameSession
            {
                userId = userId,
                quizId = selectedQuiz.id,
                startedAtUtc = nowUtc,
                expiresAtUtc = nowUtc.Add(selectedQuiz.duration),
                status = GameSessionStatus.InProgress
            };

            _dbContext.GameSessions.Add(newSession);
            await _dbContext.SaveChangesAsync();

            var firstQuestion = selectedQuiz.questions
                .OrderBy(q => q.orderIndex)
                .First();

            var othersProgress = await GetOthersProgressAsync(selectedQuiz.id, userId);

            return Ok(new
            {
                message = "Kviz uspešno zagnan.",
                session = new
                {
                    gameSessionId = newSession.id,
                    quizId = selectedQuiz.id,
                    selectedQuiz.title,
                    startedAtUtc = newSession.startedAtUtc,
                    expiresAtUtc = newSession.expiresAtUtc,
                    remainingTimeSeconds = Math.Max(0, (int)(newSession.expiresAtUtc - DateTime.UtcNow).TotalSeconds)
                },
                question = new QuestionSendDto(firstQuestion.id, firstQuestion.questionText),
                othersProgress
            });
        }

        [HttpPost("answer")]
        public async Task<IActionResult> SubmitAnswer([FromBody] AnswerRequestDto request)
        {
            var authResult = await ValidateCurrentTokenAsync();
            if (!authResult.isValid)
            {
                return Unauthorized(new { message = authResult.errorMessage });
            }

            var sessionResult = await GetActiveSessionAsync(authResult.userId);
            if (!sessionResult.isValid)
            {
                return BadRequest(new { message = sessionResult.errorMessage });
            }

            var activeSession = sessionResult.session!;
            var selectedAnswer = char.ToUpperInvariant(request.answer[0]);
            var question = await _dbContext.Questions
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.id == request.questionId && q.quizId == activeSession.quizId);

            if (question is null)
            {
                return NotFound(new { message = "Vprašanje ne pripada aktivnemu kvizu." });
            }

            var nowUtc = DateTime.UtcNow;
            var answerEntity = await _dbContext.GameAnswers
                .FirstOrDefaultAsync(a => a.gameSessionId == activeSession.id && a.questionId == question.id);

            if (answerEntity is null)
            {
                answerEntity = new GameAnswer
                {
                    gameSessionId = activeSession.id,
                    questionId = question.id,
                    selectedAnswer = selectedAnswer,
                    submittedAtUtc = nowUtc,
                    lastUpdatedAtUtc = nowUtc,
                    changeCount = 0
                };
                _dbContext.GameAnswers.Add(answerEntity);
            }
            else
            {
                if (answerEntity.selectedAnswer != selectedAnswer)
                {
                    if (answerEntity.changeCount >= MaxAnswerCorrections)
                    {
                        return BadRequest(new { message = $"Odgovor lahko popraviš največ {MaxAnswerCorrections}-krat." });
                    }

                    answerEntity.selectedAnswer = selectedAnswer;
                    answerEntity.lastUpdatedAtUtc = nowUtc;
                    answerEntity.changeCount += 1;
                }
            }

            _dbContext.GameAnswerAttempts.Add(new GameAnswerAttempt
            {
                gameSessionId = activeSession.id,
                questionId = question.id,
                submittedAnswer = selectedAnswer,
                submittedAtUtc = nowUtc
            });

            await _dbContext.SaveChangesAsync();

            var totalQuestions = await _dbContext.Questions.CountAsync(q => q.quizId == activeSession.quizId);
            var answeredCount = await _dbContext.GameAnswers.CountAsync(a => a.gameSessionId == activeSession.id);
            var isCompleted = answeredCount >= totalQuestions;

            if (isCompleted)
            {
                activeSession.status = GameSessionStatus.Completed;
                activeSession.completedAtUtc = nowUtc;
                activeSession.completionReason = "AllAnswered";
                await _dbContext.SaveChangesAsync();
            }

            var nextQuestion = await _dbContext.Questions
                .AsNoTracking()
                .Where(q => q.quizId == activeSession.quizId)
                .OrderBy(q => q.orderIndex)
                .FirstOrDefaultAsync(q => !_dbContext.GameAnswers.Any(a => a.gameSessionId == activeSession.id && a.questionId == q.id));

            var othersProgress = await GetOthersProgressAsync(activeSession.quizId, authResult.userId);

            return Ok(new
            {
                message = isCompleted ? "Kviz je zaključen, odgovorjeno je bilo na vsa vprašanja." : "Odgovor shranjen.",
                currentAnswer = answerEntity.selectedAnswer.ToString(),
                correctionCount = answerEntity.changeCount,
                remainingCorrections = Math.Max(0, MaxAnswerCorrections - answerEntity.changeCount),
                quizCompleted = isCompleted,
                nextQuestion = nextQuestion is null ? null : new QuestionSendDto(nextQuestion.id, nextQuestion.questionText),
                session = new
                {
                    activeSession.id,
                    activeSession.quizId,
                    activeSession.expiresAtUtc,
                    remainingTimeSeconds = Math.Max(0, (int)(activeSession.expiresAtUtc - DateTime.UtcNow).TotalSeconds)
                },
                othersProgress
            });
        }

        [HttpGet("question/{questionId:int}")]
        public async Task<IActionResult> GetQuestion([FromRoute] int questionId)
        {
            var authResult = await ValidateCurrentTokenAsync();
            if (!authResult.isValid)
            {
                return Unauthorized(new { message = authResult.errorMessage });
            }

            var sessionResult = await GetActiveSessionAsync(authResult.userId);
            if (!sessionResult.isValid)
            {
                return BadRequest(new { message = sessionResult.errorMessage });
            }

            var activeSession = sessionResult.session!;

            var question = await _dbContext.Questions
                .AsNoTracking()
                .FirstOrDefaultAsync(q => q.id == questionId && q.quizId == activeSession.quizId);

            if (question is null)
            {
                return NotFound(new { message = "Vprašanje ne pripada aktivnemu kvizu." });
            }

            var answerEntity = await _dbContext.GameAnswers
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.gameSessionId == activeSession.id && a.questionId == questionId);

            return Ok(new
            {
                question = new QuestionSendDto(question.id, question.questionText),
                answerInfo = new
                {
                    alreadyAnswered = answerEntity is not null,
                    currentAnswer = answerEntity?.selectedAnswer.ToString(),
                    correctionCount = answerEntity?.changeCount ?? 0,
                    remainingCorrections = answerEntity is null
                        ? MaxAnswerCorrections
                        : Math.Max(0, MaxAnswerCorrections - answerEntity.changeCount)
                },
                session = new
                {
                    activeSession.id,
                    activeSession.quizId,
                    activeSession.expiresAtUtc,
                    remainingTimeSeconds = Math.Max(0, (int)(activeSession.expiresAtUtc - DateTime.UtcNow).TotalSeconds)
                }
            });
        }

        [HttpPut("question/{questionId:int}")]
        public async Task<IActionResult> UpdateQuestionAnswer([FromRoute] int questionId, [FromBody] UpdateQuestionAnswerDto request)
        {
            var authResult = await ValidateCurrentTokenAsync();
            if (!authResult.isValid)
            {
                return Unauthorized(new { message = authResult.errorMessage });
            }

            var sessionResult = await GetActiveSessionAsync(authResult.userId);
            if (!sessionResult.isValid)
            {
                return BadRequest(new { message = sessionResult.errorMessage });
            }

            var activeSession = sessionResult.session!;
            var answerEntity = await _dbContext.GameAnswers
                .FirstOrDefaultAsync(a => a.gameSessionId == activeSession.id && a.questionId == questionId);

            if (answerEntity is null)
            {
                return BadRequest(new { message = "To vprašanje še nima odgovora. Najprej uporabi /api/answer." });
            }

            var questionExists = await _dbContext.Questions
                .AnyAsync(q => q.id == questionId && q.quizId == activeSession.quizId);
            if (!questionExists)
            {
                return NotFound(new { message = "Vprašanje ne pripada aktivnemu kvizu." });
            }

            var selectedAnswer = char.ToUpperInvariant(request.answer[0]);
            var nowUtc = DateTime.UtcNow;
            if (answerEntity.selectedAnswer != selectedAnswer)
            {
                if (answerEntity.changeCount >= MaxAnswerCorrections)
                {
                    return BadRequest(new { message = $"Odgovor lahko popraviš največ {MaxAnswerCorrections}-krat." });
                }

                answerEntity.selectedAnswer = selectedAnswer;
                answerEntity.changeCount += 1;
                answerEntity.lastUpdatedAtUtc = nowUtc;
            }

            _dbContext.GameAnswerAttempts.Add(new GameAnswerAttempt
            {
                gameSessionId = activeSession.id,
                questionId = questionId,
                submittedAnswer = selectedAnswer,
                submittedAtUtc = nowUtc
            });

            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                message = "Odgovor posodobljen.",
                questionId,
                currentAnswer = answerEntity.selectedAnswer.ToString(),
                correctionCount = answerEntity.changeCount,
                remainingCorrections = Math.Max(0, MaxAnswerCorrections - answerEntity.changeCount)
            });
        }

        [HttpGet("game/progress/{quizId:int}")]
        public async Task<IActionResult> GetOthersProgress([FromRoute] int quizId)
        {
            var authResult = await ValidateCurrentTokenAsync();
            if (!authResult.isValid)
            {
                return Unauthorized(new { message = authResult.errorMessage });
            }

            var quizExists = await _dbContext.Quizzes.AnyAsync(q => q.id == quizId);
            if (!quizExists)
            {
                return NotFound(new { message = "Kviz ne obstaja." });
            }

            var progress = await GetOthersProgressAsync(quizId, authResult.userId);
            return Ok(progress);
        }

        [HttpGet("game/status")]
        public async Task<IActionResult> GetGameStatus()
        {
            var authResult = await ValidateCurrentTokenAsync();
            if (!authResult.isValid)
            {
                return Unauthorized(new { message = authResult.errorMessage });
            }

            var latestSession = await _dbContext.GameSessions
                .OrderByDescending(gs => gs.startedAtUtc)
                .FirstOrDefaultAsync(gs => gs.userId == authResult.userId);

            if (latestSession is null)
            {
                return Ok(new { hasSession = false, message = "Uporabnik še ni začel kviza." });
            }

            if (latestSession.status == GameSessionStatus.InProgress && latestSession.expiresAtUtc <= DateTime.UtcNow)
            {
                latestSession.status = GameSessionStatus.Completed;
                latestSession.completedAtUtc = DateTime.UtcNow;
                latestSession.completionReason = "TimeExpired";
                await _dbContext.SaveChangesAsync();
            }

            return Ok(new
            {
                hasSession = true,
                session = new
                {
                    latestSession.id,
                    latestSession.quizId,
                    status = latestSession.status.ToString(),
                    latestSession.startedAtUtc,
                    latestSession.expiresAtUtc,
                    latestSession.completedAtUtc,
                    latestSession.completionReason,
                    remainingTimeSeconds = latestSession.status == GameSessionStatus.Completed
                        ? 0
                        : Math.Max(0, (int)(latestSession.expiresAtUtc - DateTime.UtcNow).TotalSeconds)
                }
            });
        }

        private async Task<(bool isValid, int userId, string? errorMessage)> ValidateCurrentTokenAsync()
        {
            var subClaim = User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(subClaim, out var userId))
            {
                return (false, 0, "Neveljaven uporabniški identifikator v tokenu.");
            }

            var jwtId = User.FindFirstValue(JwtRegisteredClaimNames.Jti);
            if (string.IsNullOrWhiteSpace(jwtId))
            {
                return (false, 0, "Token nima identifikatorja (JTI).");
            }

            var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.id == userId);
            if (user is null)
            {
                return (false, 0, "Uporabnik ne obstaja.");
            }

            var isCurrentTokenValid =
                user.currentJwtId == jwtId &&
                user.currentJwtExpiresAtUtc.HasValue &&
                user.currentJwtExpiresAtUtc.Value > DateTime.UtcNow;

            if (!isCurrentTokenValid)
            {
                return (false, 0, "Token je razveljavljen zaradi nove prijave ali je potekel.");
            }

            return (true, userId, null);
        }

        private async Task<(bool isValid, GameSession? session, string? errorMessage)> GetActiveSessionAsync(int userId)
        {
            var nowUtc = DateTime.UtcNow;
            var activeSession = await _dbContext.GameSessions
                .OrderByDescending(gs => gs.startedAtUtc)
                .FirstOrDefaultAsync(gs => gs.userId == userId && gs.status == GameSessionStatus.InProgress);

            if (activeSession is null)
            {
                return (false, null, "Aktivna seja kviza ne obstaja. Pokliči /game/start/{quiz}.");
            }

            if (activeSession.expiresAtUtc <= nowUtc)
            {
                activeSession.status = GameSessionStatus.Completed;
                activeSession.completedAtUtc = nowUtc;
                activeSession.completionReason = "TimeExpired";
                await _dbContext.SaveChangesAsync();

                return (false, null, "Čas kviza je potekel. Kviz je zaključen.");
            }

            return (true, activeSession, null);
        }

        private async Task<object> GetOthersProgressAsync(int quizId, int currentUserId)
        {
            var totalQuestions = await _dbContext.Questions.CountAsync(q => q.quizId == quizId);

            var otherSessionIds = await _dbContext.GameSessions
                .Where(gs => gs.quizId == quizId && gs.status == GameSessionStatus.InProgress && gs.userId != currentUserId)
                .Select(gs => gs.id)
                .ToListAsync();

            var otherUsersCount = otherSessionIds.Count;
            var answeredByOthers = otherUsersCount == 0
                ? 0
                : await _dbContext.GameAnswers.CountAsync(a => otherSessionIds.Contains(a.gameSessionId));

            var denominator = otherUsersCount * totalQuestions;
            var answeredSharePercent = denominator == 0
                ? 0
                : Math.Round((double)answeredByOthers / denominator * 100, 2);

            return new
            {
                quizId,
                otherActivePlayers = otherUsersCount,
                answeredByOthers,
                totalQuestions,
                answeredSharePercent
            };
        }
    }
}
