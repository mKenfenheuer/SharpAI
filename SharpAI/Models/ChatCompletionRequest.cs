using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    public class ChatCompletionRequest
    {
        [JsonPropertyName("messages")]
        public required List<ChatMessage> Messages { get; set; }

        [JsonPropertyName("model")]
        public required string Model { get; set; }

        [JsonPropertyName("frequency_penalty")]
        public double? FrequencyPenalty { get; set; }

        [JsonPropertyName("max_completion_tokens")]
        public int? MaxCompletionTokens { get; set; }

        [JsonPropertyName("max_tokens")]
        public int? MaxTokens { get; set; } // Deprecated

        [JsonPropertyName("parallel_tool_calls")]
        public bool? ParallelToolCalls { get; set; }

        [JsonPropertyName("presence_penalty")]
        public double? PresencePenalty { get; set; }

        [JsonPropertyName("reasoning_effort")]
        public ReasoningEffortLevel? ReasoningEffort { get; set; }

        [JsonPropertyName("response_format")]
        public ResponseFormat? ResponseFormat { get; set; }

        [JsonPropertyName("stop")]
        public object? Stop { get; set; } // string | array | null

        [JsonPropertyName("stream")]
        public bool? Stream { get; set; }

        [JsonPropertyName("stream_options")]
        public StreamOptions? StreamOptions { get; set; }

        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; }

        [JsonPropertyName("tool_choice")]
        public ToolChoice? ToolChoice { get; set; }

        [JsonPropertyName("tools")]
        public List<ToolDefinition>? Tools { get; set; }

        [JsonPropertyName("top_p")]
        public double? TopP { get; set; }
    }
}
