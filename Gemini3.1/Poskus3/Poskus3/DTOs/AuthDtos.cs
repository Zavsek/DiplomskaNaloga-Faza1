using System.ComponentModel.DataAnnotations;

namespace Poskus3.DTOs
{
    public class RegisterDto
    {
        [Required]
        [EmailAddress]
        public string email { get; set; }

        [Required]
        [MinLength(6)]
        public string password { get; set; }

        [Required]
        public DateTime dateOfBirth { get; set; }

        [Required]
        public string fullName { get; set; }

        [Required]
        public string country { get; set; }
    }

    public class LoginRequest
    {
        [Required]
        public LoginPayloadDto loginPayload { get; set; }
    }

    public class LoginPayloadDto
    {
        [Required]
        public string email { get; set; }

        [Required]
        public string password { get; set; }
    }
}