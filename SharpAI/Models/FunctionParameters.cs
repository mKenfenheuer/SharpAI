using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    /// <summary>
    /// Represents JSON schema for function parameters according to OpenAI specification
    /// </summary>
    public class FunctionParameters
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "object";

        [JsonPropertyName("properties")]
        public Dictionary<string, PropertySchema>? Properties { get; set; }

        [JsonPropertyName("required")]
        public List<string>? Required { get; set; }

        [JsonPropertyName("additionalProperties")]
        public bool AdditionalProperties { get; set; } = false;

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }

    /// <summary>
    /// Represents a property schema within function parameters
    /// </summary>
    public class PropertySchema
    {
        [JsonPropertyName("type")]
        public object? Type { get; set; } // Can be string or array of strings for union types

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("enum")]
        public List<object>? Enum { get; set; }

        [JsonPropertyName("items")]
        public PropertySchema? Items { get; set; } // For array types

        [JsonPropertyName("properties")]
        public Dictionary<string, PropertySchema>? Properties { get; set; } // For nested objects

        [JsonPropertyName("required")]
        public List<string>? Required { get; set; } // For nested objects

        [JsonPropertyName("additionalProperties")]
        public bool? AdditionalProperties { get; set; }

        [JsonPropertyName("default")]
        public object? Default { get; set; }

        [JsonPropertyName("minimum")]
        public double? Minimum { get; set; }

        [JsonPropertyName("maximum")]
        public double? Maximum { get; set; }

        [JsonPropertyName("minLength")]
        public int? MinLength { get; set; }

        [JsonPropertyName("maxLength")]
        public int? MaxLength { get; set; }

        [JsonPropertyName("pattern")]
        public string? Pattern { get; set; }

        [JsonPropertyName("format")]
        public string? Format { get; set; }
    }
}