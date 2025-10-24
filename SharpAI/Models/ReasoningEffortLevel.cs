using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    [JsonConverter(typeof(LowercaseEnumConverter<ReasoningEffortLevel>))]
    public enum ReasoningEffortLevel { Minimal, Low, Medium, High }
}
