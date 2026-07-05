using System.Text.Json;

namespace Poskus1.DTOs
{
    public class ChatMessageRequestDto
    {
        public string? message { get; set; }
        public JsonElement? addStatistics { get; set; }
    }
}
