namespace Poskus2.DTOs.Auth
{
    public class LoginRequestDto
    {
        public LoginPayloadDto? loginPayload { get; set; }
    }

    public class LoginPayloadDto
    {
        public string email { get; set; } = string.Empty;
        public string password { get; set; } = string.Empty;
    }
}
