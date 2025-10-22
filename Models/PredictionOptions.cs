using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    public class PredictionOptions
    {
        [JsonPropertyName("partial_output")]
        public string? PartialOutput { get; set; }

        [JsonPropertyName("enable")]
        public bool? Enable { get; set; }
    }
}
