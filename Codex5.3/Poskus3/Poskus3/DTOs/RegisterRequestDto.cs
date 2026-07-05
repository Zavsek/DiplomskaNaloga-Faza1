using System.ComponentModel.DataAnnotations;

namespace Poskus3.DTOs
{
    public class RegisterRequestDto
    {
        [Required]
        [EmailAddress]
        public string email { get; set; } = string.Empty;

        [Required]
        [MinLength(8)]
        public string password { get; set; } = string.Empty;

        [Required]
        public DateOnly dateOfBirth { get; set; }

        [Required]
        [MinLength(2)]
        public string fullName { get; set; } = string.Empty;

        [Required]
        [MinLength(2)]
        public string country { get; set; } = string.Empty;
    }
}
