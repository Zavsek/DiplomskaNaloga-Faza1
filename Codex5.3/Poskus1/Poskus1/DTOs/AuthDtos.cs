using System.ComponentModel.DataAnnotations;

namespace Poskus1.DTOs
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
        public DateTime dateOfBirth { get; set; }

        [Required]
        public string fullName { get; set; } = string.Empty;

        [Required]
        public string country { get; set; } = string.Empty;
    }

    public class LoginPayloadDto
    {
        [Required]
        [EmailAddress]
        public string email { get; set; } = string.Empty;

        [Required]
        public string password { get; set; } = string.Empty;
    }

    public class LoginRequestDto
    {
        [Required]
        public LoginPayloadDto? loginPayload { get; set; }
    }
}
