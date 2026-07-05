using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Poskus1.Entities
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

        [Required]
        public bool isFinished { get; set; } = false;

        // Označuje ali je ta seja zadnji (trenutni) poskus tega kviza za tega uporabnika.
        // Ob novem zagonu istega kviza se ta vrednost na prejšnji seji postavi na false.
        [Required]
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
        public char selectedAnswer { get; set; }

        // koliko krat je bil odgovor popravljen (max 2)
        [Required]
        public int editCount { get; set; } = 0;

        [Required]
        public DateTime answeredAt { get; set; }
    }
}
