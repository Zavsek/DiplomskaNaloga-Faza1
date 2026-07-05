namespace Poskus2.DTOs
{
    public class LoginPayload
    {
        public string email { get; set; }
        public string password { get; set; }
    }

    public class LoginRequest
    {
        public LoginPayload loginPayload { get; set; }
    }
}
