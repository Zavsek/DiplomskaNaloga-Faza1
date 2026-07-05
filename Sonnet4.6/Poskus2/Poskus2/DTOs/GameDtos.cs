using System.ComponentModel.DataAnnotations;

namespace Poskus2.DTOs
{
    public class StartGameResponseDto
    {
        public int gameSessionId { get; set; }
        public int quizId { get; set; }
        public string quizTitle { get; set; } = string.Empty;
        public DateTime endsAt { get; set; }
        public int totalQuestions { get; set; }
        public QuestionSendDto firstQuestion { get; set; } = null!;
        public string wsUrl { get; set; } = string.Empty;
    }

    public class AnswerSubmitDto
    {
        [Required]
        public int questionId { get; set; }

        [Required]
        public string answer { get; set; } = string.Empty;
    }

    public class AnswerResultDto
    {
        public bool accepted { get; set; }
        public string message { get; set; } = string.Empty;
        public int editCount { get; set; }
        public int answeredQuestions { get; set; }
        public int totalQuestions { get; set; }
    }

    public class QuestionDetailDto
    {
        public int questionId { get; set; }
        public string question { get; set; } = string.Empty;
        public int orderIndex { get; set; }
        public int totalQuestions { get; set; }
        public char? yourAnswer { get; set; }
        public int editCount { get; set; }
        public bool canEdit { get; set; }
    }

    public class WsProgressMessage
    {
        public string type { get; set; } = "progress";
        public int gameSessionId { get; set; }
        public int totalParticipants { get; set; }
        public int totalQuestions { get; set; }
        public double averageProgress { get; set; }
        public List<ParticipantProgress> participants { get; set; } = new();
    }

    public class ParticipantProgress
    {
        public int userId { get; set; }
        public string fullName { get; set; } = string.Empty;
        public int answeredQuestions { get; set; }
        public double progressPercent { get; set; }
    }

    public class WsFinishedMessage
    {
        public string type { get; set; } = "finished";
        public int gameSessionId { get; set; }
        public string message { get; set; } = "Čas kviza je potekel. Kviz je zaključen.";
        public DateTime finishedAt { get; set; }
    }
}
