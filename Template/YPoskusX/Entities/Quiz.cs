using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


namespace YPoskusX.Entities
{
    [Table("quizzes")]
    public class Quiz
    {
        [Key]
        public int id { get; set; }

        [Required]
        public string title { get; set; }

        [Required]
        public TimeSpan duration { get; set; }

        public ICollection<Question> questions { get; set; } = new List<Question>();
    }

    [Table("questions")]
    public class Question
    {
        [Key]
        public int id { get; set; }

        [Required]
        public string questionText { get; set; }

        [Required]
        public char answer { get; set; } // A, B, C, D

        [Required]
        public int quizId { get; set; }

        [ForeignKey(nameof(quizId))]
        public Quiz quiz { get; set; }

        [Required]
        public int orderIndex { get; set; }
    }
}
