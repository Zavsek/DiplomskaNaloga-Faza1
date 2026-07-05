using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Poskus3.Entities
{
    [Table("game_session_answers")]
    public class GameSessionAnswer
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
        public int correctionCount { get; set; }

        [Required]
        public DateTime answeredAtUtc { get; set; }

        [ForeignKey(nameof(gameSessionId))]
        public GameSession gameSession { get; set; } = null!;

        [ForeignKey(nameof(questionId))]
        public Question question { get; set; } = null!;
    }
}
