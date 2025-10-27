using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    public class ChatMessage
    {
        [JsonPropertyName("role")]
        public required ChatRole Role { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; } // string | array (text, image, audio, etc.)

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("tool_call_id")]
        public string? ToolCallId { get; set; }

        [JsonPropertyName("tool_calls")]
        public List<ToolCall>? ToolCalls { get; set; }
    }

    [JsonConverter(typeof(LowercaseEnumConverter<ChatRole>))]
    public enum ChatRole { System, User, Assistant, Tool }
}
