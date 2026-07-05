using System.ComponentModel.DataAnnotations;

namespace Poskus1.DTOs
{
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
        public LoginPayloadDto loginPayload { get; set; } = new();
    }
}
