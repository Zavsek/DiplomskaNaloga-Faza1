using System.ComponentModel.DataAnnotations;

namespace Poskus3.DTOs
{
    public class LoginPayload
    {
        [Required]
        [EmailAddress]
        public string email { get; set; } = string.Empty;

        [Required]
        public string password { get; set; } = string.Empty;
    }

    public class LoginDto
    {
        [Required]
        public LoginPayload loginPayload { get; set; } = new();
    }
}
