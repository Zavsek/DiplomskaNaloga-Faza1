using System.ComponentModel.DataAnnotations;

namespace Poskus1.DTOs
{
    public class ChatSendDto
    {
        public string? message { get; set; }

        // Če true, bo strežnik izračunal statistiko pošiljatelja in jo vključil v broadcast
        public bool? addStatistics { get; set; }
    }

    public class ChatBroadcastDto
    {
        public int messageId { get; set; }
        public int userId { get; set; }
        public string senderName { get; set; } = string.Empty;
        public string? message { get; set; }
        public UserStatisticsDto? statistics { get; set; }
        public DateTime sentAt { get; set; }
    }
}
