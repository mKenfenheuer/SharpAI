using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    public class ChatCompletionChunk
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
        public List<ChatCompletionChunkChoice>? Choices { get; set; }

        [JsonPropertyName("system_fingerprint")]
        public string? SystemFingerprint { get; set; }

        [JsonPropertyName("service_tier")]
        public ServiceTier? ServiceTier { get; set; }

        [JsonPropertyName("usage")]
        public TokenUsage? Usage { get; set; }
    }
}
