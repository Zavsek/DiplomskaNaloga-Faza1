using System.ComponentModel.DataAnnotations;

namespace Poskus1.DTOs
{
    public class AnswerSubmitDto
    {
        [Required]
        public int questionId { get; set; }

        [Required]
        [RegularExpression("^[ABCD]$", ErrorMessage = "Odgovor mora biti A, B, C ali D.")]
        public string answer { get; set; } = string.Empty;
    }

    public class GameStartResponseDto
    {
        public int sessionId { get; set; }
        public string quizTitle { get; set; } = string.Empty;
        public int totalQuestions { get; set; }
        public double remainingSeconds { get; set; }
        public QuestionSendDto firstQuestion { get; set; } = null!;
    }

    public class QuestionResponseDto
    {
        public QuestionSendDto question { get; set; } = null!;
        public string? yourAnswer { get; set; }
        public int editsRemaining { get; set; }
    }

    public class AnswerResultDto
    {
        public bool accepted { get; set; }
        public string message { get; set; } = string.Empty;
        public int editsRemaining { get; set; }
        public QuestionSendDto? nextQuestion { get; set; }
    }

    public class QuizProgressBroadcastDto
    {
        public int quizId { get; set; }
        public string quizTitle { get; set; } = string.Empty;
        public int totalQuestions { get; set; }
        public int activePlayers { get; set; }
        // delež vprašanj ki so jih aktivni udeleženci skupaj že odgovorili
        public double averageProgressPercent { get; set; }
    }
}
