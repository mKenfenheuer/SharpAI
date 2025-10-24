using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    public class AudioOptions
    {
        [JsonPropertyName("voice")]
        public string? Voice { get; set; }

        [JsonPropertyName("format")]
        public string? Format { get; set; } // mp3 | wav | etc.

        [JsonPropertyName("response_format")]
        public string? ResponseFormat { get; set; }
    }
}
