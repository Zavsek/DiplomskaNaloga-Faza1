using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Poskus2.Entities
{
    [Table("users")]
    public class AppUser
    {
        [Key]
        public int id { get; set; }

        [Required]
        [MaxLength(320)]
        public string email { get; set; } = string.Empty;

        [Required]
        public byte[] passwordHash { get; set; } = Array.Empty<byte>();

        [Required]
        public byte[] passwordSalt { get; set; } = Array.Empty<byte>();

        [Required]
        public DateOnly dateOfBirth { get; set; }

        [Required]
        [MaxLength(200)]
        public string fullName { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string country { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? currentTokenJti { get; set; }

        public DateTimeOffset? currentTokenExpiresAtUtc { get; set; }

        [Required]
        public DateTimeOffset createdAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }
}
