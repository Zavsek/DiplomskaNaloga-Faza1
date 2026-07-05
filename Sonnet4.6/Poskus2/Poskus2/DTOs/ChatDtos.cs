using System.ComponentModel.DataAnnotations;

namespace Poskus2.DTOs
{
    public class SendChatMessageDto
    {
        public string? message { get; set; }
        public bool? addStatistics { get; set; }
    }

    public class UserStatisticsDto
    {
        public int questionsAnswered { get; set; }
        public double correctProcentage { get; set; }
        public double avgAnwserTime { get; set; }
        public string mostCommonAnwser { get; set; } = string.Empty;
        public int longestStreak { get; set; }
        public double avgWastedTimeOnWrongAnswers { get; set; }
    }

    public class ChatMessageBroadcastDto
    {
        public string type { get; set; } = "chat";
        public int messageId { get; set; }
        public int userId { get; set; }
        public string fullName { get; set; } = string.Empty;
        public string? message { get; set; }
        public UserStatisticsDto? statistics { get; set; }
        public DateTime sentAt { get; set; }
    }
}
