using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Poskus3.Data;
using Poskus3.DTOs;
using Poskus3.Entities;
using Poskus3.Services;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Poskus3.Controllers
{
    [ApiController]
    public class WebSocketController : ControllerBase
    {
        private readonly GameWebSocketManager _wsManager;
        private readonly ChatWebSocketManager _chatWsManager;
        private readonly IServiceScopeFactory _scopeFactory;

        public WebSocketController(GameWebSocketManager wsManager, ChatWebSocketManager chatWsManager, IServiceScopeFactory scopeFactory)
        {
            _wsManager = wsManager;
            _chatWsManager = chatWsManager;
            _scopeFactory = scopeFactory;
        }

        [Authorize]
        [Route("/ws")]
        [HttpGet]
        public async Task Get()
        {
            if (HttpContext.WebSockets.IsWebSocketRequest)
            {
                using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();

                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (int.TryParse(userIdStr, out var userId))
                {
                    _wsManager.AddSocket(userId, webSocket);

                    var buffer = new byte[1024 * 4];
                    var receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    while (!receiveResult.CloseStatus.HasValue)
                    {
                        receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    }

                    await _wsManager.RemoveSocketAsync(userId, webSocket);
                }
                else
                {
                    HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                }
            }
            else
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            }
        }

        [Authorize]
        [Route("/chat")]
        [HttpGet]
        public async Task Chat()
        {
            if (HttpContext.WebSockets.IsWebSocketRequest)
            {
                using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();

                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (int.TryParse(userIdStr, out var userId))
                {
                    _chatWsManager.AddSocket(webSocket, userId);

                    var buffer = new byte[1024 * 8];
                    try
                    {
                        var receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                        while (!receiveResult.CloseStatus.HasValue)
                        {
                            var text = Encoding.UTF8.GetString(buffer, 0, receiveResult.Count);
                            
                            ChatPayloadDto payload = null;
                            try { payload = JsonSerializer.Deserialize<ChatPayloadDto>(text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); } catch { }

                            bool hasMessage = !string.IsNullOrWhiteSpace(payload?.message);
                            bool addStats = payload?.addStatistics == true;

                            if (hasMessage || addStats)
                            {
                                using var scope = _scopeFactory.CreateScope();
                                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                                var user = await dbContext.Users.FindAsync(userId);
                                UserStatisticsDto stats = null;
                                if (addStats)
                                {
                                    stats = await CalculateStats(userId, dbContext);
                                }

                                var chatMsg = new ChatMessage
                                {
                                    userId = userId,
                                    message = payload?.message,
                                    statisticsJson = stats != null ? JsonSerializer.Serialize(stats) : null
                                };
                                dbContext.ChatMessages.Add(chatMsg);
                                await dbContext.SaveChangesAsync();

                                var broadcastPayload = new
                                {
                                    type = "ChatMessage",
                                    userId = userId,
                                    fullName = user?.fullName,
                                    message = payload?.message,
                                    statistics = stats,
                                    timestamp = chatMsg.createdAt
                                };

                                await _chatWsManager.BroadcastAsync(broadcastPayload);
                            }

                            receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        }
                    }
                    catch
                    {
                        // Ignore dropped
                    }
                    finally
                    {
                        await _chatWsManager.RemoveSocketAsync(webSocket);
                    }
                }
                else
                {
                    HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
                }
            }
            else
            {
                HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            }
        }

        private async Task<UserStatisticsDto> CalculateStats(int userId, AppDbContext dbContext)
        {
            var latestSessions = await dbContext.QuizSessions
                .Where(s => s.userId == userId)
                .GroupBy(s => s.quizId)
                .Select(g => g.OrderByDescending(s => s.startTime).FirstOrDefault())
                .ToListAsync();

            var sessionIds = latestSessions.Where(s => s != null).Select(s => s.id).ToList();

            var answers = await dbContext.QuizSessionAnswers
                .Include(a => a.question)
                .Where(a => sessionIds.Contains(a.sessionId))
                .OrderBy(a => a.updatedAt)
                .ToListAsync();

            if (!answers.Any()) return new UserStatisticsDto();

            int questionsAnswered = answers.Count;
            int correctAnswers = answers.Count(a => a.submittedAnswer == a.question.answer);
            double correctProcentage = questionsAnswered > 0 ? Math.Round((double)correctAnswers / questionsAnswered * 100, 2) : 0;

            double avgAnwserTime = answers.Average(a => a.timeSpent.TotalSeconds);

            var mostCommonAnwser = answers.GroupBy(a => a.submittedAnswer)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key.ToString())
                .FirstOrDefault() ?? "";

            int longestStreak = 0;
            int currentStreak = 0;
            foreach (var a in answers)
            {
                if (a.submittedAnswer == a.question.answer)
                {
                    currentStreak++;
                    if (currentStreak > longestStreak) longestStreak = currentStreak;
                }
                else
                {
                    currentStreak = 0;
                }
            }

            var wrongAnswers = answers.Where(a => a.submittedAnswer != a.question.answer).ToList();
            double avgWastedTimeOnWrongAnswers = wrongAnswers.Any()
                ? wrongAnswers.Average(a => a.timeSpent.TotalSeconds)
                : 0;

            return new UserStatisticsDto
            {
                questionsAnswered = questionsAnswered,
                correctProcentage = correctProcentage,
                avgAnwserTime = Math.Round(avgAnwserTime, 2),
                mostCommonAnwser = mostCommonAnwser,
                longestStreak = longestStreak,
                avgWastedTimeOnWrongAnswers = Math.Round(avgWastedTimeOnWrongAnswers, 2)
            };
        }
    }
}