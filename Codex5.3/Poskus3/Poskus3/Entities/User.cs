using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Poskus3.Entities
{
    [Table("users")]
    public class User
    {
        [Key]
        public int id { get; set; }

        [Required]
        [MaxLength(255)]
        public string email { get; set; } = string.Empty;

        [Required]
        public string passwordHash { get; set; } = string.Empty;

        [Required]
        public string passwordSalt { get; set; } = string.Empty;

        [Required]
        public DateOnly dateOfBirth { get; set; }

        [Required]
        [MaxLength(255)]
        public string fullName { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string country { get; set; } = string.Empty;

        [MaxLength(128)]
        public string? currentTokenJti { get; set; }

        public DateTime? currentTokenExpiresAtUtc { get; set; }
    }
}
