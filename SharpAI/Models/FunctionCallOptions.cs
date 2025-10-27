using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    /// <summary>
    /// Options for controlling function call behavior in chat completions.
    /// </summary>
    public class FunctionCallOptions
    {
        /// <summary>
        /// The name of the specific function to call (when tool_choice is "function").
        /// </summary>
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
