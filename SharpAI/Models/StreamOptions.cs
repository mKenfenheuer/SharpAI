using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    public class StreamOptions
    {
        [JsonPropertyName("include_usage")]
        public bool? IncludeUsage { get; set; }
    }
}
