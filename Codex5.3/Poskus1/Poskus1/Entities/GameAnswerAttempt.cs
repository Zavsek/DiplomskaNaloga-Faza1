using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Poskus1.Entities
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
        public char submittedAnswer { get; set; }

        [Required]
        public DateTime submittedAtUtc { get; set; }
    }
}
