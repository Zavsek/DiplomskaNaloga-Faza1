using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Poskus3.Entities
{
    [Table("chat_messages")]
    public class ChatMessage
    {
        [Key]
        public int id { get; set; }

        public int userId { get; set; }
        [ForeignKey(nameof(userId))]
        public User user { get; set; }

        public string? message { get; set; }

        public string? statisticsJson { get; set; }

        public DateTime createdAt { get; set; } = DateTime.UtcNow;
    }
}