using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    /// <summary>
    /// JSON converter that serializes enums as lowercase snake_case strings.
    /// Used throughout the API to maintain OpenAI compatibility.
    /// </summary>
    /// <typeparam name="T">The enum type to convert</typeparam>
    public class LowercaseEnumConverter<T> : JsonStringEnumConverter
        where T : struct, Enum
    {
        /// <summary>
        /// Initializes the converter with snake_case_lower naming policy.
        /// </summary>
        public LowercaseEnumConverter() : base(JsonNamingPolicy.SnakeCaseLower) { }
    }
}
