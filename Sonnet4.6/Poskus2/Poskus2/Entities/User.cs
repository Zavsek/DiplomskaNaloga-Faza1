using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Poskus2.Entities
{
    [Table("users")]
    public class User
    {
        [Key]
        public int id { get; set; }

        [Required]
        public string email { get; set; } = string.Empty;

        [Required]
        public string passwordHash { get; set; } = string.Empty;

        [Required]
        public string fullName { get; set; } = string.Empty;

        [Required]
        public DateOnly dateOfBirth { get; set; }

        [Required]
        public string country { get; set; } = string.Empty;

        public string? activeTokenJti { get; set; }

        public DateTime? tokenIssuedAt { get; set; }
    }
}
