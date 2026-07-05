using Poskus1.Data;
using Poskus1.DTOs;
using Poskus1.Entities;

namespace Poskus1.Services
{
    public class ChatService
    {
        private readonly AppDbContext _db;
        private readonly StatisticsService _statsService;
        private readonly ChatWebSocketManager _wsManager;

        public ChatService(AppDbContext db, StatisticsService statsService, ChatWebSocketManager wsManager)
        {
            _db = db;
            _statsService = statsService;
            _wsManager = wsManager;
        }

        public async Task<(bool success, string message)> SendMessageAsync(int userId, string senderName, ChatSendDto dto)
        {
            bool hasText = !string.IsNullOrWhiteSpace(dto.message);
            bool wantsStats = dto.addStatistics == true;

            if (!hasText && !wantsStats)
                return (false, "Sporočilo ne sme biti prazno. Pošljite besedilo ali zahtevajte statistiko.");

            var chatMessage = new ChatMessage
            {
                userId = userId,
                message = hasText ? dto.message!.Trim() : null,
                includesStatistics = wantsStats,
                sentAt = DateTime.UtcNow
            };

            _db.ChatMessages.Add(chatMessage);
            await _db.SaveChangesAsync();

            UserStatisticsDto? stats = null;
            if (wantsStats)
                stats = await _statsService.GetUserStatisticsAsync(userId);

            var broadcast = new ChatBroadcastDto
            {
                messageId = chatMessage.id,
                userId = userId,
                senderName = senderName,
                message = chatMessage.message,
                statistics = stats,
                sentAt = chatMessage.sentAt
            };

            await _wsManager.BroadcastAsync(broadcast);

            return (true, "Sporočilo poslano.");
        }
    }
}
