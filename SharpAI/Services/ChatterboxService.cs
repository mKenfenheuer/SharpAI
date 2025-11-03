using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using SharpAI.Models;
using Microsoft.AspNetCore.Hosting;
using SharpAI.Services;
using System.Net.Http.Headers;
using System.Linq;

namespace SharpAI
{
    // DTOs for TTS API communication
    public class TtsApiRequest
    {
        public string Text { get; set; } = string.Empty;
        public string? LanguageId { get; set; }
        public float CfgWeight { get; set; } = 0.5f;
        public float Exaggeration { get; set; } = 0.5f;
        public string OutputFormat { get; set; } = "wav";
    }

    public class TtsApiResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? LanguageDetected { get; set; }
    }
    public class ChatterboxService : BackgroundService
    {
        private readonly HttpClient _httpClient;
        private readonly PythonApiHostedService _pythonApiService;
        private bool _initialized = false;
        private readonly Channel<TtsInternalRequest> _requestChannel;
        private readonly ChannelWriter<TtsInternalRequest> _requestWriter;
        private readonly ChannelReader<TtsInternalRequest> _requestReader;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<MemoryStream>> _pendingRequests;
        private readonly ILogger<ChatterboxService> _logger;
        private readonly IWebHostEnvironment _environment;

        // Supported audio response formats
        private static readonly HashSet<string> SupportedResponseFormats = new HashSet<string>
        {
            "mp3", "wav"
        };

        public ChatterboxService(
            ILogger<ChatterboxService> logger, 
            IWebHostEnvironment environment,
            PythonApiHostedService pythonApiService,
            HttpClient httpClient)
        {
            _logger = logger;
            _environment = environment;
            _httpClient = httpClient;
            _pythonApiService = pythonApiService;            var options = new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            };

            _requestChannel = Channel.CreateBounded<TtsInternalRequest>(options);
            _requestWriter = _requestChannel.Writer;
            _requestReader = _requestChannel.Reader;
            _pendingRequests = new ConcurrentDictionary<string, TaskCompletionSource<MemoryStream>>();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await WaitForPythonApiAsync(stoppingToken);

                // Warm up the model with a test request if we are in production
                if (_environment.IsProduction())
                {
                    await WarmUpModel();
                }

                _logger.LogInformation("ChatterboxService started, processing TTS requests...");

                await foreach (var request in _requestReader.ReadAllAsync(stoppingToken))
                {
                    try
                    {
                        var result = await ProcessTtsRequestAsync(request);

                        if (_pendingRequests.TryRemove(request.RequestId, out var tcs))
                        {
                            tcs.SetResult(result);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing TTS request {RequestId}", request.RequestId);

                        if (_pendingRequests.TryRemove(request.RequestId, out var tcs))
                        {
                            tcs.SetException(ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error in ChatterboxService");
            }
        }

        private async Task WaitForPythonApiAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Waiting for Python API to become available...");

            var timeout = TimeSpan.FromMinutes(2);
            var startTime = DateTime.UtcNow;

            while (DateTime.UtcNow - startTime < timeout && !stoppingToken.IsCancellationRequested)
            {
                if (_pythonApiService.IsHealthy)
                {
                    _initialized = true;
                    _logger.LogInformation("Python API is ready");
                    return;
                }

                await Task.Delay(1000, stoppingToken);
            }

            if (stoppingToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("Startup cancelled");
            }

            throw new TimeoutException("Python API failed to become available within timeout period");
        }

        private async Task WarmUpModel()
        {
            try
            {
                _logger.LogInformation("Warming up ChatterboxService TTS model...");

                if (!_initialized)
                {
                    _logger.LogWarning("ChatterboxService not initialized yet, skipping warmup");
                    return;
                }

                var warmupUrl = $"{_pythonApiService.ApiBaseUrl}/warmup";
                var response = await _httpClient.PostAsync(warmupUrl, null);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<JsonElement>(content);

                    if (result.TryGetProperty("success", out var success) && success.GetBoolean())
                    {
                        _logger.LogInformation("ChatterboxService TTS model warmed up successfully");
                    }
                    else
                    {
                        _logger.LogWarning("Warmup returned false, but continuing anyway");
                    }
                }
                else
                {
                    _logger.LogWarning($"Warmup failed with status: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to warm up ChatterboxService TTS model, but continuing anyway");
            }
        }

        private async Task<MemoryStream> ProcessTtsRequestAsync(TtsInternalRequest request)
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("ChatterboxService not initialized");
            }

            _logger.LogDebug("Processing TTS request for text: {Text}...",
                request.Text.Length > 50 ? request.Text[..50] + "..." : request.Text);

            try
            {
                // Validate response format
                if (string.IsNullOrEmpty(request.ResponseFormat) ||
                    !SupportedResponseFormats.Contains(request.ResponseFormat.ToLower()))
                {
                    _logger.LogWarning("Response format '{Format}' not supported, defaulting to 'wav'", request.ResponseFormat);
                    request.ResponseFormat = "wav";
                }

                // Use exaggeration as emotion intensity (ChatterboxMultilingualTTS parameter)
                float actualExaggeration = request.Exaggeration;
                float actualCfgWeight = request.CfgWeight;

                // If emotion_intensity is provided and differs from default, use it as exaggeration
                if (request.EmotionIntensity != 0.5f)
                {
                    actualExaggeration = request.EmotionIntensity;
                    _logger.LogDebug("Using emotion_intensity ({EmotionIntensity}) as exaggeration parameter", request.EmotionIntensity);
                }

                // Auto-adjust cfg_weight based on exaggeration level for optimal results
                if (actualExaggeration > 0.6f)
                {
                    actualCfgWeight = Math.Min(actualCfgWeight, 0.3f);
                    _logger.LogDebug("High exaggeration detected ({Exaggeration}), adjusting cfg_weight to {CfgWeight}", actualExaggeration, actualCfgWeight);
                }
                else if (actualExaggeration < 0.4f)
                {
                    actualCfgWeight = Math.Max(actualCfgWeight, 0.5f);
                    _logger.LogDebug("Low exaggeration detected ({Exaggeration}), using cfg_weight {CfgWeight}", actualExaggeration, actualCfgWeight);
                }

                // Call the Python API (language detection is now handled by Python API)
                var audioBytes = await CallTtsApiAsync(new TtsApiRequest
                {
                    Text = request.Text,
                    LanguageId = request.LanguageId, // Can be null - Python API will auto-detect
                    CfgWeight = actualCfgWeight * 0.9f, // Slightly reduce cfg_weight for Chatterbox
                    Exaggeration = actualExaggeration,
                    OutputFormat = request.ResponseFormat
                });

                var stream = new MemoryStream(audioBytes);
                stream.Position = 0;

                _logger.LogDebug("TTS synthesis completed successfully. Generated {Bytes} bytes", audioBytes.Length);
                return stream;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ProcessTtsRequestAsync: {Message}", ex.Message);
                throw;
            }
        }

        private async Task<byte[]> CallTtsApiAsync(TtsApiRequest request)
        {
            try
            {
                var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                var content = new StringContent(json, Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
                var response = await _httpClient.PostAsync($"{_pythonApiService.ApiBaseUrl}/synthesize", content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new HttpRequestException($"TTS API call failed: {response.StatusCode} - {errorContent}");
                }

                // Log detected language if available in response headers
                if (response.Headers.TryGetValues("X-Detected-Language", out var detectedLanguageValues))
                {
                    var detectedLanguage = detectedLanguageValues.FirstOrDefault();
                    if (!string.IsNullOrEmpty(detectedLanguage))
                    {
                        _logger.LogDebug("Python API detected language: {Language}", detectedLanguage);
                    }
                }

                // The API now returns audio bytes directly
                var audioBytes = await response.Content.ReadAsByteArrayAsync();
                return audioBytes;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling TTS API");
                throw;
            }
        }

        /// <summary>
        /// Public method to queue a TTS request and get the result asynchronously
        /// </summary>
        public async Task<MemoryStream> SynthesizeToStreamAsync(
            string text,
            string languageId = null,
            float emotionIntensity = 0.5f,
            string responseFormat = null,
            string referenceAudioPath = null)
        {
            return await SynthesizeToStreamAsync(text, languageId, emotionIntensity, referenceAudioPath, responseFormat, 0.5f, 0.5f);
        }

        /// <summary>
        /// Public method to queue a TTS request with advanced parameters and get the result asynchronously
        /// </summary>
        public async Task<MemoryStream> SynthesizeToStreamAsync(
            string text,
            string languageId = null,
            float emotionIntensity = 0.5f,
            string referenceAudioPath = null,
            string responseFormat = "wav",
            float cfgWeight = 0.5f,
            float exaggeration = 0.5f)
        {
            var requestId = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<MemoryStream>();

            var request = new TtsInternalRequest
            {
                RequestId = requestId,
                Text = text,
                LanguageId = languageId,
                EmotionIntensity = emotionIntensity,
                ReferenceAudioPath = referenceAudioPath,
                CfgWeight = cfgWeight,
                Exaggeration = exaggeration,
                ResponseFormat = responseFormat
            };

            _pendingRequests[requestId] = tcs;

            try
            {
                await _requestWriter.WriteAsync(request);
                return await tcs.Task;
            }
            catch
            {
                _pendingRequests.TryRemove(requestId, out _);
                throw;
            }
        }

        public override void Dispose()
        {
            try
            {
                _requestWriter.Complete();
                _logger.LogInformation("ChatterboxService disposed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during disposal");
            }

            base.Dispose();
        }
    }
}

