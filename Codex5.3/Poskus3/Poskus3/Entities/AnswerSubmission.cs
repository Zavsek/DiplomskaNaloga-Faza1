using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Poskus3.Entities
{
    [Table("answer_submissions")]
    public class AnswerSubmission
    {
        [Key]
        public int id { get; set; }

        [Required]
        public int gameSessionId { get; set; }

        [Required]
        public int questionId { get; set; }

        [Required]
        public char selectedAnswer { get; set; }

        [Required]
        public DateTime submittedAtUtc { get; set; }

        [Required]
        public int responseTimeMs { get; set; }

        [ForeignKey(nameof(gameSessionId))]
        public GameSession gameSession { get; set; } = null!;

        [ForeignKey(nameof(questionId))]
        public Question question { get; set; } = null!;
    }
}
