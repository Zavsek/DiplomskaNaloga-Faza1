using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Poskus3.Entities
{
    [Table("quiz_sessions")]
    public class QuizSession
    {
        [Key]
        public int id { get; set; }

        public int userId { get; set; }

        [ForeignKey(nameof(userId))]
        public User user { get; set; }

        public int quizId { get; set; }

        [ForeignKey(nameof(quizId))]
        public Quiz quiz { get; set; }

        public DateTime startTime { get; set; }

        public DateTime endTime { get; set; }

        public DateTime lastActionTime { get; set; }

        public bool isFinished { get; set; }

        public ICollection<QuizSessionAnswer> answers { get; set; } = new List<QuizSessionAnswer>();
    }

    [Table("quiz_session_answers")]
    public class QuizSessionAnswer
    {
        [Key]
        public int id { get; set; }

        public int sessionId { get; set; }

        [ForeignKey(nameof(sessionId))]
        public QuizSession session { get; set; }

        public int questionId { get; set; }

        [ForeignKey(nameof(questionId))]
        public Question question { get; set; }

        public char submittedAnswer { get; set; }

        public int correctionCount { get; set; } = 0;

        public DateTime updatedAt { get; set; } = DateTime.UtcNow;

        public TimeSpan timeSpent { get; set; } = TimeSpan.Zero;
    }
}