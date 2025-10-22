using LLama;
using LLama.Common;
using LLama.Native;
using SharpAI.Models;
using System.Collections.Concurrent;
using System.Text.Json;

namespace SharpAI.Services
{
    public class LLamaModelManager : IDisposable
    {
        private readonly string _modelsDirectory;
        private readonly ILogger<LLamaModelManager> _logger;
        private readonly HttpClient _httpClient;
        private readonly ConcurrentDictionary<string, ModelInstance> _loadedModels = new();
        private readonly ModelsConfiguration _modelsConfig;
        private readonly SemaphoreSlim _downloadSemaphore = new(1, 1);
        private readonly LLamaNativeLogging _nativeLogging;

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

        public async Task<ModelInstance> GetModelAsync(string modelName)
        {
            // Check if model is already loaded
            if (_loadedModels.TryGetValue(modelName, out var existingModel))
            {
                return existingModel;
            }

            // Find model configuration
            var modelConfig = _modelsConfig.Models.FirstOrDefault(m => 
                m.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase));

            if (modelConfig == null)
            {
                throw new ArgumentException($"Model '{modelName}' is not configured. Available models: {string.Join(", ", _modelsConfig.Models.Select(m => m.Name))}");
            }

            // Ensure model file exists (download if needed)
            var modelPath = await EnsureModelFileAsync(modelConfig);

            // Load the model
            var modelInstance = await LoadModelAsync(modelConfig, modelPath);
            
            // Cache the loaded model
            _loadedModels.TryAdd(modelName, modelInstance);
            
            return modelInstance;
        }

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
                        _logger.LogInformation("Downloading {ModelName}: {Progress:F1}% ({DownloadedMB:F1}/{TotalMB:F1} MB)", 
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

        private Task<ModelInstance> LoadModelAsync(ModelConfiguration config, string modelPath)
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
                var executor = new InteractiveExecutor(context);

                var modelInstance = new ModelInstance
                {
                    Name = config.Name,
                    Weights = weights,
                    Context = context,
                    Executor = executor,
                    Configuration = config
                };

                _logger.LogInformation("Successfully loaded model {ModelName}", config.Name);
                return Task.FromResult(modelInstance);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load model {ModelName} from {Path}", config.Name, modelPath);
                throw new InvalidOperationException($"Failed to load model {config.Name}: {ex.Message}", ex);
            }
        }

        private ModelsConfiguration LoadModelsConfiguration()
        {
            // Default configuration with popular models
            var defaultConfig = new ModelsConfiguration
            {
                Models = new List<ModelConfiguration>
                {
                    new ModelConfiguration
                    {
                        Name = "llama-3.2-3b-instruct",
                        HuggingFaceId = "bartowski/Llama-3.2-3B-Instruct-GGUF",
                        Filename = "Llama-3.2-3B-Instruct-Q4_K_M.gguf",
                        ContextSize = 4096,
                        GpuLayers = 5,
                        Description = "Llama 3.2 3B Instruct model optimized for chat",
                        SizeGB = 2.02,
                        Type = "instruct"
                    },
                    new ModelConfiguration
                    {
                        Name = "llama-3.2-1b-instruct",
                        HuggingFaceId = "bartowski/Llama-3.2-1B-Instruct-GGUF",
                        Filename = "Llama-3.2-1B-Instruct-Q4_K_M.gguf",
                        ContextSize = 4096,
                        GpuLayers = 5,
                        Description = "Llama 3.2 1B Instruct model - smaller and faster",
                        SizeGB = 0.8,
                        Type = "instruct"
                    },
                    new ModelConfiguration
                    {
                        Name = "phi-3-mini-instruct",
                        HuggingFaceId = "microsoft/Phi-3-mini-4k-instruct-gguf",
                        Filename = "Phi-3-mini-4k-instruct-q4.gguf",
                        ContextSize = 4096,
                        GpuLayers = 5,
                        Description = "Microsoft Phi-3 Mini optimized for instruction following",
                        SizeGB = 2.4,
                        Type = "instruct"
                    },
                    new ModelConfiguration
                    {
                        Name = "gemma-2-2b-instruct",
                        HuggingFaceId = "google/gemma-2-2b-it-GGUF",
                        Filename = "gemma-2-2b-it-q4_k_m.gguf",
                        ContextSize = 8192,
                        GpuLayers = 5,
                        Description = "Google Gemma 2 2B Instruct model",
                        SizeGB = 1.6,
                        Type = "instruct"
                    },
                    new ModelConfiguration
                    {
                        Name = "phi-4",
                        HuggingFaceId = "microsoft/phi-4-gguf",
                        Filename = "phi-4-Q8_0.gguf",
                        ContextSize = 32768,
                        GpuLayers = 5,
                        Description = "Phi-4 model by Microsoft",
                        SizeGB = 15.6,
                        Type = "instruct"
                    }
                }
            };

            // Try to load from configuration file if it exists
            var configPath = Path.Combine("Data", "models-config.json");
            if (File.Exists(configPath))
            {
                try
                {
                    var configJson = File.ReadAllText(configPath);
                    var loadedConfig = JsonSerializer.Deserialize<ModelsConfiguration>(configJson, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                    });
                    
                    if (loadedConfig?.Models?.Any() == true)
                    {
                        _logger.LogInformation("Loaded models configuration from {ConfigPath}", configPath);
                        return loadedConfig;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load models configuration from {ConfigPath}, using defaults", configPath);
                }
            }

            // Save default configuration for reference
            try
            {
                var defaultConfigJson = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                    WriteIndented = true
                });
                File.WriteAllText(configPath, defaultConfigJson);
                _logger.LogInformation("Created default models configuration at {ConfigPath}", configPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save default models configuration");
            }

            return defaultConfig;
        }

        public List<string> GetAvailableModels()
        {
            return _modelsConfig.Models.Select(m => m.Name).ToList();
        }

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

    public class ModelInstance : IDisposable
    {
        public string Name { get; set; } = string.Empty;
        public LLamaWeights? Weights { get; set; }
        public LLamaContext? Context { get; set; }
        public InteractiveExecutor? Executor { get; set; }
        public ModelConfiguration? Configuration { get; set; }

        public void Dispose()
        {
            Context?.Dispose();
            Weights?.Dispose();
        }
    }
}