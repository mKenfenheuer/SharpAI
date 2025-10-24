using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    public class ToolCall
    {
        [JsonPropertyName("id")]
        public required string Id { get; set; }

        [JsonPropertyName("type")]
        public required ToolType Type { get; set; }

        [JsonPropertyName("function")]
        public FunctionCall? Function { get; set; }
    }

    public class FunctionCall
    {
        [JsonPropertyName("name")]
        public required string Name { get; set; }

        [JsonPropertyName("arguments")]
        public required string Arguments { get; set; } // JSON string
    }

    [JsonConverter(typeof(LowercaseEnumConverter<ToolType>))]
    public enum ToolType { Function }
}