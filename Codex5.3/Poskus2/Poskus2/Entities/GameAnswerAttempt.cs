using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Poskus2.Entities
{
    [Table("game_answer_attempts")]
    public class GameAnswerAttempt
    {
        [Key]
        public int id { get; set; }

        [Required]
        public int gameSessionId { get; set; }

        [ForeignKey(nameof(gameSessionId))]
        public GameSession gameSession { get; set; } = null!;

        [Required]
        public int questionId { get; set; }

        [ForeignKey(nameof(questionId))]
        public Question question { get; set; } = null!;

        [Required]
        public char selectedAnswer { get; set; }

        [Required]
        public bool isCorrect { get; set; }

        [Required]
        public DateTimeOffset submittedAtUtc { get; set; }
    }
}
