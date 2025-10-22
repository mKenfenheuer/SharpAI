using System.Text.Json.Serialization;

namespace SharpAI.Models
{
    public class ModelConfiguration
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("huggingface_id")]
        public string HuggingFaceId { get; set; } = string.Empty;

        [JsonPropertyName("filename")]
        public string Filename { get; set; } = string.Empty;

        [JsonPropertyName("context_size")]
        public uint ContextSize { get; set; } = 4096;

        [JsonPropertyName("gpu_layers")]
        public int GpuLayers { get; set; } = 5;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("size_gb")]
        public double SizeGB { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = "instruct";
    }

    public class ModelsConfiguration
    {
        [JsonPropertyName("models")]
        public List<ModelConfiguration> Models { get; set; } = new List<ModelConfiguration>();
    }
}