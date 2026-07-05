namespace Poskus2.DTOs.Statistics
{
    public class UserStatisticsDto
    {
        public int questionsAnswered { get; set; }
        public double correctProcentage { get; set; }
        public double avgAnwserTime { get; set; }
        public string? mostCommonAnwser { get; set; }
        public int longestStreak { get; set; }
        public double avgWastedTimeOnWrongAnswers { get; set; }
    }
}
