using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    /// <summary>
    /// Represents a chunk of streaming chat completion data.
    /// Compatible with OpenAI's streaming chat completion format.
    /// </summary>
    public class ChatCompletionChunk
    {
        /// <summary>
        /// Unique identifier for the chat completion.
        /// </summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        /// <summary>
        /// The object type (chat.completion.chunk).
        /// </summary>
        [JsonPropertyName("object")]
        public CompletionObjectType? Object { get; set; }

        /// <summary>
        /// Unix timestamp (in seconds) when the chat completion was created.
        /// </summary>
        [JsonPropertyName("created")]
        public long Created { get; set; }

        /// <summary>
        /// The model used for the completion.
        /// </summary>
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
