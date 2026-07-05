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
        public string email { get; set; }

        [Required]
        public string passwordHash { get; set; }

        [Required]
        public DateTime dateOfBirth { get; set; }

        [Required]
        public string fullName { get; set; }

        [Required]
        public string country { get; set; }

        public string? currentJwtId { get; set; }
    }
}
