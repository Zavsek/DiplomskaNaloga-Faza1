using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Poskus2.Entities
{
    [Table("chat_messages")]
    public class ChatMessage
    {
        [Key]
        public int id { get; set; }

        [Required]
        public int userId { get; set; }

        [ForeignKey(nameof(userId))]
        public User user { get; set; } = null!;

        public string? message { get; set; }

        public bool includesStatistics { get; set; } = false;

        public DateTime sentAt { get; set; } = DateTime.UtcNow;
    }
}
