using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    /// <summary>
    /// Represents a request to transcribe audio to text using Whisper models.
    /// Compatible with OpenAI's audio transcription API.
    /// </summary>
    public class AudioTranscriptionRequest
    {
        /// <summary>
        /// The audio file object (not file name) to transcribe, in one of these formats: flac, mp3, mp4, mpeg, mpga, m4a, ogg, wav, or webm.
        /// </summary>
        [JsonPropertyName("file")]
        public IFormFile? File { get; set; }

        /// <summary>
        /// ID of the model to use. Currently, only whisper-1 (which maps to different sized models) is available.
        /// </summary>
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        /// <summary>
        /// The language of the input audio. Supplying the input language in ISO-639-1 format will improve accuracy and latency.
        /// </summary>
        [JsonPropertyName("language")]
        public string? Language { get; set; }

        /// <summary>
        /// An optional text to guide the model's style or continue a previous audio segment.
        /// </summary>
        [JsonPropertyName("prompt")]
        public string? Prompt { get; set; }

        /// <summary>
        /// The format of the transcript output, in one of these options: json, text, srt, verbose_json, or vtt.
        /// </summary>
        [JsonPropertyName("response_format")]
        public AudioResponseFormat? ResponseFormat { get; set; }

        /// <summary>
        /// The sampling temperature, between 0 and 1. Higher values like 0.8 will make the output more random, while lower values like 0.2 will make it more focused and deterministic.
        /// </summary>
        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; }

        /// <summary>
        /// The timestamp granularities to populate for this transcription.
        /// </summary>
        [JsonPropertyName("timestamp_granularities")]
        public List<TimestampGranularity>? TimestampGranularities { get; set; }
    }

    /// <summary>
    /// Defines the format of the audio transcription response.
    /// </summary>
    [JsonConverter(typeof(LowercaseEnumConverter<AudioResponseFormat>))]
    public enum AudioResponseFormat
    {
        /// <summary>
        /// Simple JSON format with just the transcribed text.
        /// </summary>
        Json,
        /// <summary>
        /// Plain text format.
        /// </summary>
        Text,
        /// <summary>
        /// SubRip Subtitle format with timestamps.
        /// </summary>
        Srt,
        /// <summary>
        /// Detailed JSON format with word-level timestamps and confidence scores.
        /// </summary>
        VerboseJson,
        /// <summary>
        /// WebVTT subtitle format with timestamps.
        /// </summary>
        Vtt
    }

    /// <summary>
    /// Defines the granularity of timestamps in the transcription response.
    /// </summary>
    [JsonConverter(typeof(LowercaseEnumConverter<TimestampGranularity>))]
    public enum TimestampGranularity
    {
        /// <summary>
        /// Provide timestamps for individual words.
        /// </summary>
        Word,
        /// <summary>
        /// Provide timestamps for sentence segments.
        /// </summary>
        Segment
    }
}