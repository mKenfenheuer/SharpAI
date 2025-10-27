using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    /// <summary>
    /// Represents the response from an audio transcription operation using Whisper models.
    /// Compatible with OpenAI's audio transcription response format.
    /// </summary>
    public class AudioTranscriptionResponse
    {
        /// <summary>
        /// The transcribed text from the audio file.
        /// </summary>
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        /// <summary>
        /// The type of task performed (typically "transcribe").
        /// </summary>
        [JsonPropertyName("task")]
        public string? Task { get; set; }

        /// <summary>
        /// The detected or specified language of the audio (ISO-639-1 format).
        /// </summary>
        [JsonPropertyName("language")]
        public string? Language { get; set; }

        /// <summary>
        /// The duration of the audio file in seconds.
        /// </summary>
        [JsonPropertyName("duration")]
        public double? Duration { get; set; }

        /// <summary>
        /// Detailed transcription segments with timestamps (when verbose format is requested).
        /// </summary>
        [JsonPropertyName("segments")]
        public List<TranscriptionSegment>? Segments { get; set; }

        /// <summary>
        /// Individual word-level timestamps (when word-level granularity is requested).
        /// </summary>
        [JsonPropertyName("words")]
        public List<TranscriptionWord>? Words { get; set; }
    }

    /// <summary>
    /// Represents a segment of transcribed audio with timing and confidence information.
    /// </summary>
    public class TranscriptionSegment
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("seek")]
        public int Seek { get; set; }

        [JsonPropertyName("start")]
        public double Start { get; set; }

        [JsonPropertyName("end")]
        public double End { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("tokens")]
        public List<int>? Tokens { get; set; }

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("avg_logprob")]
        public double AvgLogprob { get; set; }

        [JsonPropertyName("compression_ratio")]
        public double CompressionRatio { get; set; }

        [JsonPropertyName("no_speech_prob")]
        public double NoSpeechProb { get; set; }
    }

    /// <summary>
    /// Represents a single word in the transcription with start and end timestamps.
    /// </summary>
    public class TranscriptionWord
    {
        [JsonPropertyName("word")]
        public string? Word { get; set; }

        [JsonPropertyName("start")]
        public double Start { get; set; }

        [JsonPropertyName("end")]
        public double End { get; set; }
    }

    /// <summary>
    /// Represents a chunk of streaming audio transcription data.
    /// </summary>
    public class AudioTranscriptionChunk
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("start")]
        public double Start { get; set; }

        [JsonPropertyName("end")]
        public double End { get; set; }

        [JsonPropertyName("is_final")]
        public bool IsFinal { get; set; }
    }
}