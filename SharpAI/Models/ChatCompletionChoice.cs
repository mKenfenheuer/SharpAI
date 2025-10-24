using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    public class ChatCompletionChoice
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("message")]
        public ChatMessage? Message { get; set; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }

        [JsonPropertyName("logprobs")]
        public object? LogProbs { get; set; }
    }
}
