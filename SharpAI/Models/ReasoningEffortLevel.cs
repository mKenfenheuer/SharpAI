using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    /// <summary>
    /// Controls the amount of effort the model puts into reasoning for complex requests.
    /// Higher values increase reasoning time and potentially improve response quality.
    /// </summary>
    [JsonConverter(typeof(LowercaseEnumConverter<ReasoningEffortLevel>))]
    public enum ReasoningEffortLevel 
    { 
        /// <summary>
        /// Minimal reasoning effort for quick responses.
        /// </summary>
        Minimal, 
        /// <summary>
        /// Low reasoning effort.
        /// </summary>
        Low, 
        /// <summary>
        /// Medium reasoning effort for balanced performance.
        /// </summary>
        Medium, 
        /// <summary>
        /// High reasoning effort for complex problems.
        /// </summary>
        High 
    }
}
