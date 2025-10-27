using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    /// <summary>
    /// Represents token usage statistics for a chat completion request.
    /// Compatible with OpenAI's usage reporting format.
    /// </summary>
    public class TokenUsage
    {
        /// <summary>
        /// Number of tokens in the prompt.
        /// </summary>
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        /// <summary>
        /// Number of tokens in the generated completion.
        /// </summary>
        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }

        /// <summary>
        /// Total number of tokens used in the request (prompt + completion).
        /// </summary>
        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }

        /// <summary>
        /// Number of tokens used for reasoning (for reasoning models like o1).
        /// </summary>
        [JsonPropertyName("reasoning_tokens")]
        public int? ReasoningTokens { get; set; }

        /// <summary>
        /// Number of tokens retrieved from cache to avoid recomputation.
        /// </summary>
        [JsonPropertyName("cached_tokens")]
        public int? CachedTokens { get; set; }
    }
}
