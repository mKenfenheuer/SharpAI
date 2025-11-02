using Python.Runtime;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using SharpAI.Models;
using Microsoft.AspNetCore.Hosting;

namespace SharpAI
{
    public class ChatterboxService : BackgroundService
    {
        private dynamic _model;
        private dynamic _langdetect;
        private bool _initialized = false;
        private readonly Channel<TtsInternalRequest> _requestChannel;
        private readonly ChannelWriter<TtsInternalRequest> _requestWriter;
        private readonly ChannelReader<TtsInternalRequest> _requestReader;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<MemoryStream>> _pendingRequests;
        private readonly ILogger<ChatterboxService> _logger;
        private readonly IWebHostEnvironment _environment;

        // Supported languages by ChatterboxMultilingualTTS
        private static readonly HashSet<string> SupportedLanguages = new HashSet<string>
        {
            "ar", "da", "de", "el", "en", "es", "fi", "fr", "he", "hi",
            "it", "ja", "ko", "ms", "nl", "no", "pl", "pt", "ru", "sv",
            "sw", "tr", "zh"
        };

        // Supported audio response formats
        private static readonly HashSet<string> SupportedResponseFormats = new HashSet<string>
        {
            "mp3", "wav"
        };

        public ChatterboxService(ILogger<ChatterboxService> logger, IWebHostEnvironment environment)
        {
            _logger = logger;
            _environment = environment;
            var options = new BoundedChannelOptions(100)
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
                await InitializePythonAndModels();
                
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
                        var result = ProcessTtsRequest(request);
                        
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

        private async Task InitializePythonAndModels()
        {
            await Task.Run(() =>
            {
                try
                {
                    if (!PythonEngine.IsInitialized)
                    {
                        // Set Python.NET to use automatic thread state management
                        PythonEngine.Initialize();
                        PythonEngine.BeginAllowThreads();
                        _logger.LogInformation("Python engine initialized with thread support");
                    }

                    using (Py.GIL())
                    {
                        // Capture Python output for logging
                        dynamic sys = Py.Import("sys");
                        dynamic io = Py.Import("io");
                        dynamic stdout_capture = io.StringIO();
                        dynamic stderr_capture = io.StringIO();
                        dynamic original_stdout = sys.stdout;
                        dynamic original_stderr = sys.stderr;
                        
                        try
                        {
                            // Redirect stdout and stderr to capture output
                            sys.stdout = stdout_capture;
                            sys.stderr = stderr_capture;
                            
                            // Load multilingual TTS model
                            dynamic mtl = Py.Import("chatterbox.mtl_tts");
                            _model = mtl.ChatterboxMultilingualTTS.from_pretrained(device: "cuda");
                            _logger.LogInformation("Chatterbox Multilingual TTS loaded successfully on CUDA");

                            // Load language detection library
                            _langdetect = Py.Import("langdetect");
                            _logger.LogInformation("Language detection module initialized");
                        }
                        finally
                        {
                            // Restore original stdout and stderr
                            sys.stdout = original_stdout;
                            sys.stderr = original_stderr;
                            
                            // Log captured output
                            string capturedStdout = stdout_capture.getvalue().ToString();
                            string capturedStderr = stderr_capture.getvalue().ToString();
                            
                            if (!string.IsNullOrWhiteSpace(capturedStdout))
                            {
                                _logger.LogDebug("Python stdout during initialization: {Output}", capturedStdout.Trim());
                            }
                            
                            if (!string.IsNullOrWhiteSpace(capturedStderr))
                            {
                                _logger.LogDebug("Python stderr during initialization: {Output}", capturedStderr.Trim());
                            }
                        }
                    }

                    _initialized = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to initialize Python engine and models");
                    throw;
                }
            });
        }

        private async Task WarmUpModel()
        {
            try
            {
                _logger.LogInformation("Warming up ChatterboxService TTS model...");
                
                // Wait a bit to ensure Python initialization is complete
                await Task.Delay(100);
                
                if (!_initialized)
                {
                    _logger.LogWarning("ChatterboxService not initialized yet, skipping warmup");
                    return;
                }
                
                var warmupRequest = new TtsInternalRequest
                {
                    RequestId = "warmup",
                    Text = "Hello, this is a warmup test.",
                    LanguageId = "en",
                    EmotionIntensity = 0.5f,
                    CfgWeight = 0.5f,
                    Exaggeration = 0.5f,
                    ResponseFormat = "wav"
                };

                var warmupResult = ProcessTtsRequest(warmupRequest);
                warmupResult?.Dispose(); // Clean up the test audio

                _logger.LogInformation("ChatterboxService TTS model warmed up successfully");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to warm up ChatterboxService TTS model, but continuing anyway");
            }
        }

        private MemoryStream ProcessTtsRequest(TtsInternalRequest request)
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("ChatterboxService not initialized");
            }

            _logger.LogDebug("Processing TTS request for text: {Text}...", 
                request.Text.Length > 50 ? request.Text[..50] + "..." : request.Text);

            try
            {
                using (Py.GIL())
                {
                    // Auto detect language if not specified
                    string languageId = request.LanguageId;
                    if (string.IsNullOrEmpty(languageId))
                    {
                        try
                        {
                            languageId = _langdetect.detect(request.Text).ToString();
                            _logger.LogDebug("Auto-detected language: {Language}", languageId);
                        }
                        catch (PythonException ex)
                        {
                            _logger.LogWarning("Language detection failed: {Error}, defaulting to English", ex.Message);
                            languageId = "en";
                        }
                    }

                    // Validate and adjust language if needed
                    if (!SupportedLanguages.Contains(languageId))
                    {
                        _logger.LogWarning("Language '{Language}' not supported, defaulting to English", languageId);
                        languageId = "en";
                    }

                    // Validate response format
                    if (string.IsNullOrEmpty(request.ResponseFormat) ||
                        !SupportedResponseFormats.Contains(request.ResponseFormat.ToLower()))
                    {
                        _logger.LogWarning("Response format '{Format}' not supported, defaulting to 'wav'", request.ResponseFormat);
                        request.ResponseFormat = "wav";
                    }

                    dynamic wav;

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
                    // Higher exaggeration (dramatic speech) -> lower cfg_weight for better pacing
                    if (actualExaggeration > 0.6f)
                    {
                        // Dramatic/expressive speech: use lower cfg_weight (~0.3)
                        actualCfgWeight = Math.Min(actualCfgWeight, 0.3f);
                        _logger.LogDebug("High exaggeration detected ({Exaggeration}), adjusting cfg_weight to {CfgWeight} for dramatic speech", actualExaggeration, actualCfgWeight);
                    }
                    else if (actualExaggeration < 0.4f)
                    {
                        // Low exaggeration: can use higher cfg_weight for more control
                        actualCfgWeight = Math.Max(actualCfgWeight, 0.5f);
                        _logger.LogDebug("Low exaggeration detected ({Exaggeration}), using cfg_weight {CfgWeight} for controlled speech", actualExaggeration, actualCfgWeight);
                    }

                    // Capture Python output during TTS generation
                    dynamic sys = Py.Import("sys");
                    dynamic io = Py.Import("io");
                    dynamic stdout_capture = io.StringIO();
                    dynamic stderr_capture = io.StringIO();
                    dynamic original_stdout = sys.stdout;
                    dynamic original_stderr = sys.stderr;
                    
                    string tempPath;
                    try
                    {
                        // Redirect stdout and stderr to capture output
                        sys.stdout = stdout_capture;
                        sys.stderr = stderr_capture;

                        // Optional voice cloning
                        if (!string.IsNullOrEmpty(request.ReferenceAudioPath))
                        {
                            _logger.LogDebug("Using reference audio for voice cloning: {Path}", request.ReferenceAudioPath);
                            dynamic librosa = Py.Import("librosa");
                            dynamic result = librosa.load(request.ReferenceAudioPath, sr: null);
                            dynamic refWav = result[0];

                            wav = _model.generate(
                                request.Text,
                                language_id: languageId,
                                reference_wav: refWav,
                                cfg_weight: actualCfgWeight,
                                exaggeration: actualExaggeration
                            );
                        }
                        else
                        {
                            wav = _model.generate(
                                request.Text,
                                language_id: languageId,
                                cfg_weight: actualCfgWeight,
                                exaggeration: actualExaggeration
                            );
                        }

                        // Convert to WAV memory stream
                        dynamic torchaudio = Py.Import("torchaudio");
                        tempPath = Path.GetTempFileName();
                        tempPath = Path.ChangeExtension(tempPath, "." + request.ResponseFormat);
                        torchaudio.save(tempPath, wav, _model.sr);
                    }
                    finally
                    {
                        // Restore original stdout and stderr
                        sys.stdout = original_stdout;
                        sys.stderr = original_stderr;
                        
                        // Log captured output
                        string capturedStdout = stdout_capture.getvalue().ToString();
                        string capturedStderr = stderr_capture.getvalue().ToString();
                        
                        if (!string.IsNullOrWhiteSpace(capturedStdout))
                        {
                            _logger.LogDebug("Python stdout during TTS generation: {Output}", capturedStdout.Trim());
                        }
                        
                        if (!string.IsNullOrWhiteSpace(capturedStderr))
                        {
                            _logger.LogDebug("Python stderr during TTS generation: {Output}", capturedStderr.Trim());
                        }
                    }

                    var bytes = File.ReadAllBytes(tempPath);
                    File.Delete(tempPath); // Clean up temp file
                    var stream = new MemoryStream(bytes);
                    stream.Position = 0;

                    _logger.LogDebug("TTS synthesis completed successfully. Generated {Bytes} bytes", bytes.Length);
                    return stream;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ProcessTtsRequest: {Message}", ex.Message);
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
                
                if (_initialized)
                {
                    _logger.LogInformation("Shutting down Python engine...");
                    try
                    {
                        // Shutdown Python engine
                        PythonEngine.Shutdown();
                        _logger.LogInformation("Python engine shut down successfully");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error shutting down Python engine: {Message}", ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during disposal");
            }
            
            base.Dispose();
        }
    }
}
