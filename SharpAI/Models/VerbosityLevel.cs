using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    /// <summary>
    /// Controls the verbosity level of responses and logging output.
    /// </summary>
    [JsonConverter(typeof(LowercaseEnumConverter<VerbosityLevel>))]
    public enum VerbosityLevel 
    { 
        /// <summary>
        /// Low verbosity with minimal output.
        /// </summary>
        Low, 
        /// <summary>
        /// Medium verbosity with balanced detail.
        /// </summary>
        Medium, 
        /// <summary>
        /// High verbosity with detailed output.
        /// </summary>
        High 
    }
}
