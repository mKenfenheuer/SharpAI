using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    public class LowercaseEnumConverter<T> : JsonStringEnumConverter
        where T : struct, Enum
    {
        public LowercaseEnumConverter() : base(JsonNamingPolicy.SnakeCaseLower) { }
    }
}
