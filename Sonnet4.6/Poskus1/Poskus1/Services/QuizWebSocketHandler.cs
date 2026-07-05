using Microsoft.EntityFrameworkCore;
using Poskus1.Data;
using Poskus1.DTOs;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Poskus1.Services
{
    // Obdeluje WebSocket povezave na /ws/quiz-progress?quizId={id}
    public static class QuizWebSocketHandler
    {
        public static async Task HandleAsync(
            HttpContext context,
            WebSocket ws,
            QuizProgressTracker tracker,
            AppDbContext db)
        {
            if (!context.Request.Query.TryGetValue("quizId", out var quizIdStr)
                || !int.TryParse(quizIdStr, out var quizId))
            {
                await CloseWithError(ws, "Manjka parameter quizId.");
                return;
            }

            var quiz = await db.Quizzes
                .Include(q => q.questions)
                .FirstOrDefaultAsync(q => q.id == quizId);

            if (quiz == null)
            {
                await CloseWithError(ws, "Kviz ne obstaja.");
                return;
            }

            tracker.AddWebSocketClient(quizId, ws);

            // Pošlji takoj trenutni napredek ob povezavi
            var currentProgress = tracker.GetProgress(quizId, quiz.title, quiz.questions.Count);
            if (currentProgress != null)
            {
                var initialPayload = JsonSerializer.Serialize(new { type = "progress", data = currentProgress });
                var initialBytes = Encoding.UTF8.GetBytes(initialPayload);
                await ws.SendAsync(
                    new ArraySegment<byte>(initialBytes),
                    WebSocketMessageType.Text, true,
                    CancellationToken.None);
            }
            else
            {
                // Ni aktivnih udeležencev
                var emptyPayload = JsonSerializer.Serialize(new
                {
                    type = "progress",
                    data = new QuizProgressBroadcastDto
                    {
                        quizId = quizId,
                        quizTitle = quiz.title,
                        totalQuestions = quiz.questions.Count,
                        activePlayers = 0,
                        averageProgressPercent = 0
                    }
                });
                var emptyBytes = Encoding.UTF8.GetBytes(emptyPayload);
                await ws.SendAsync(
                    new ArraySegment<byte>(emptyBytes),
                    WebSocketMessageType.Text, true,
                    CancellationToken.None);
            }

            // Drži povezavo odprto (klient pošilja ping ali čaka na broadcast)
            var buffer = new byte[256];
            while (ws.State == WebSocketState.Open)
            {
                try
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                        break;
                    // ignoriramo klientova sporočila — ta socket je samo read
                }
                catch
                {
                    break;
                }
            }

            if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
            {
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Goodbye", CancellationToken.None); }
                catch { /* ignore */ }
            }
        }

        private static async Task CloseWithError(WebSocket ws, string reason)
        {
            var msg = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { error = reason }));
            if (ws.State == WebSocketState.Open)
            {
                await ws.SendAsync(new ArraySegment<byte>(msg), WebSocketMessageType.Text, true, CancellationToken.None);
                await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, reason, CancellationToken.None);
            }
        }
    }
}
