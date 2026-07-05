using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Poskus1.Entities
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

        // Besedilno sporočilo (opcijsko)
        public string? message { get; set; }

        // Ali sporočilo vključuje statistiko uporabnika
        [Required]
        public bool includesStatistics { get; set; } = false;

        [Required]
        public DateTime sentAt { get; set; }
    }
}
