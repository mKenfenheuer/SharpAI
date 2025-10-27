using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    public class ToolCall
    {
        [JsonPropertyName("type")]
        public required ToolType Type { get; set; }

        [JsonPropertyName("function")]
        public FunctionCall? Function { get; set; }

        // For streaming compatibility, some responses might include call id
        [JsonPropertyName("id")]
        public string? CallId { get; set; }
    }

    public class FunctionCall
    {
        [JsonPropertyName("name")]
        public required string Name { get; set; }

        [JsonIgnore]
        public string Namespace { get; set; } = "functions"; // Default namespace

        [JsonPropertyName("arguments")]
        public required string Arguments { get; set; } // JSON string
    }

    [JsonConverter(typeof(LowercaseEnumConverter<ToolType>))]
    public enum ToolType { Function }
}