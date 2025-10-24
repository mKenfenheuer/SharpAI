using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    public class ChatCompletionResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("object")]
        public CompletionObjectType? Object { get; set; }

        [JsonPropertyName("created")]
        public long Created { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("choices")]
        public List<ChatCompletionChoice>? Choices { get; set; }

        [JsonPropertyName("usage")]
        public TokenUsage? Usage { get; set; }
    }

    [JsonConverter(typeof(LowercaseEnumConverter<CompletionObjectType>))]
    public enum CompletionObjectType { ChatCompletion, ChatCompletionChunk }
}
