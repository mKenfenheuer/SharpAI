using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    public class ResponseFormat
    {
        [JsonPropertyName("type")]
        public ResponseFormatType? Type { get; set; }

        [JsonPropertyName("json_schema")]
        public JsonSchemaFormat? JsonSchema { get; set; }
    }

    public class JsonSchemaFormat
    {
        [JsonPropertyName("name")]
        public required string Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("schema")]
        public object? Schema { get; set; }

        [JsonPropertyName("strict")]
        public bool? Strict { get; set; } = false;
    }

    [JsonConverter(typeof(LowercaseEnumConverter<ResponseFormatType>))]
    public enum ResponseFormatType { Text, JsonObject, JsonSchema }
}
