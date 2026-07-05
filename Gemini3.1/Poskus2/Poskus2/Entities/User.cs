using System;

namespace Poskus2.Entities
{
    public class User
    {
        public int Id { get; set; }
        public string Email { get; set; }
        public string PasswordHash { get; set; }
        public DateTime DateOfBirth { get; set; }
        public string FullName { get; set; }
        public string Country { get; set; }
        public string? ActiveTokenId { get; set; }
    }
}
