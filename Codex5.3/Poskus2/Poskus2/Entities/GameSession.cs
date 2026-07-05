using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Poskus2.Entities
{
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
        [MaxLength(32)]
        public string status { get; set; } = GameSessionStatus.InProgress;

        [Required]
        public DateTimeOffset startedAtUtc { get; set; }

        [Required]
        public DateTimeOffset endsAtUtc { get; set; }

        public DateTimeOffset? completedAtUtc { get; set; }

        public ICollection<GameAnswer> answers { get; set; } = new List<GameAnswer>();
    }

    public static class GameSessionStatus
    {
        public const string InProgress = "InProgress";
        public const string Finished = "Finished";
    }
}
