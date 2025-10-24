using Whisper.net;
using Whisper.net.Ggml;
using System.Collections.Concurrent;
using System.Text.Json;
using Whisper.net.Logger;

namespace SharpAI.Services
{
    public class WhisperModelManager : IDisposable
    {
        private readonly string _modelsDirectory;
        private readonly ILogger<WhisperModelManager> _logger;
        private readonly ILogger<WhisperModelInstance> _instanceLogger;
        private readonly ConcurrentDictionary<string, WhisperModelInstance> _loadedModels = new();
        private readonly WhisperModelsConfiguration _modelsConfig;
        private readonly SemaphoreSlim _downloadSemaphore = new(1, 1);

        private IDisposable _whisperLogger;

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
                _ => modelName.ToLower()
            };
        }

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
                        _logger.LogInformation("Downloading Whisper {ModelName}: {DownloadedMB:F1} MB downloaded",
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

        private WhisperModelsConfiguration LoadWhisperModelsConfiguration()
        {
            // Default configuration with different Whisper model sizes
            var defaultConfig = new WhisperModelsConfiguration
            {
                Models = new List<WhisperModelConfiguration>
                {
                    new WhisperModelConfiguration
                    {
                        Name = "tiny",
                        GgmlType = GgmlType.Tiny,
                        Filename = "ggml-tiny.bin",
                        Description = "Fastest, lowest accuracy - Good for real-time applications",
                        SizeMB = 39,
                        Speed = "Very Fast",
                        Accuracy = "Low"
                    },
                    new WhisperModelConfiguration
                    {
                        Name = "base",
                        GgmlType = GgmlType.Base,
                        Filename = "ggml-base.bin",
                        Description = "Balanced speed and accuracy - Default model",
                        SizeMB = 142,
                        Speed = "Fast",
                        Accuracy = "Good"
                    },
                    new WhisperModelConfiguration
                    {
                        Name = "small",
                        GgmlType = GgmlType.Small,
                        Filename = "ggml-small.bin",
                        Description = "Good accuracy, moderate speed",
                        SizeMB = 466,
                        Speed = "Medium",
                        Accuracy = "Better"
                    },
                    new WhisperModelConfiguration
                    {
                        Name = "medium",
                        GgmlType = GgmlType.Medium,
                        Filename = "ggml-medium.bin",
                        Description = "High accuracy, slower processing",
                        SizeMB = 1520,
                        Speed = "Slow",
                        Accuracy = "High"
                    },
                    new WhisperModelConfiguration
                    {
                        Name = "large-v1",
                        GgmlType = GgmlType.LargeV1,
                        Filename = "ggml-large-v1.bin",
                        Description = "Highest accuracy, slowest processing - Original large model",
                        SizeMB = 2900,
                        Speed = "Very Slow",
                        Accuracy = "Highest"
                    },
                    new WhisperModelConfiguration
                    {
                        Name = "large-v2",
                        GgmlType = GgmlType.LargeV2,
                        Filename = "ggml-large-v2.bin",
                        Description = "Highest accuracy, slowest processing - Improved large model",
                        SizeMB = 2900,
                        Speed = "Very Slow",
                        Accuracy = "Highest"
                    },
                    new WhisperModelConfiguration
                    {
                        Name = "large-v3",
                        GgmlType = GgmlType.LargeV3,
                        Filename = "ggml-large-v3.bin",
                        Description = "Latest and most accurate model - Best quality",
                        SizeMB = 2900,
                        Speed = "Very Slow",
                        Accuracy = "Best"
                    },
                    new WhisperModelConfiguration
                    {
                        Name = "large-v3-turbo",
                        GgmlType = GgmlType.LargeV3,
                        Filename = "ggml-large-v3-turbo.bin",
                        Description = "Latest and most accurate model - Best quality (Turbo)",
                        SizeMB = 1700,
                        Speed = "Slow",
                        Accuracy = "Best"
                    }
                }
            };

            // Try to load from configuration file if it exists
            var configPath = Path.Combine("Data", "whisper-models-config.json");
            if (File.Exists(configPath))
            {
                try
                {
                    var configJson = File.ReadAllText(configPath);
                    var loadedConfig = JsonSerializer.Deserialize<WhisperModelsConfiguration>(configJson, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                    });

                    if (loadedConfig?.Models?.Any() == true)
                    {
                        _logger.LogInformation("Loaded Whisper models configuration from {ConfigPath}", configPath);
                        return loadedConfig;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load Whisper models configuration from {ConfigPath}, using defaults", configPath);
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
                _logger.LogInformation("Created default Whisper models configuration at {ConfigPath}", configPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save default Whisper models configuration");
            }

            return defaultConfig;
        }

        public List<string> GetAvailableModels()
        {
            return _modelsConfig.Models.Select(m => m.Name).ToList();
        }

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

    public class WhisperModelsConfiguration
    {
        public List<WhisperModelConfiguration> Models { get; set; } = new List<WhisperModelConfiguration>();
    }

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