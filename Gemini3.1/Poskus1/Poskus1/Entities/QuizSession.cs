using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Poskus1.Entities
{
    [Table("quiz_sessions")]
    public class QuizSession
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required]
        public int QuizId { get; set; }

        [Required]
        public DateTime StartTime { get; set; }

        [Required]
        public DateTime ExpiresAt { get; set; }

        public bool IsCompleted { get; set; }

        [ForeignKey(nameof(UserId))]
        public User User { get; set; }

        [ForeignKey(nameof(QuizId))]
        public Quiz Quiz { get; set; }

        public ICollection<QuizAnswer> Answers { get; set; } = new List<QuizAnswer>();
    }

    [Table("quiz_answers")]
    public class QuizAnswer
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int QuizSessionId { get; set; }

        [Required]
        public int QuestionId { get; set; }

        [Required]
        public char SubmittedAnswer { get; set; }

        public int ChangeCount { get; set; }

        public DateTime AnsweredAt { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(QuizSessionId))]
        public QuizSession QuizSession { get; set; }

        [ForeignKey(nameof(QuestionId))]
        public Question Question { get; set; }
    }
}
