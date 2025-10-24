using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    public class FunctionDefinition
    {
        [JsonPropertyName("name")]
        public required string Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("parameters")]
        public FunctionParameters? Parameters { get; set; } // JSON schema

        [JsonPropertyName("strict")]
        public bool? Strict { get; set; }
    }
}
