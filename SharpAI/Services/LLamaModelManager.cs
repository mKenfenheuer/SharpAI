using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;
using SharpAI.Models;
using SharpAI.Services.Harmony;
using System.Collections.Concurrent;
using System.Text.Json;

namespace SharpAI.Services
{
    /// <summary>
    /// Manages GPT-OSS models and provides OpenAI-compatible model name mapping.
    /// Supports gpt-oss-20b and gpt-oss-120b models with harmony response format.
    /// 
    /// Model Mapping:
    /// - Standard OpenAI models (gpt-4, gpt-4o, gpt-3.5-turbo, etc.) → gpt-oss-20b
    /// - Larger models (gpt-5, gpt-5-pro, o1, o1-preview) → gpt-oss-120b
    /// - Direct model names (gpt-oss-20b, gpt-oss-120b) → No mapping
    /// </summary>
    public class LLamaModelManager : IDisposable
    {
        private readonly string _modelsDirectory;
        private readonly ILogger<LLamaModelManager> _logger;
        private readonly HttpClient _httpClient;
        private readonly ConcurrentDictionary<string, ModelInstance> _loadedModels = new();
        private readonly ModelsConfiguration _modelsConfig;
        private readonly SemaphoreSlim _downloadSemaphore = new(1, 1);
        private readonly LLamaNativeLogging _nativeLogging;

        /// <summary>
        /// Initializes a new instance of the LLamaModelManager.
        /// </summary>
        /// <param name="logger">Logger for model manager operations</param>
        /// <param name="httpClient">HTTP client for downloading models</param>
        /// <param name="nativeLogging">Native logging bridge for LLama</param>
        public LLamaModelManager(ILogger<LLamaModelManager> logger, HttpClient httpClient, LLamaNativeLogging nativeLogging)
        {
            _logger = logger;
            _httpClient = httpClient;
            _nativeLogging = nativeLogging;
            _httpClient = httpClient;
            _modelsDirectory = Path.Combine("Data", "Models");
            Directory.CreateDirectory(_modelsDirectory);
            _modelsConfig = LoadModelsConfiguration();
        }

        /// <summary>
        /// Gets a model instance by name, loading it if necessary. Maps OpenAI model names to GPT-OSS models.
        /// </summary>
        /// <param name="modelName">Requested model name (OpenAI-compatible or direct GPT-OSS name)</param>
        /// <returns>Loaded model instance</returns>
        public async Task<ModelInstance> GetModelAsync(string modelName)
        {
            // Map OpenAI model names to gpt-oss models
            var mappedModelName = MapOpenAIModelToGptOss(modelName);
            
            if (mappedModelName != modelName)
            {
                _logger.LogInformation("Mapped model '{RequestedModel}' to '{MappedModel}'", modelName, mappedModelName);
            }
            
            // Check if model is already loaded
            if (_loadedModels.TryGetValue(mappedModelName, out var existingModel))
            {
                return existingModel;
            }

            // Find model configuration
            var modelConfig = _modelsConfig.Models.FirstOrDefault(m => 
                m.Name.Equals(mappedModelName, StringComparison.OrdinalIgnoreCase));

            if (modelConfig == null)
            {
                throw new ArgumentException($"Model '{modelName}' is not supported. Only gpt-oss-20b and gpt-oss-120b models are available.");
            }

            // Ensure model file exists (download if needed)
            var modelPath = await EnsureModelFileAsync(modelConfig);

            // Load the model
            var modelInstance = await LoadModelAsync(modelConfig, modelPath);
            
            // Cache the loaded model using the mapped name
            _loadedModels.TryAdd(mappedModelName, modelInstance);
            
            return modelInstance;
        }

        /// <summary>
        /// Ensures a model file exists locally, downloading it if necessary.
        /// </summary>
        /// <param name="config">Model configuration</param>
        /// <returns>Path to the local model file</returns>
        private async Task<string> EnsureModelFileAsync(ModelConfiguration config)
        {
            var modelPath = Path.Combine(_modelsDirectory, config.Filename);
            
            if (File.Exists(modelPath))
            {
                _logger.LogInformation("Model {ModelName} already exists at {ModelPath}", config.Name, modelPath);
                return modelPath;
            }

            _logger.LogInformation("Model {ModelName} not found locally. Downloading from HuggingFace: {HuggingFaceId}", 
                config.Name, config.HuggingFaceId);

            await _downloadSemaphore.WaitAsync();
            try
            {
                // Double-check after acquiring the semaphore
                if (File.Exists(modelPath))
                {
                    return modelPath;
                }

                await DownloadModelFromHuggingFaceAsync(config, modelPath);
                return modelPath;
            }
            finally
            {
                _downloadSemaphore.Release();
            }
        }

        /// <summary>
        /// Downloads a model from HuggingFace with progress tracking.
        /// </summary>
        /// <param name="config">Model configuration</param>
        /// <param name="destinationPath">Local path to save the model</param>
        private async Task DownloadModelFromHuggingFaceAsync(ModelConfiguration config, string destinationPath)
        {
            var huggingFaceUrl = $"https://huggingface.co/{config.HuggingFaceId}/resolve/main/{config.Filename}";
            
            _logger.LogInformation("Downloading model {ModelName} from {Url} (Size: ~{SizeGB:F1} GB)", 
                config.Name, huggingFaceUrl, config.SizeGB);

            var tempPath = destinationPath + ".tmp";
            
            try
            {
                using var response = await _httpClient.GetAsync(huggingFaceUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var downloadedBytes = 0L;

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                var lastLogTime = DateTime.UtcNow;

                while (true)
                {
                    var bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    await fileStream.WriteAsync(buffer, 0, bytesRead);
                    downloadedBytes += bytesRead;

                    // Log progress every 30 seconds
                    if (DateTime.UtcNow - lastLogTime > TimeSpan.FromSeconds(30))
                    {
                        var progress = totalBytes > 0 ? (downloadedBytes * 100.0 / totalBytes) : 0;
                            _logger.LogDebug("Downloading {ModelName}: {Progress:F1}% ({DownloadedMB:F1}/{TotalMB:F1} MB)", 
                                config.Name, progress, downloadedBytes / 1024.0 / 1024.0, totalBytes / 1024.0 / 1024.0);
                        lastLogTime = DateTime.UtcNow;
                    }
                }

                // Move temp file to final location
                File.Move(tempPath, destinationPath);
                
                _logger.LogInformation("Successfully downloaded model {ModelName} to {Path}", config.Name, destinationPath);
            }
            catch (Exception ex)
            {
                // Clean up temp file if it exists
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }
                
                _logger.LogError(ex, "Failed to download model {ModelName} from {Url}", config.Name, huggingFaceUrl);
                throw new InvalidOperationException($"Failed to download model {config.Name}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Loads a model from a local file into memory and creates execution context.
        /// </summary>
        /// <param name="config">Model configuration</param>
        /// <param name="modelPath">Path to the local model file</param>
        /// <returns>Loaded model instance</returns>
        private async Task<ModelInstance> LoadModelAsync(ModelConfiguration config, string modelPath)
        {
            _logger.LogInformation("Loading model {ModelName} from {Path}", config.Name, modelPath);

            try
            {
                var parameters = new ModelParams(modelPath)
                {
                    ContextSize = config.ContextSize,
                    GpuLayerCount = config.GpuLayers
                };

                var weights = LLamaWeights.LoadFromFile(parameters);
                var context = weights.CreateContext(parameters);

                var modelInstance = new ModelInstance
                {
                    Name = config.Name,
                    Weights = weights,
                    Context = context
                };

                _logger.LogInformation("Successfully loaded model {ModelName}", config.Name);
                return modelInstance;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load model {ModelName} from {Path}", config.Name, modelPath);
                throw new InvalidOperationException($"Failed to load model {config.Name}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Maps OpenAI model names to internal GPT-OSS model names.
        /// </summary>
        /// <param name="modelName">OpenAI-style model name</param>
        /// <returns>Internal GPT-OSS model name</returns>
        private string MapOpenAIModelToGptOss(string modelName)
        {
            return modelName.ToLowerInvariant() switch
            {
                // Direct gpt-oss model names
                "gpt-oss-20b" => "gpt-oss-20b",
                "gpt-oss-120b" => "gpt-oss-120b",
                
                // Standard OpenAI models -> gpt-oss-20b (default)
                "gpt-4" => "gpt-oss-20b",
                "gpt-4-turbo" => "gpt-oss-20b", 
                "gpt-4-turbo-preview" => "gpt-oss-20b",
                "gpt-4o" => "gpt-oss-20b",
                "gpt-4o-mini" => "gpt-oss-20b",
                "gpt-3.5-turbo" => "gpt-oss-20b",
                "chatgpt-4o-latest" => "gpt-oss-20b",
                
                // Larger/Pro models -> gpt-oss-120b
                "gpt-5" => "gpt-oss-120b",
                "gpt-5-pro" => "gpt-oss-120b",
                "gpt-4-pro" => "gpt-oss-120b",
                "o1" => "gpt-oss-120b",
                "o1-preview" => "gpt-oss-120b",
                "o1-mini" => "gpt-oss-20b", // o1-mini maps to smaller model
                
                // Default fallback to 20b model
                _ => "gpt-oss-20b"
            };
        }

        /// <summary>
        /// Loads model configuration from defaults (could be extended to read from files).
        /// </summary>
        /// <returns>Models configuration with available GPT-OSS models</returns>
        private ModelsConfiguration LoadModelsConfiguration()
        {
            // Default configuration with popular models
            var defaultConfig = new ModelsConfiguration
            {
                Models = new List<ModelConfiguration>
                {
                    new ModelConfiguration
                    {
                        Name = "gpt-oss-20b",
                        HuggingFaceId = "ggml-org/gpt-oss-20b-GGUF",
                        Filename = "gpt-oss-20b-mxfp4.gguf",
                        ContextSize = 32768 * 2,
                        GpuLayers = 99999,
                        Description = "GPT-OSS 20B reasoning model with harmony format",
                        SizeGB = 12.1
                    },
                    new ModelConfiguration
                    {
                        Name = "gpt-oss-120b",
                        HuggingFaceId = "ggml-org/gpt-oss-120b-GGUF", 
                        Filename = "gpt-oss-120b-mxfp4-00001-of-00003.gguf",
                        ContextSize = 32768 * 2,
                        GpuLayers = 6,
                        Description = "GPT-OSS 120B reasoning model with harmony format - large variant",
                        SizeGB = 72.5
                    }
                }
            };

            return defaultConfig;
        }

        /// <summary>
        /// Gets list of all available model names (OpenAI-compatible names).
        /// </summary>
        /// <returns>List of supported model names</returns>
        public List<string> GetAvailableModels()
        {
            // Return OpenAI-compatible model names that are supported
            return new List<string>
            {
                // Direct gpt-oss models
                "gpt-oss-20b",
                "gpt-oss-120b",
                
                // OpenAI standard models that map to gpt-oss-20b
                "gpt-4",
                "gpt-4-turbo",
                "gpt-4-turbo-preview",
                "gpt-4o",
                "gpt-4o-mini",
                "gpt-3.5-turbo",
                "chatgpt-4o-latest",
                
                // Larger models that map to gpt-oss-120b
                "gpt-5",
                "gpt-5-pro",
                "gpt-4-pro",
                "o1",
                "o1-preview",
                "o1-mini"
            };
        }

        /// <summary>
        /// Gets list of internal model names (GPT-OSS model names).
        /// </summary>
        /// <returns>List of internal model names</returns>
        public List<string> GetInternalModels()
        {
            return _modelsConfig.Models.Select(m => m.Name).ToList();
        }

        /// <summary>
        /// Unloads a model from memory and disposes resources.
        /// </summary>
        /// <param name="modelName">Name of the model to unload</param>
        public void UnloadModel(string modelName)
        {
            if (_loadedModels.TryRemove(modelName, out var model))
            {
                model.Dispose();
                _logger.LogInformation("Unloaded model {ModelName}", modelName);
            }
        }

        public void Dispose()
        {
            foreach (var model in _loadedModels.Values)
            {
                model.Dispose();
            }
            _loadedModels.Clear();
            _downloadSemaphore.Dispose();
            _httpClient.Dispose();
        }
    }

    /// <summary>
    /// Represents a loaded model instance with weights, context, and provides executor.
    /// </summary>
    public class ModelInstance : IDisposable
    {
        public string Name { get; set; } = string.Empty;
        public LLamaWeights? Weights { get; set; }
        public LLamaContext? Context { get; set; }

        public void Dispose()
        {
            Weights?.Dispose();
            Context?.Dispose();
        }

        public async IAsyncEnumerable<string> InferAsync(ChatCompletionRequest request, string formattedPrompt, Grammar? grammar)
        {
            var inferenceParams = LLamaUtilities.CreateInferenceParams(request, this, grammar);
            var executor = new StatelessExecutor(Weights!, Context!.Params);

            await foreach (var token in executor.InferAsync(formattedPrompt, inferenceParams))
            {
                yield return token;
            }
        }
    }
}