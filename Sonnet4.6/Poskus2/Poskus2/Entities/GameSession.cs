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
        public int quizId { get; set; }

        [ForeignKey(nameof(quizId))]
        public Quiz quiz { get; set; } = null!;

        [Required]
        public DateTime startedAt { get; set; }

        public DateTime? endsAt { get; set; }

        public bool isFinished { get; set; } = false;

        public ICollection<UserGameSession> participants { get; set; } = new List<UserGameSession>();
    }

    [Table("user_game_sessions")]
    public class UserGameSession
    {
        [Key]
        public int id { get; set; }

        [Required]
        public int gameSessionId { get; set; }

        [ForeignKey(nameof(gameSessionId))]
        public GameSession gameSession { get; set; } = null!;

        [Required]
        public int userId { get; set; }

        [ForeignKey(nameof(userId))]
        public User user { get; set; } = null!;

        public DateTime joinedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// NULL = legacy zapis (pred uvedbo polja), obravnava se kot true.
        /// false = prepisano z novejšim poskusom istega kviza.
        /// </summary>
        public bool? isLatest { get; set; } = true;

        public ICollection<UserAnswer> answers { get; set; } = new List<UserAnswer>();
    }

    [Table("user_answers")]
    public class UserAnswer
    {
        [Key]
        public int id { get; set; }

        [Required]
        public int userGameSessionId { get; set; }

        [ForeignKey(nameof(userGameSessionId))]
        public UserGameSession userGameSession { get; set; } = null!;

        [Required]
        public int questionId { get; set; }

        [ForeignKey(nameof(questionId))]
        public Question question { get; set; } = null!;

        [Required]
        public char answer { get; set; }

        public int editCount { get; set; } = 0;

        /// <summary>
        /// Čas prvega oddanega odgovora na to vprašanje (ne popravka).
        /// NULL = legacy zapis pred uvedbo polja.
        /// </summary>
        public DateTime? firstAnsweredAt { get; set; }

        public DateTime answeredAt { get; set; } = DateTime.UtcNow;
    }
}
