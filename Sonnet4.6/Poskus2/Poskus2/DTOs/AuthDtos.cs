using System.ComponentModel.DataAnnotations;

namespace Poskus2.DTOs
{
    public class RegisterDto
    {
        [Required]
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

    public class LoginPayload
    {
        [Required]
        public string email { get; set; } = string.Empty;

        [Required]
        public string password { get; set; } = string.Empty;
    }

    public class LoginDto
    {
        [Required]
        public LoginPayload loginPayload { get; set; } = new();
    }

    public class TokenValidationInfo
    {
        public bool valid { get; set; }
        public string? jti { get; set; }
        public string? email { get; set; }
        public string? fullName { get; set; }
        public DateTime? issuedAt { get; set; }
        public DateTime? expiresAt { get; set; }
        public string? message { get; set; }
    }
}
