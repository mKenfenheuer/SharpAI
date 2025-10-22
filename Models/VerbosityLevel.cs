using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    [JsonConverter(typeof(LowercaseEnumConverter<VerbosityLevel>))]
    public enum VerbosityLevel { Low, Medium, High }
}
