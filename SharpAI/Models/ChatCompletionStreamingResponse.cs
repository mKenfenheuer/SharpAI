using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    /// <summary>
    /// Represents a streaming chat completion response from the API.
    /// Compatible with OpenAI's streaming chat completion response format.
    /// </summary>
    public class ChatCompletionStreamingResponse
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

        /// <summary>
        /// List of completion choices with delta updates.
        /// </summary>
        [JsonPropertyName("choices")]
        public List<ChatCompletionStreamingChoice>? Choices { get; set; }

        /// <summary>
        /// Usage statistics for the completion request. Only present in the final chunk.
        /// </summary>
        [JsonPropertyName("usage")]
        public TokenUsage? Usage { get; set; }
    }
}