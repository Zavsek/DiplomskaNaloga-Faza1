using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Poskus3.Entities
{
    [Table("game_sessions")]
    public class GameSession
    {
        [Key]
        public int id { get; set; }

        [Required]
        public int userId { get; set; }

        [Required]
        public int quizId { get; set; }

        [Required]
        public DateTime startedAtUtc { get; set; }

        public DateTime? lastInteractionAtUtc { get; set; }

        [Required]
        public DateTime expiresAtUtc { get; set; }

        public DateTime? completedAtUtc { get; set; }

        [MaxLength(64)]
        public string? completionReason { get; set; }

        [ForeignKey(nameof(userId))]
        public User user { get; set; } = null!;

        [ForeignKey(nameof(quizId))]
        public Quiz quiz { get; set; } = null!;

        public ICollection<GameSessionAnswer> answers { get; set; } = new List<GameSessionAnswer>();
        public ICollection<AnswerSubmission> submissions { get; set; } = new List<AnswerSubmission>();
    }
}
