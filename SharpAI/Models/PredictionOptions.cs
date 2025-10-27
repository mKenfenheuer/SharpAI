using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    /// <summary>
    /// Configuration options for predictive features and partial output handling.
    /// </summary>
    public class PredictionOptions
    {
        /// <summary>
        /// Partial output content for prediction context.
        /// </summary>
        [JsonPropertyName("partial_output")]
        public string? PartialOutput { get; set; }

        /// <summary>
        /// Whether to enable predictive features.
        /// </summary>
        [JsonPropertyName("enable")]
        public bool? Enable { get; set; }
    }
}
