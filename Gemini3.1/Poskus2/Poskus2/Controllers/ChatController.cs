using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Poskus2.Data;
using Poskus2.DTOs;
using Poskus2.Entities;
using Poskus2.Services;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace Poskus2.Controllers
{
    [ApiController]
    public class ChatController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly QuizWebSocketManager _wsManager;
        private readonly IConfiguration _config;

        public ChatController(AppDbContext context, QuizWebSocketManager wsManager, IConfiguration config)
        {
            _context = context;
            _wsManager = wsManager;
            _config = config;
        }

        [AllowAnonymous]
        [HttpGet("/chat")]
        public async Task ConnectGlobalChat([FromQuery] string token)
        {
            if (HttpContext.WebSockets.IsWebSocketRequest)
            {
                if (string.IsNullOrEmpty(token))
                {
                    HttpContext.Response.StatusCode = 401;
                    return;
                }

                var tokenHandler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                var jwtSection = _config.GetSection("Jwt");
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

                    var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
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
                        _wsManager.AddChatSocket(userId, socket);

                        var buffer = new byte[1024 * 16];
                        while (socket.State == WebSocketState.Open)
                        {
                            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by client", CancellationToken.None);
                            }
                            else if (result.MessageType == WebSocketMessageType.Text)
                            {
                                var messageText = Encoding.UTF8.GetString(buffer, 0, result.Count);
                                await HandleIncomingChatMessage(userIdStr, user.FullName, messageText);
                            }
                        }

                        _wsManager.RemoveChatSocket(userId);
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

        private async Task HandleIncomingChatMessage(string userIdStr, string fullName, string messageText)
        {
            try
            {
                var receiveDto = JsonSerializer.Deserialize<ChatReceiveDto>(messageText);
                
                bool hasMessage = !string.IsNullOrWhiteSpace(receiveDto?.message);
                bool hasStats = receiveDto?.addStatistics == true;

                if (!hasMessage && !hasStats)
                {
                    return; // Message cannot be completely empty
                }

                var broadcastDto = new ChatMessageBroadcastDto
                {
                    userId = int.Parse(userIdStr),
                    fullName = fullName,
                    message = hasMessage ? receiveDto.message : null,
                    timestamp = DateTime.UtcNow
                };

                if (hasStats)
                {
                    broadcastDto.statistics = await CalculateUserStatistics(broadcastDto.userId);
                }

                using var scope = HttpContext.RequestServices.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                
                var chatMessage = new ChatMessage
                {
                    UserId = broadcastDto.userId,
                    MessageText = broadcastDto.message,
                    CreatedAt = broadcastDto.timestamp,
                    StatisticsJson = hasStats ? JsonSerializer.Serialize(broadcastDto.statistics) : null
                };
                
                dbContext.ChatMessages.Add(chatMessage);
                await dbContext.SaveChangesAsync();

                await _wsManager.BroadcastChatMessageAsync(broadcastDto);
            }
            catch
            {
                // Invalid JSON or other error, ignore
            }
        }

        private async Task<UserStatisticsDto> CalculateUserStatistics(int userId)
        {
            using var scope = HttpContext.RequestServices.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Find the latest session for each quiz for the user
            var latestSessionIds = await dbContext.QuizSessions
                .Where(s => s.UserId == userId)
                .GroupBy(s => s.QuizId)
                .Select(g => g.OrderByDescending(x => x.StartTime).Select(x => x.Id).FirstOrDefault())
                .ToListAsync();

            if (!latestSessionIds.Any())
            {
                return new UserStatisticsDto(); // All zeros
            }

            var answers = await dbContext.QuizSessionAnswers
                .Include(a => a.Question)
                .Where(a => latestSessionIds.Contains(a.QuizSessionId))
                .OrderBy(a => a.UpdatedAt)
                .ToListAsync();

            var stats = new UserStatisticsDto
            {
                questionsAnswered = answers.Count
            };

            if (stats.questionsAnswered > 0)
            {
                int correctAnswers = answers.Count(a => a.SelectedAnswer == a.Question.answer);
                stats.correctProcentage = (double)correctAnswers / stats.questionsAnswered * 100;
                
                stats.avgAnwserTime = answers.Average(a => a.TimeSpentMs);

                stats.mostCommonAnwser = answers
                    .GroupBy(a => a.SelectedAnswer)
                    .OrderByDescending(g => g.Count())
                    .First().Key;

                // Longest Streak
                int longestStreak = 0;
                int currentStreak = 0;
                foreach (var a in answers)
                {
                    if (a.SelectedAnswer == a.Question.answer)
                    {
                        currentStreak++;
                        if (currentStreak > longestStreak) longestStreak = currentStreak;
                    }
                    else
                    {
                        currentStreak = 0;
                    }
                }
                stats.longestStreak = longestStreak;

                var wrongAnswers = answers.Where(a => a.SelectedAnswer != a.Question.answer).ToList();
                stats.avgWastedTimeOnWrongAnswers = wrongAnswers.Any() 
                    ? wrongAnswers.Average(a => a.TimeSpentMs) 
                    : 0;
            }

            return stats;
        }
    }
}