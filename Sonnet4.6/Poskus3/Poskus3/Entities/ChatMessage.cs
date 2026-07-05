using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Poskus3.Entities
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

        // Null če sporočilo nima besedila
        public string? message { get; set; }

        // True če sporočilo vsebuje statistiko (shranjeno ob pošiljanju)
        public bool hasStatistics { get; set; } = false;

        [Required]
        public DateTime sentAt { get; set; } = DateTime.UtcNow;
    }
}
