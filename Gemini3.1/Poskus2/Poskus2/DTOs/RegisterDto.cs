using System;

namespace Poskus2.DTOs
{
    public class RegisterDto
    {
        public string email { get; set; }
        public string password { get; set; }
        public DateTime dateOfBirth { get; set; }
        public string fullName { get; set; }
        public string country { get; set; }
    }
}
