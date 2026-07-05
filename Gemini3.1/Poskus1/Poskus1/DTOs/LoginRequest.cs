namespace Poskus1.DTOs
{
    public class LoginPayload
    {
        public string email { get; set; } = string.Empty;
        public string password { get; set; } = string.Empty;
    }

    public class LoginRequest
    {
        public LoginPayload loginPayload { get; set; } = new();
    }
}
