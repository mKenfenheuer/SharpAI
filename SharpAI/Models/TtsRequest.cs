using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    /// <summary>
    /// Request model for OpenAI-compatible Text-to-Speech API
    /// </summary>
    public class TtsRequest
    {
        /// <summary>
        /// The text to generate audio for. The maximum length is 4096 characters.
        /// </summary>
        [Required]
        [MaxLength(4096)]
        [JsonPropertyName("input")]
        public string Input { get; set; } = string.Empty;

        /// <summary>
        /// One of the available TTS models: tts-1 or tts-1-hd
        /// </summary>
        [Required]
        [JsonPropertyName("model")]
        public string Model { get; set; } = "tts-1";

        /// <summary>
        /// The voice to use when generating the audio. 
        /// Supported voices are: alloy, echo, fable, onyx, nova, and shimmer.
        /// </summary>
        [Required]
        [JsonPropertyName("voice")]
        public string Voice { get; set; } = "alloy";

        /// <summary>
        /// The format to audio in. Supported formats are mp3, opus, aac, flac, wav, and pcm.
        /// </summary>
        [JsonPropertyName("response_format")]
        public string? ResponseFormat { get; set; } = "wav";

        /// <summary>
        /// The speed of the generated audio. Select a value from 0.25 to 4.0. 1.0 is the default.
        /// </summary>
        [JsonPropertyName("speed")]
        public double? Speed { get; set; } = 1.0;

        /// <summary>
        /// SharpAI extension: Emotion intensity for voice synthesis (0.0 to 1.0)
        /// </summary>
        [JsonPropertyName("emotion_intensity")]
        public float? EmotionIntensity { get; set; } = 0.5f;

        /// <summary>
        /// SharpAI extension: Reference audio file path for voice cloning
        /// </summary>
        [JsonPropertyName("reference_audio")]
        public string? ReferenceAudio { get; set; }

        /// <summary>
        /// SharpAI extension: CFG weight for controlling generation quality (0.0 to 1.0, default: 0.5)
        /// Lower values (~0.3) help with fast speakers and dramatic speech
        /// </summary>
        [JsonPropertyName("cfg_weight")]
        public float? CfgWeight { get; set; } = 0.5f;

        /// <summary>
        /// SharpAI extension: Exaggeration level for expressive speech (0.0 to 1.0, default: 0.5)
        /// Higher values (~0.7) for more dramatic/expressive speech
        /// </summary>
        [JsonPropertyName("exaggeration")]
        public float? Exaggeration { get; set; } = 0.5f;
    }

    /// <summary>
    /// Form request model for TTS with optional reference audio upload
    /// </summary>
    public class TtsFormRequest
    {
        /// <summary>
        /// The text to generate audio for. The maximum length is 4096 characters.
        /// </summary>
        [Required]
        [MaxLength(4096)]
        public string Input { get; set; } = string.Empty;

        /// <summary>
        /// One of the available TTS models: tts-1 or tts-1-hd
        /// </summary>
        [Required]
        public string Model { get; set; } = "tts-1";

        /// <summary>
        /// The voice to use when generating the audio.
        /// </summary>
        [Required]
        public string Voice { get; set; } = "alloy";

        /// <summary>
        /// The format to audio in. Supported formats are mp3, opus, aac, flac, wav, and pcm.
        /// </summary>
        [JsonPropertyName("response_format")]
        public string? ResponseFormat { get; set; } = "mp3";

        /// <summary>
        /// The speed of the generated audio. Select a value from 0.25 to 4.0. 1.0 is the default.
        /// </summary>
        public double? Speed { get; set; } = 1.0;

        /// <summary>
        /// SharpAI extension: Emotion intensity for voice synthesis (0.0 to 1.0)
        /// </summary>
        [JsonPropertyName("emotion_intensity")]
        public float? EmotionIntensity { get; set; } = 0.5f;

        /// <summary>
        /// SharpAI extension: Reference audio file for voice cloning
        /// </summary>
        [JsonPropertyName("reference_audio")]
        public IFormFile? ReferenceAudio { get; set; }

        /// <summary>
        /// SharpAI extension: CFG weight for controlling generation quality (0.0 to 1.0, default: 0.5)
        /// </summary>
        [JsonPropertyName("cfg_weight")]
        public float? CfgWeight { get; set; } = 0.5f;

        /// <summary>
        /// SharpAI extension: Exaggeration level for expressive speech (0.0 to 1.0, default: 0.5)
        /// </summary>
        [JsonPropertyName("exaggeration")]
        public float? Exaggeration { get; set; } = 0.5f;
    }

    /// <summary>
    /// Internal request structure for the TTS queue
    /// </summary>
    public class TtsInternalRequest
    {
        public string RequestId { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string? LanguageId { get; set; }
        public float EmotionIntensity { get; set; } = 0.5f;
        public string? ReferenceAudioPath { get; set; }
        public float CfgWeight { get; set; } = 0.5f;
        public float Exaggeration { get; set; } = 0.5f;
        public string ResponseFormat { get; set; } = "wav";
    }
}