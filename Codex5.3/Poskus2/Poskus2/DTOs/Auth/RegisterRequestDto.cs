namespace Poskus2.DTOs.Auth
{
    public class RegisterRequestDto
    {
        public string email { get; set; } = string.Empty;
        public string password { get; set; } = string.Empty;
        public DateOnly dateOfBirth { get; set; }
        public string fullName { get; set; } = string.Empty;
        public string country { get; set; } = string.Empty;
    }
}
