using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Poskus1.Entities
{
    [Table("users")]
    [Index(nameof(email), IsUnique = true)]
    public class AppUser
    {
        [Key]
        public int id { get; set; }

        [Required]
        [MaxLength(256)]
        public string email { get; set; } = string.Empty;

        [Required]
        [MaxLength(256)]
        public string passwordHash { get; set; } = string.Empty;

        [Required]
        [MaxLength(256)]
        public string passwordSalt { get; set; } = string.Empty;

        [Required]
        public DateTime dateOfBirth { get; set; }

        [Required]
        [MaxLength(200)]
        public string fullName { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string country { get; set; } = string.Empty;

        [MaxLength(64)]
        public string? currentJwtId { get; set; }

        public DateTime? currentJwtExpiresAtUtc { get; set; }
    }
}
