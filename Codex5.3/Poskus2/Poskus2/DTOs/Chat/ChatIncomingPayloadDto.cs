using System.Text.Json;

namespace Poskus2.DTOs.Chat
{
    public class ChatIncomingPayloadDto
    {
        public string? message { get; set; }
        public JsonElement? addStatistics { get; set; }
    }
}
