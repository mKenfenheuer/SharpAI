using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    /// <summary>
    /// Represents a complete chat completion response from the API.
    /// Compatible with OpenAI's chat completion response format.
    /// </summary>
    public class ChatCompletionResponse
    {
        /// <summary>
        /// Unique identifier for the chat completion.
        /// </summary>
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        /// <summary>
        /// The object type (chat.completion).
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
        /// List of completion choices. Can be more than one if n is greater than 1.
        /// </summary>
        [JsonPropertyName("choices")]
        public List<ChatCompletionChoice>? Choices { get; set; }

        /// <summary>
        /// Usage statistics for the completion request.
        /// </summary>
        [JsonPropertyName("usage")]
        public TokenUsage? Usage { get; set; }
    }

    /// <summary>
    /// Defines the type of completion object.
    /// </summary>
    [JsonConverter(typeof(LowercaseEnumConverter<CompletionObjectType>))]
    public enum CompletionObjectType 
    { 
        /// <summary>
        /// Complete chat completion response.
        /// </summary>
        ChatCompletion, 
        /// <summary>
        /// Streaming chat completion chunk.
        /// </summary>
        ChatCompletionChunk 
    }
}
