using System.ComponentModel.DataAnnotations;

namespace Poskus3.DTOs
{
    public class AnswerSubmitDto
    {
        [Required]
        public int QuestionId { get; set; }

        [Required]
        [RegularExpression("^[ABCD]$", ErrorMessage = "Answer must be A, B, C, or D.")]
        public char Answer { get; set; }
    }
}