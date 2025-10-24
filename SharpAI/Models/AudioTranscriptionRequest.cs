using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    public class AudioTranscriptionRequest
    {
        [JsonPropertyName("file")]
        public IFormFile? File { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("language")]
        public string? Language { get; set; }

        [JsonPropertyName("prompt")]
        public string? Prompt { get; set; }

        [JsonPropertyName("response_format")]
        public AudioResponseFormat? ResponseFormat { get; set; }

        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; }

        [JsonPropertyName("timestamp_granularities")]
        public List<TimestampGranularity>? TimestampGranularities { get; set; }
    }

    [JsonConverter(typeof(LowercaseEnumConverter<AudioResponseFormat>))]
    public enum AudioResponseFormat
    {
        Json,
        Text,
        Srt,
        VerboseJson,
        Vtt
    }

    [JsonConverter(typeof(LowercaseEnumConverter<TimestampGranularity>))]
    public enum TimestampGranularity
    {
        Word,
        Segment
    }
}