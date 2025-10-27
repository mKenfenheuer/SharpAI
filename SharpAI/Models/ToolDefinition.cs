using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    /// <summary>
    /// Represents a tool definition that can be used by the AI model.
    /// Currently supports function tools for calling external functions.
    /// </summary>
    public class ToolDefinition
    {
        /// <summary>
        /// The type of tool. Currently only 'function' is supported.
        /// </summary>
        [JsonPropertyName("type")]
        public ToolType? Type { get; set; }

        /// <summary>
        /// Function definition when the tool type is 'function'.
        /// </summary>
        [JsonPropertyName("function")]
        public FunctionDefinition? Function { get; set; }
    }
}
