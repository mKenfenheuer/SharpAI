using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    /// <summary>
    /// Specifies the format that the model must output.
    /// Compatible with OpenAI's response format specification.
    /// </summary>
    public class ResponseFormat
    {
        /// <summary>
        /// The type of response format (text, json_object, or json_schema).
        /// </summary>
        [JsonPropertyName("type")]
        public ResponseFormatType? Type { get; set; }

        /// <summary>
        /// JSON schema definition when type is "json_schema".
        /// </summary>
        [JsonPropertyName("json_schema")]
        public JsonSchemaFormat? JsonSchema { get; set; }
    }

    /// <summary>
    /// Defines a JSON schema for structured output format.
    /// </summary>
    public class JsonSchemaFormat
    {
        [JsonPropertyName("name")]
        public required string Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("schema")]
        public object? Schema { get; set; }

        [JsonPropertyName("strict")]
        public bool? Strict { get; set; } = false;
    }

    /// <summary>
    /// Defines the available response format types.
    /// </summary>
    [JsonConverter(typeof(LowercaseEnumConverter<ResponseFormatType>))]
    public enum ResponseFormatType 
    { 
        /// <summary>
        /// Plain text response format.
        /// </summary>
        Text, 
        /// <summary>
        /// JSON object response format.
        /// </summary>
        JsonObject, 
        /// <summary>
        /// Structured JSON response following a specific schema.
        /// </summary>
        JsonSchema 
    }
}
