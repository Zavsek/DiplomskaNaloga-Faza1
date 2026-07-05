using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Poskus2.Entities
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
        public DateTime EndTime { get; set; }

        public DateTime LastInteractionTime { get; set; }

        [Required]
        public bool IsFinished { get; set; }

        [ForeignKey(nameof(UserId))]
        public User User { get; set; }

        [ForeignKey(nameof(QuizId))]
        public Quiz Quiz { get; set; }

        public ICollection<QuizSessionAnswer> Answers { get; set; } = new List<QuizSessionAnswer>();
    }

    [Table("quiz_session_answers")]
    public class QuizSessionAnswer
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int QuizSessionId { get; set; }

        [Required]
        public int QuestionId { get; set; }

        [Required]
        public char SelectedAnswer { get; set; }

        [Required]
        public int EditCount { get; set; }

        public double TimeSpentMs { get; set; }

        public DateTime UpdatedAt { get; set; }

        [ForeignKey(nameof(QuizSessionId))]
        public QuizSession Session { get; set; }

        [ForeignKey(nameof(QuestionId))]
        public Question Question { get; set; }
    }
}
