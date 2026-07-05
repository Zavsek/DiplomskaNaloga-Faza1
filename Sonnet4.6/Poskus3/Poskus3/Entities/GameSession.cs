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

        [ForeignKey(nameof(userId))]
        public User user { get; set; } = null!;

        [Required]
        public int quizId { get; set; }

        [ForeignKey(nameof(quizId))]
        public Quiz quiz { get; set; } = null!;

        [Required]
        public DateTime startedAt { get; set; }

        public DateTime? finishedAt { get; set; }

        public bool isFinished { get; set; } = false;

        // true samo za zadnji poskus tega uporabnika na tem kvizu
        public bool isLatestAttempt { get; set; } = true;

        public ICollection<UserAnswer> answers { get; set; } = new List<UserAnswer>();
    }

    [Table("user_answers")]
    public class UserAnswer
    {
        [Key]
        public int id { get; set; }

        [Required]
        public int sessionId { get; set; }

        [ForeignKey(nameof(sessionId))]
        public GameSession session { get; set; } = null!;

        [Required]
        public int questionId { get; set; }

        [ForeignKey(nameof(questionId))]
        public Question question { get; set; } = null!;

        [Required]
        public char answer { get; set; }

        // koliko krat je bil odgovor spremenjen (max 2 spremembi)
        public int editCount { get; set; } = 0;

        // čas zadnje oddaje/spremembe odgovora
        public DateTime answeredAt { get; set; } = DateTime.UtcNow;
    }
}
