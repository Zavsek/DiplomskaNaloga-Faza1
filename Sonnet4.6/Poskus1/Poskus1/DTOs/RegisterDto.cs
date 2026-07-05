using System.ComponentModel.DataAnnotations;

namespace Poskus1.DTOs
{
    public class RegisterDto
    {
        [Required]
        [EmailAddress]
        public string email { get; set; } = string.Empty;

        [Required]
        public string password { get; set; } = string.Empty;

        [Required]
        public DateOnly dateOfBirth { get; set; }

        [Required]
        public string fullName { get; set; } = string.Empty;

        [Required]
        public string country { get; set; } = string.Empty;
    }
}
