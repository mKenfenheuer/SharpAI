using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    /// <summary>
    /// Configuration definition for GPT-OSS models using the Harmony format.
    /// All models automatically use the Harmony format without requiring Jinja templates or custom parsing.
    /// Supports automatic model downloading and loading with GPU acceleration.
    /// </summary>
    public class ModelConfiguration
    {
        /// <summary>
        /// Display name for the model (e.g., "gpt-oss-20b").
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Hugging Face repository identifier for downloading the model.
        /// </summary>
        [JsonPropertyName("huggingface_id")]
        public string HuggingFaceId { get; set; } = string.Empty;

        /// <summary>
        /// Specific filename of the model file to download.
        /// </summary>
        [JsonPropertyName("filename")]
        public string Filename { get; set; } = string.Empty;

        /// <summary>
        /// Maximum context size (number of tokens) the model supports.
        /// </summary>
        [JsonPropertyName("context_size")]
        public uint ContextSize { get; set; } = 4096;

        /// <summary>
        /// Number of layers to offload to GPU for acceleration (0 for CPU-only).
        /// </summary>
        [JsonPropertyName("gpu_layers")]
        public int GpuLayers { get; set; } = 5;

        /// <summary>
        /// Human-readable description of the model and its capabilities.
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Approximate size of the model file in gigabytes.
        /// </summary>
        [JsonPropertyName("size_gb")]
        public double SizeGB { get; set; }
    }

    /// <summary>
    /// Root configuration object containing all available model definitions.
    /// </summary>
    public class ModelsConfiguration
    {
        /// <summary>
        /// List of all available model configurations.
        /// </summary>
        [JsonPropertyName("models")]
        public List<ModelConfiguration> Models { get; set; } = new List<ModelConfiguration>();
    }
}