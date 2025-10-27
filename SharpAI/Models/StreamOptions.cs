using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    /// <summary>
    /// Configuration options for streaming chat completions.
    /// </summary>
    public class StreamOptions
    {
        /// <summary>
        /// If set, usage statistics will be included in the final chunk of the stream.
        /// </summary>
        [JsonPropertyName("include_usage")]
        public bool? IncludeUsage { get; set; }
    }
}
