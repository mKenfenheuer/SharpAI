using Whisper.net;
using Whisper.net.Ggml;
using System.Collections.Concurrent;
using System.Text.Json;
using Whisper.net.Logger;

namespace SharpAI.Services
{
    /// <summary>
    /// Manages Whisper speech recognition models, including downloading and loading.
    /// Supports multiple model sizes and OpenAI-compatible model naming.
    /// </summary>
    public class WhisperModelManager : IDisposable
    {
        private readonly string _modelsDirectory;
        private readonly ILogger<WhisperModelManager> _logger;
        private readonly ILogger<WhisperModelInstance> _instanceLogger;
        private readonly ConcurrentDictionary<string, WhisperModelInstance> _loadedModels = new();
        private readonly WhisperModelsConfiguration _modelsConfig;
        private readonly SemaphoreSlim _downloadSemaphore = new(1, 1);

        private IDisposable _whisperLogger;

        /// <summary>
        /// Initializes a new instance of the WhisperModelManager.
        /// </summary>
        /// <param name="logger">Logger for model manager operations</param>
        /// <param name="instanceLogger">Logger for model instance operations</param>
        public WhisperModelManager(ILogger<WhisperModelManager> logger, ILogger<WhisperModelInstance> instanceLogger)
        {
            _logger = logger;
            _modelsDirectory = Path.Combine("Data", "Models");
            Directory.CreateDirectory(_modelsDirectory);

            _modelsConfig = LoadWhisperModelsConfiguration();
            _instanceLogger = instanceLogger;

            _whisperLogger = LogProvider.AddLogger((l, s) =>
            {
                switch (l)
                {
                    case WhisperLogLevel.Debug:
                        _instanceLogger.LogDebug(s);
                        break;
                    case WhisperLogLevel.Info:
                        _instanceLogger.LogInformation(s);
                        break;
                    case WhisperLogLevel.Warning:
                        _instanceLogger.LogWarning(s);
                        break;
                    case WhisperLogLevel.Error:
                        _instanceLogger.LogError(s);
                        break;
                    default:
                        _instanceLogger.LogInformation(s);
                        break;
                }
            });
            _instanceLogger = instanceLogger;
        }

    /// <summary>
    /// Retrieves a Whisper model instance, downloading and loading it if necessary.
    /// </summary>
    /// <param name="modelName">Requested model name (supports OpenAI-style and internal names)</param>
    /// <returns>Loaded WhisperModelInstance</returns>
    public async Task<WhisperModelInstance> GetModelAsync(string modelName)
        {
            // Normalize model name - handle OpenAI naming convention
            var normalizedModelName = NormalizeModelName(modelName);

            // Check if model is already loaded
            if (_loadedModels.TryGetValue(normalizedModelName, out var existingModel))
            {
                return existingModel;
            }

            // Find model configuration
            var modelConfig = _modelsConfig.Models.FirstOrDefault(m =>
                m.Name.Equals(normalizedModelName, StringComparison.OrdinalIgnoreCase));

            if (modelConfig == null)
            {
                throw new ArgumentException($"Whisper model '{modelName}' is not configured. Available models: {string.Join(", ", _modelsConfig.Models.Select(m => m.Name))}");
            }

            // Ensure model file exists (download if needed)
            var modelPath = await EnsureModelFileAsync(modelConfig);

            // Load the model
            var modelInstance = await LoadModelAsync(modelConfig, modelPath);

            // Cache the loaded model
            _loadedModels.TryAdd(normalizedModelName, modelInstance);

            return modelInstance;
        }

        /// <summary>
        /// Normalizes model names to handle OpenAI API naming conventions.
        /// </summary>
        /// <param name="modelName">Original model name</param>
        /// <returns>Normalized internal model name</returns>
        private string NormalizeModelName(string modelName)
        {
            // Handle OpenAI API model names (whisper-1 -> base)
            return modelName.ToLower() switch
            {
                "whisper-1" => "base",
                "whisper-tiny" => "tiny",
                "whisper-base" => "base",
                "whisper-small" => "small",
                "whisper-medium" => "medium",
                "whisper-large" => "large-v3",
                "whisper-large-v1" => "large-v1",
                "whisper-large-v2" => "large-v2",
                "whisper-large-v3" => "large-v3",
                "whisper-large-v3-turbo" => "large-v3-turbo",
                _ => modelName.ToLower()
            };
        }

        /// <summary>
        /// Ensures a Whisper model file exists locally, downloading it if necessary.
        /// </summary>
        /// <param name="config">Whisper model configuration</param>
        /// <returns>Path to the local model file</returns>
        private async Task<string> EnsureModelFileAsync(WhisperModelConfiguration config)
        {
            var modelPath = Path.Combine(_modelsDirectory, config.Filename);

            if (File.Exists(modelPath))
            {
                _logger.LogInformation("Whisper model {ModelName} already exists at {ModelPath}", config.Name, modelPath);
                return modelPath;
            }

            _logger.LogInformation("Whisper model {ModelName} not found locally. Downloading {GgmlType}",
                config.Name, config.GgmlType);

            await _downloadSemaphore.WaitAsync();
            try
            {
                // Double-check after acquiring the semaphore
                if (File.Exists(modelPath))
                {
                    return modelPath;
                }

                await DownloadWhisperModelAsync(config, modelPath);
                return modelPath;
            }
            finally
            {
                _downloadSemaphore.Release();
            }
        }

        /// <summary>
        /// Downloads a Whisper model using the official Whisper.net downloader.
        /// </summary>
        /// <param name="config">Whisper model configuration</param>
        /// <param name="destinationPath">Local path to save the model</param>
        private async Task DownloadWhisperModelAsync(WhisperModelConfiguration config, string destinationPath)
        {
            _logger.LogInformation("Downloading Whisper model {ModelName} ({GgmlType}) - Size: ~{SizeMB} MB",
                config.Name, config.GgmlType, config.SizeMB);

            try
            {
                using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(config.GgmlType);
                using var fileWriter = File.Create(destinationPath);

                var buffer = new byte[8192];
                var totalBytes = 0L;
                var lastLogTime = DateTime.UtcNow;

                while (true)
                {
                    var bytesRead = await modelStream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    await fileWriter.WriteAsync(buffer, 0, bytesRead);
                    totalBytes += bytesRead;

                        // Log progress every 30 seconds
                        if (DateTime.UtcNow - lastLogTime > TimeSpan.FromSeconds(30))
                        {
                            _logger.LogDebug("Downloading Whisper {ModelName}: {DownloadedMB:F1} MB downloaded",
                                config.Name, totalBytes / 1024.0 / 1024.0);
                            lastLogTime = DateTime.UtcNow;
                        }
                }

                _logger.LogInformation("Successfully downloaded Whisper model {ModelName} to {Path} ({TotalMB:F1} MB)",
                    config.Name, destinationPath, totalBytes / 1024.0 / 1024.0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download Whisper model {ModelName}", config.Name);

                // Clean up partial file
                if (File.Exists(destinationPath))
                {
                    try { File.Delete(destinationPath); } catch { }
                }

                throw new InvalidOperationException($"Failed to download Whisper model {config.Name}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Loads a Whisper model from a local file into memory.
        /// </summary>
        /// <param name="config">Whisper model configuration</param>
        /// <param name="modelPath">Path to the local model file</param>
        /// <returns>Loaded Whisper model instance</returns>
        private async Task<WhisperModelInstance> LoadModelAsync(WhisperModelConfiguration config, string modelPath)
        {
            _logger.LogInformation("Loading Whisper model {ModelName} from {Path}", config.Name, modelPath);

            try
            {
                var factory = WhisperFactory.FromPath(modelPath,
                    new WhisperFactoryOptions
                    {
                        UseFlashAttention = true,
                        UseGpu = true
                    }
                );

                var modelInstance = new WhisperModelInstance
                {
                    Name = config.Name,
                    Factory = factory,
                    Configuration = config,
                };

                _logger.LogInformation("Successfully loaded Whisper model {ModelName}", config.Name);
                return await Task.FromResult(modelInstance);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load Whisper model {ModelName} from {Path}", config.Name, modelPath);
                throw new InvalidOperationException($"Failed to load Whisper model {config.Name}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Loads Whisper model configuration with all available model sizes.
        /// </summary>
        /// <returns>Configuration with available Whisper models</returns>
        private WhisperModelsConfiguration LoadWhisperModelsConfiguration()
        {
            // Default configuration with different Whisper model sizes
            var defaultConfig = new WhisperModelsConfiguration
            {
                Models = new List<WhisperModelConfiguration>
                {
                    new WhisperModelConfiguration
                    {
                        Name = "medium",
                        GgmlType = GgmlType.Medium,
                        Filename = "ggml-medium.bin",
                        Description = "High accuracy, slower processing",
                        SizeMB = 1520,
                        Speed = "Slow",
                        Accuracy = "High"
                    }
                }
            };

            return defaultConfig;
        }

        /// <summary>
        /// Gets list of all available Whisper model names.
        /// </summary>
        /// <returns>List of available model names</returns>
        public List<string> GetAvailableModels()
        {
            return _modelsConfig.Models.Select(m => m.Name).ToList();
        }

        /// <summary>
        /// Unloads a Whisper model from memory and disposes resources.
        /// </summary>
        /// <param name="modelName">Name of the model to unload</param>
        public void UnloadModel(string modelName)
        {
            var normalizedName = NormalizeModelName(modelName);
            if (_loadedModels.TryRemove(normalizedName, out var model))
            {
                model.Dispose();
                _logger.LogInformation("Unloaded Whisper model {ModelName}", normalizedName);
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
            _whisperLogger?.Dispose();
        }
    }

    /// <summary>
    /// Configuration for a specific Whisper model variant.
    /// </summary>
    public class WhisperModelConfiguration
    {
        public string Name { get; set; } = string.Empty;
        public GgmlType GgmlType { get; set; }
        public string Filename { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int SizeMB { get; set; }
        public string Speed { get; set; } = string.Empty;
        public string Accuracy { get; set; } = string.Empty;
    }

    /// <summary>
    /// Configuration container for all available Whisper models.
    /// </summary>
    public class WhisperModelsConfiguration
    {
        public List<WhisperModelConfiguration> Models { get; set; } = new List<WhisperModelConfiguration>();
    }

    /// <summary>
    /// Represents a loaded Whisper model instance with factory and configuration.
    /// </summary>
    public class WhisperModelInstance : IDisposable
    {
        public string Name { get; set; } = string.Empty;
        public WhisperFactory? Factory { get; set; }
        public WhisperModelConfiguration? Configuration { get; set; }

        public void Dispose()
        {
            Factory?.Dispose();
        }
    }
}