using Microsoft.EntityFrameworkCore;
using Poskus3.Data;
using Poskus3.Services;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Poskus3.Services
{
    public class GameProgressWebSocketHandler
    {
        private readonly GameSessionService _sessionService;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly JwtService _jwtService;

        public GameProgressWebSocketHandler(
            GameSessionService sessionService,
            IServiceScopeFactory scopeFactory,
            JwtService jwtService)
        {
            _sessionService = sessionService;
            _scopeFactory = scopeFactory;
            _jwtService = jwtService;
        }

        public async Task HandleAsync(HttpContext context)
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new { message = "Zahtevana je WebSocket povezava." });
                return;
            }

            // Autentikacija: token iz query stringa ali Authorization headerja
            var token = context.Request.Query["token"].FirstOrDefault()
                ?? ExtractBearerToken(context.Request.Headers.Authorization.FirstOrDefault());

            if (string.IsNullOrEmpty(token))
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { message = "Token ni posredovan." });
                return;
            }

            var principal = _jwtService.ValidateToken(token);
            if (principal == null)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { message = "Neveljaven token." });
                return;
            }

            var subClaim = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? principal.FindFirst("sub")?.Value;
            if (!int.TryParse(subClaim, out var userId))
            {
                context.Response.StatusCode = 401;
                return;
            }

            // Preveri JTI v bazi
            var decoded = _jwtService.DecodeToken(token);
            if (decoded == null)
            {
                context.Response.StatusCode = 401;
                return;
            }

            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var user = await db.Users.FindAsync(userId);
                if (user == null || user.currentTokenJti != decoded.Id)
                {
                    context.Response.StatusCode = 401;
                    await context.Response.WriteAsJsonAsync(new { message = "Token je bil razveljavljen." });
                    return;
                }
            }

            using var ws = await context.WebSockets.AcceptWebSocketAsync();

            var active = _sessionService.GetActiveSessionForUser(userId);
            if (active == null)
            {
                await SendJsonAsync(ws, new { message = "Nimaš aktivne seje.", finished = true });
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Ni aktivne seje.", CancellationToken.None);
                return;
            }

            int totalQuestions;
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                totalQuestions = await db.Questions.CountAsync(q => q.quizId == active.QuizId);
            }

            // Pošiljamo posodobitve vsako sekundo dokler:
            // - seja ni zaključena
            // - WebSocket ni zaprt
            while (ws.State == WebSocketState.Open)
            {
                var current = _sessionService.GetActiveSession(active.SessionId);

                if (current == null || current.IsExpired)
                {
                    // Seja je potekla
                    if (current != null && current.IsExpired)
                        await _sessionService.FinishSessionAsync(active.SessionId, expired: true);

                    await SendJsonAsync(ws, new
                    {
                        finished = true,
                        message = "Čas kviza je potekel.",
                        quizId = active.QuizId
                    });
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Seja zaključena.", CancellationToken.None);
                    return;
                }

                var progress = await _sessionService.GetQuizProgressAsync(active.QuizId, totalQuestions);
                var secondsLeft = Math.Max(0, (int)(current.ExpiresAt - DateTime.UtcNow).TotalSeconds);

                await SendJsonAsync(ws, new
                {
                    finished = false,
                    quizId = active.QuizId,
                    sessionId = active.SessionId,
                    secondsRemaining = secondsLeft,
                    activePlayers = progress.activePlayers,
                    answeredQuestions = progress.answeredQuestions,
                    totalQuestions,
                    progressPercent = progress.progressPercent
                });

                // Počakaj 1 sekundo ali na sporočilo odjemalca (ping/pong ali zaprtje)
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try
                {
                    var buffer = new byte[128];
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Odjemalec zaprl povezavo.", CancellationToken.None);
                        return;
                    }
                }
                catch (OperationCanceledException)
                {
                    // Timeout potekel — pošlji naslednjo posodobitev
                }
                catch (WebSocketException)
                {
                    return;
                }
            }
        }

        private static async Task SendJsonAsync(WebSocket ws, object payload)
        {
            if (ws.State != WebSocketState.Open) return;
            var json = JsonSerializer.Serialize(payload);
            var bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                CancellationToken.None);
        }

        private static string? ExtractBearerToken(string? header)
        {
            if (header != null && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return header["Bearer ".Length..].Trim();
            return null;
        }
    }
}
