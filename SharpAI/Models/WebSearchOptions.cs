using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    /// <summary>
    /// Configuration options for web search functionality.
    /// </summary>
    public class WebSearchOptions
    {
        /// <summary>
        /// Whether web search functionality is enabled.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool? Enabled { get; set; }

        /// <summary>
        /// Maximum number of search results to retrieve.
        /// </summary>
        [JsonPropertyName("max_results")]
        public int? MaxResults { get; set; }
    }
}
