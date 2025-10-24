using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    public class ChatCompletionChunkChoice
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("delta")]
        public ChatMessage? Delta { get; set; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }
}
