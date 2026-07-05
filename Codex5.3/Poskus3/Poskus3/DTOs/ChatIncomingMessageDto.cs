using System.Text.Json;

namespace Poskus3.DTOs
{
    public class ChatIncomingMessageDto
    {
        public string? message { get; set; }
        public JsonElement? addStatistics { get; set; }
    }
}
