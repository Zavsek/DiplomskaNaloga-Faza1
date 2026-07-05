using System;

namespace Poskus1.Entities
{
    public class User
    {
        public int Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public DateTime DateOfBirth { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string? ActiveTokenId { get; set; }
    }
}
