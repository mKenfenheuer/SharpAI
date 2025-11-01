using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    /// <summary>
    /// Represents a chat completion request compatible with OpenAI's chat completion API.
    /// Contains all the parameters needed to generate a chat response including messages,
    /// model selection, and various generation parameters.
    /// </summary>
    public class ChatCompletionRequest
    {
        /// <summary>
        /// The messages to generate a chat completion for, in the format described in the chat guide.
        /// </summary>
        [JsonPropertyName("messages")]
        public required List<ChatMessage> Messages { get; set; }

        /// <summary>
        /// ID of the model to use. Compatible with OpenAI model names.
        /// </summary>
        [JsonPropertyName("model")]
        public required string Model { get; set; }

        /// <summary>
        /// Number between -2.0 and 2.0. Positive values penalize new tokens based on their existing frequency in the text so far.
        /// </summary>
        [JsonPropertyName("frequency_penalty")]
        public double? FrequencyPenalty { get; set; }

        /// <summary>
        /// The maximum number of tokens that can be generated in the chat completion.
        /// </summary>
        [JsonPropertyName("max_completion_tokens")]
        public int? MaxCompletionTokens { get; set; }

        /// <summary>
        /// The maximum number of tokens to generate in the chat completion (deprecated, use MaxCompletionTokens instead).
        /// </summary>
        [JsonPropertyName("max_tokens")]
        public int? MaxTokens { get; set; } // Deprecated

        /// <summary>
        /// Whether to enable parallel function calling during tool use.
        /// </summary>
        [JsonPropertyName("parallel_tool_calls")]
        public bool? ParallelToolCalls { get; set; }

        /// <summary>
        /// Number between -2.0 and 2.0. Positive values penalize new tokens based on whether they appear in the text so far.
        /// </summary>
        [JsonPropertyName("presence_penalty")]
        public double? PresencePenalty { get; set; }

        /// <summary>
        /// Controls the amount of effort the model puts into reasoning. Higher values increase reasoning time.
        /// </summary>
        [JsonPropertyName("reasoning_effort")]
        public ReasoningEffortLevel? ReasoningEffort { get; set; }

        /// <summary>
        /// An object specifying the format that the model must output.
        /// </summary>
        [JsonPropertyName("response_format")]
        public ResponseFormat? ResponseFormat { get; set; }

        /// <summary>
        /// Up to 4 sequences where the API will stop generating further tokens. Can be a string, array of strings, or null.
        /// </summary>
        [JsonPropertyName("stop")]
        public object? Stop { get; set; } // string | array | null

        /// <summary>
        /// If set, partial message deltas will be sent as data-only server-sent events.
        /// </summary>
        [JsonPropertyName("stream")]
        public bool? Stream { get; set; }

        /// <summary>
        /// Options for streaming response.
        /// </summary>
        [JsonPropertyName("stream_options")]
        public StreamOptions? StreamOptions { get; set; }

        /// <summary>
        /// What sampling temperature to use, between 0 and 2. Higher values make output more random.
        /// </summary>
        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; }

        /// <summary>
        /// Controls which (if any) tool is called by the model.
        /// Can be a string ("none", "auto", "required") or an object specifying a specific tool.
        /// - "none": The model will not call any tool and instead generates a message.
        /// - "auto": The model can pick between generating a message or calling one or more tools.
        /// - "required": The model must call one or more tools.
        /// - Object: {"type": "function", "function": {"name": "my_function"}} forces the model to call that tool.
        /// </summary>
        [JsonPropertyName("tool_choice")]
        public ToolChoice? ToolChoice { get; set; } = null;

        /// <summary>
        /// A list of tools the model may call. Currently, only functions are supported as a tool.
        /// </summary>
        [JsonPropertyName("tools")]
        public List<ToolDefinition>? Tools { get; set; }

        /// <summary>
        /// An alternative to sampling with temperature, called nucleus sampling, where the model considers the results of the tokens with top_p probability mass.
        /// </summary>
        [JsonPropertyName("top_p")]
        public double? TopP { get; set; }
    }
}
