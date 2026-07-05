namespace Poskus1.DTOs
{
    public class ChatMessageRequestDto
    {
        public string? message { get; set; }
        public bool? addStatistics { get; set; }
    }

    public class ChatMessageResponseDto
    {
        public int userId { get; set; }
        public string? message { get; set; }
        public UserStatisticsDto? statistics { get; set; }
        public DateTime timestamp { get; set; }
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
}
