using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    public class WebSearchOptions
    {
        [JsonPropertyName("enabled")]
        public bool? Enabled { get; set; }

        [JsonPropertyName("max_results")]
        public int? MaxResults { get; set; }
    }
}
