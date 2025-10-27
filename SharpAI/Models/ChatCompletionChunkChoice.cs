using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    /// <summary>
    /// Represents a choice in a streaming chat completion chunk.
    /// Contains delta information for incremental message updates.
    /// </summary>
    public class ChatCompletionChunkChoice
    {
        /// <summary>
        /// The index of this choice in the list of choices.
        /// </summary>
        [JsonPropertyName("index")]
        public int Index { get; set; }

        /// <summary>
        /// The incremental message delta for this chunk.
        /// </summary>
        [JsonPropertyName("delta")]
        public ChatMessage? Delta { get; set; }

        /// <summary>
        /// The reason the model stopped generating tokens (null for intermediate chunks).
        /// </summary>
        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }
}
