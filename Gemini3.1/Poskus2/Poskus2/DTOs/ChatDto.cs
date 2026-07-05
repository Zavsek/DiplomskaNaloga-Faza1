namespace Poskus2.DTOs
{
    public class ChatReceiveDto
    {
        public string? message { get; set; }
        public bool? addStatistics { get; set; }
    }

    public class UserStatisticsDto
    {
        public int questionsAnswered { get; set; }
        public double correctProcentage { get; set; }
        public double avgAnwserTime { get; set; }
        public char? mostCommonAnwser { get; set; }
        public int longestStreak { get; set; }
        public double avgWastedTimeOnWrongAnswers { get; set; }
    }

    public class ChatMessageBroadcastDto
    {
        public int userId { get; set; }
        public string fullName { get; set; }
        public string? message { get; set; }
        public UserStatisticsDto? statistics { get; set; }
        public DateTime timestamp { get; set; }
    }
}