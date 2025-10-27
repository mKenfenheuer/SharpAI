using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    /// <summary>
    /// Defines a function that can be called by the AI model during tool use.
    /// Compatible with OpenAI's function definition format.
    /// </summary>
    public class FunctionDefinition
    {
        /// <summary>
        /// The name of the function to be called. Must be a-z, A-Z, 0-9, or contain underscores and dashes.
        /// </summary>
        [JsonPropertyName("name")]
        public required string Name { get; set; }

        /// <summary>
        /// A description of what the function does, used by the model to choose when and how to call the function.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>
        /// The parameters the function accepts, described as a JSON Schema object.
        /// </summary>
        [JsonPropertyName("parameters")]
        public FunctionParameters? Parameters { get; set; } // JSON schema

        /// <summary>
        /// Whether to enable strict schema adherence when generating the function call.
        /// </summary>
        [JsonPropertyName("strict")]
        public bool? Strict { get; set; }
    }
}
