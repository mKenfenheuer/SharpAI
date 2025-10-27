using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    /// <summary>
    /// Configuration options for audio processing operations including voice synthesis and format selection.
    /// </summary>
    public class AudioOptions
    {
        /// <summary>
        /// The voice to use for audio synthesis (if applicable).
        /// </summary>
        [JsonPropertyName("voice")]
        public string? Voice { get; set; }

        /// <summary>
        /// The audio format for output (mp3, wav, etc.).
        /// </summary>
        [JsonPropertyName("format")]
        public string? Format { get; set; } // mp3 | wav | etc.

        /// <summary>
        /// The response format for audio operations.
        /// </summary>
        [JsonPropertyName("response_format")]
        public string? ResponseFormat { get; set; }
    }
}
