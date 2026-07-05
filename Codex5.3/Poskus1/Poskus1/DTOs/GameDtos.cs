using System.ComponentModel.DataAnnotations;

namespace Poskus1.DTOs
{
    public class AnswerRequestDto
    {
        [Required]
        public int questionId { get; set; }

        [Required]
        [RegularExpression("^[ABCDabcd]$", ErrorMessage = "Dovoljeni so samo odgovori A, B, C ali D.")]
        public string answer { get; set; } = string.Empty;
    }

    public class UpdateQuestionAnswerDto
    {
        [Required]
        [RegularExpression("^[ABCDabcd]$", ErrorMessage = "Dovoljeni so samo odgovori A, B, C ali D.")]
        public string answer { get; set; } = string.Empty;
    }
}
