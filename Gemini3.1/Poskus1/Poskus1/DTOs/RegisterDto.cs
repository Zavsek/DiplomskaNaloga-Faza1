using System;

namespace Poskus1.DTOs
{
    public class RegisterDto
    {
        public string email { get; set; } = string.Empty;
        public string password { get; set; } = string.Empty;
        public DateTime dateOfBirth { get; set; }
        public string fullName { get; set; } = string.Empty;
        public string country { get; set; } = string.Empty;
    }
}
