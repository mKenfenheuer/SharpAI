using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    public class ToolDefinition
    {
        [JsonPropertyName("type")]
        public ToolType? Type { get; set; }

        [JsonPropertyName("function")]
        public FunctionDefinition? Function { get; set; }
    }
}
