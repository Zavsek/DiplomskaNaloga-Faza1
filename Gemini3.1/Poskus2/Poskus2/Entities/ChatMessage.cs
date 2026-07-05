using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Poskus2.Entities
{
    [Table("chat_messages")]
    public class ChatMessage
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        public string? MessageText { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; }

        public string? StatisticsJson { get; set; }

        [ForeignKey(nameof(UserId))]
        public User User { get; set; }
    }
}
