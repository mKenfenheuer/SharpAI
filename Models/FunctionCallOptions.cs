using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    public class FunctionCallOptions
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
