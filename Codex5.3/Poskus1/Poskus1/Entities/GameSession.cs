using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Poskus1.Entities
{
    public enum GameSessionStatus
    {
        InProgress = 1,
        Completed = 2
    }

    [Table("game_sessions")]
    public class GameSession
    {
        [Key]
        public int id { get; set; }

        [Required]
        public int userId { get; set; }

        [ForeignKey(nameof(userId))]
        public AppUser user { get; set; } = null!;

        [Required]
        public int quizId { get; set; }

        [ForeignKey(nameof(quizId))]
        public Quiz quiz { get; set; } = null!;

        [Required]
        public DateTime startedAtUtc { get; set; }

        [Required]
        public DateTime expiresAtUtc { get; set; }

        public DateTime? completedAtUtc { get; set; }

        [Required]
        public GameSessionStatus status { get; set; } = GameSessionStatus.InProgress;

        [MaxLength(120)]
        public string? completionReason { get; set; }

        public ICollection<GameAnswer> answers { get; set; } = new List<GameAnswer>();
    }
}
