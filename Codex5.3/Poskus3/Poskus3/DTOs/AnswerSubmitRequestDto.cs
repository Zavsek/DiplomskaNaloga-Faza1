using System.ComponentModel.DataAnnotations;

namespace Poskus3.DTOs
{
    public class AnswerSubmitRequestDto
    {
        [Required]
        public int questionId { get; set; }

        [Required]
        [RegularExpression("^[A-D]$")]
        public string answer { get; set; } = string.Empty;
    }
}
