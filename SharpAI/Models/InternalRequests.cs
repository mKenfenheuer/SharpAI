using SharpAI.Models;

namespace SharpAI.Models
{
    /// <summary>
    /// Internal request structure for the Whisper transcription queue
    /// </summary>
    public class WhisperInternalRequest
    {
        public string RequestId { get; set; } = string.Empty;
        public Stream AudioStream { get; set; } = null!;
        public AudioTranscriptionRequest TranscriptionRequest { get; set; } = null!;
        public string? AudioFileName { get; set; }
        public bool IsStreaming { get; set; } = false;
    }

    /// <summary>
    /// Internal response structure for the Whisper transcription queue
    /// </summary>
    public class WhisperInternalResponse
    {
        public AudioTranscriptionResponse? TranscriptionResponse { get; set; }
        public IAsyncEnumerable<AudioTranscriptionChunk>? StreamingResponse { get; set; }
        public Exception? Error { get; set; }
    }
}