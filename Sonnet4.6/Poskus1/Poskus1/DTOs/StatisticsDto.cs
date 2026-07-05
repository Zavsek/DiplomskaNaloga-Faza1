namespace Poskus1.DTOs
{
    public class UserStatisticsDto
    {
        public int questionsAnswered { get; set; }
        public double correctProcentage { get; set; }
        public double avgAnwserTime { get; set; }
        public string mostCommonAnwser { get; set; } = string.Empty;
        public int longestStreak { get; set; }
        public double avgWastedTimeOnWrongAnswers { get; set; }
    }
}
