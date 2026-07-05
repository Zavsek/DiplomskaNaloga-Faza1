using System.ComponentModel.DataAnnotations;

namespace Poskus3.DTOs
{
    public class RegisterDto
    {
        [Required]
        [EmailAddress]
        public string email { get; set; } = string.Empty;

        [Required]
        [MinLength(6)]
        public string password { get; set; } = string.Empty;

        [Required]
        public DateTime dateOfBirth { get; set; }

        [Required]
        public string fullName { get; set; } = string.Empty;

        [Required]
        public string country { get; set; } = string.Empty;
    }
}
