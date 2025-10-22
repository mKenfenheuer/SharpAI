using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    public class TokenUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }

        [JsonPropertyName("reasoning_tokens")]
        public int? ReasoningTokens { get; set; }

        [JsonPropertyName("cached_tokens")]
        public int? CachedTokens { get; set; }
    }
}
