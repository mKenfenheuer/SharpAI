using Whisper.net;
using Whisper.net.Ggml;
using SharpAI.Models;
using System.Text;
using System.Text.Json;
using FFMpegCore;
using FFMpegCore.Enums;
using Whisper.net.Logger;
using System.Threading.Channels;
using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Hosting;

namespace SharpAI.Services
{
    /// <summary>
    /// Represents an audio segment with start and end timestamps
    /// </summary>
    public class AudioSegment
    {
        public double StartTime { get; set; }
        public double EndTime { get; set; }
        public double Duration => EndTime - StartTime;
    }

    /// <summary>
    /// Hosted service for audio transcription using Whisper speech recognition models.
    /// Accepts requests asynchronously but processes them sequentially.
    /// Supports multiple response formats, streaming transcription, and audio format conversion.
    /// </summary>
    public class WhisperHostedService : BackgroundService
    {
        private readonly WhisperModelManager _modelManager;
        private readonly ILogger<WhisperHostedService> _logger;
        private readonly IWebHostEnvironment _environment;
        private readonly Channel<WhisperInternalRequest> _requestChannel;
        private readonly ChannelWriter<WhisperInternalRequest> _requestWriter;
        private readonly ChannelReader<WhisperInternalRequest> _requestReader;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<WhisperInternalResponse>> _pendingRequests;

        /// <summary>
        /// Initializes a new instance of the WhisperHostedService.
        /// </summary>
        /// <param name="modelManager">Whisper model manager for loading models</param>
        /// <param name="logger">Logger for service operations</param>
        /// <param name="environment">Web host environment to determine if running in production</param>
        public WhisperHostedService(WhisperModelManager modelManager, ILogger<WhisperHostedService> logger, IWebHostEnvironment environment)
        {
            _modelManager = modelManager;
            _logger = logger;
            _environment = environment;

            var options = new BoundedChannelOptions(50)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            };

            _requestChannel = System.Threading.Channels.Channel.CreateBounded<WhisperInternalRequest>(options);
            _requestWriter = _requestChannel.Writer;
            _requestReader = _requestChannel.Reader;
            _pendingRequests = new ConcurrentDictionary<string, TaskCompletionSource<WhisperInternalResponse>>();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // Warm up the model with a test request if we are in production
                if (_environment.IsProduction())
                {
                    await WarmUpModel();
                }

                _logger.LogInformation("WhisperHostedService started, processing transcription requests...");

                await foreach (var request in _requestReader.ReadAllAsync(stoppingToken))
                {
                    try
                    {
                        var result = await ProcessTranscriptionRequestAsync(request);

                        if (_pendingRequests.TryRemove(request.RequestId, out var tcs))
                        {
                            tcs.SetResult(result);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing transcription request {RequestId}", request.RequestId);

                        if (_pendingRequests.TryRemove(request.RequestId, out var tcs))
                        {
                            tcs.SetException(ex);
                        }
                    }
                    finally
                    {
                        // Dispose of the audio stream to free memory
                        request.AudioStream?.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error in WhisperHostedService");
            }
        }

        private async Task WarmUpModel()
        {
            try
            {
                _logger.LogInformation("Warming up WhisperHostedService model...");

                // Create a minimal WAV file for testing (1 second of silence at 16kHz)
                var sampleRate = 16000;
                var duration = 1.0; // 1 second
                var samples = (int)(sampleRate * duration);
                var audioData = new byte[44 + samples * 2]; // WAV header + 16-bit samples

                // Write WAV header
                var header = new byte[]
                {
                    0x52, 0x49, 0x46, 0x46, // "RIFF"
                    0x00, 0x00, 0x00, 0x00, // File size (will be filled later)
                    0x57, 0x41, 0x56, 0x45, // "WAVE"
                    0x66, 0x6D, 0x74, 0x20, // "fmt "
                    0x10, 0x00, 0x00, 0x00, // Format chunk size
                    0x01, 0x00,             // Audio format (PCM)
                    0x01, 0x00,             // Channels (1)
                    0x80, 0x3E, 0x00, 0x00, // Sample rate (16000)
                    0x00, 0x7D, 0x00, 0x00, // Byte rate (16000 * 1 * 2)
                    0x02, 0x00,             // Block align (1 * 2)
                    0x10, 0x00,             // Bits per sample (16)
                    0x64, 0x61, 0x74, 0x61, // "data"
                    0x00, 0x00, 0x00, 0x00  // Data size (will be filled later)
                };

                Array.Copy(header, audioData, header.Length);

                // Fill file size and data size
                var fileSize = audioData.Length - 8;
                var dataSize = samples * 2;
                BitConverter.GetBytes(fileSize).CopyTo(audioData, 4);
                BitConverter.GetBytes(dataSize).CopyTo(audioData, 40);

                using var audioStream = new MemoryStream(audioData);

                var warmupRequest = new AudioTranscriptionRequest
                {
                    Model = "whisper-medium",
                    Language = "en",
                    ResponseFormat = AudioResponseFormat.Text
                };

                var warmupInternalRequest = new WhisperInternalRequest
                {
                    RequestId = "warmup",
                    AudioStream = audioStream,
                    TranscriptionRequest = warmupRequest,
                    IsStreaming = false
                };

                var warmupResult = await ProcessTranscriptionRequestAsync(warmupInternalRequest);

                if (warmupResult.Error != null)
                {
                    _logger.LogWarning("Warmup transcription had error: {Error}", warmupResult.Error.Message);
                }
                else
                {
                    _logger.LogInformation("WhisperHostedService model warmed up successfully");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to warm up WhisperHostedService model, but continuing anyway");
            }
        }

        /// <summary>
        /// Processes a transcription request and returns the response
        /// </summary>
        private async Task<WhisperInternalResponse> ProcessTranscriptionRequestAsync(WhisperInternalRequest internalRequest)
        {
            var modelName = internalRequest.TranscriptionRequest.Model ?? "large-v3-turbo";
            _logger.LogInformation("Processing transcription request {RequestId} using model: {ModelName}",
                internalRequest.RequestId, modelName);

            WhisperModelInstance modelInstance;
            try
            {
                modelInstance = await _modelManager.GetModelAsync(modelName);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("Invalid Whisper model requested: {ModelName} using large-v3-turbo", modelName);
                modelInstance = await _modelManager.GetModelAsync("large-v3-turbo");
            }

            try
            {
                var builder = modelInstance.Factory!.CreateBuilder();

                // Optimize Whisper parameters for better accuracy and reduced hallucinations
                var temperature = internalRequest.TranscriptionRequest.Temperature ?? 0.1; // Lower default temperature
                builder.WithTemperature((float)Math.Min(temperature, 0.3)); // Even stricter cap
                
                // Very strict parameters to eliminate hallucinations
                builder.WithEntropyThreshold(2.8f);        // Even higher entropy threshold
                builder.WithLogProbThreshold(-0.8f);       // Much more selective
                builder.WithNoSpeechThreshold(0.7f);       // Higher no-speech threshold
                builder.WithBeamSearchSamplingStrategy();

                if (internalRequest.TranscriptionRequest.Language == null || internalRequest.TranscriptionRequest.Language.ToLower() == "auto")
                {
                    builder.WithLanguage("auto");
                }
                else if (internalRequest.TranscriptionRequest.Language != null)
                {
                    builder.WithLanguage(internalRequest.TranscriptionRequest.Language);
                }

                if (!string.IsNullOrEmpty(internalRequest.TranscriptionRequest.Prompt))
                {
                    builder = builder.WithPrompt(internalRequest.TranscriptionRequest.Prompt);
                }

                using var processor = builder.Build();

                if (internalRequest.IsStreaming)
                {
                    // For streaming, return the async enumerable
                    return new WhisperInternalResponse
                    {
                        StreamingResponse = ProcessStreamingTranscriptionAsync(processor, internalRequest.AudioStream, internalRequest.RequestId)
                    };
                }
                else
                {
                    // For non-streaming, process audio segments separately
                    return await ProcessSegmentBasedTranscriptionAsync(processor, internalRequest);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during audio transcription for request {RequestId}", internalRequest.RequestId);
                return new WhisperInternalResponse
                {
                    Error = ex
                };
            }
        }

        /// <summary>
        /// Processes transcription using segment-based approach with silence detection
        /// </summary>
        private async Task<WhisperInternalResponse> ProcessSegmentBasedTranscriptionAsync(
            WhisperProcessor processor, 
            WhisperInternalRequest internalRequest)
        {
            var tempWavFile = Path.GetTempFileName() + ".wav";
            var normalizedFile = "";
            
            try
            {
                // Save the audio stream to a temporary WAV file
                using (var fileStream = File.Create(tempWavFile))
                {
                    internalRequest.AudioStream.Position = 0;
                    await internalRequest.AudioStream.CopyToAsync(fileStream);
                }

                // First normalize loudness to handle very quiet files, then detect silence
                normalizedFile = Path.GetTempFileName() + ".wav";
                await ApplyAudioPreprocessingAsync(tempWavFile, normalizedFile, optimizeForSilenceDetection: true);
                
                // Detect audio segments with speech content using the normalized audio
                var audioSegments = await DetectAudioSegmentsAsync(normalizedFile);
                _logger.LogInformation("Found {SegmentCount} audio segments to transcribe", audioSegments.Count);

                var transcriptionSegments = new List<TranscriptionSegment>();
                var words = new List<TranscriptionWord>();
                var fullText = new StringBuilder();
                var segmentId = 0;
                var seenTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Process each audio segment separately
                foreach (var audioSegment in audioSegments)
                {
                    try
                    {
                        // Extract the audio segment from the normalized file (same file used for silence detection)
                        var segmentFile = await ExtractAudioSegmentAsync(normalizedFile, audioSegment);
                        
                        // Validate audio quality before transcription
                        if (!await HasSufficientAudioEnergyAsync(segmentFile))
                        {
                            _logger.LogDebug("Skipping segment {Start}-{End}: insufficient audio energy", 
                                audioSegment.StartTime, audioSegment.EndTime);
                            try { File.Delete(segmentFile); } catch { }
                            continue;
                        }

                        using var segmentStream = File.OpenRead(segmentFile);
                        
                        // Collect all results from this segment first
                        var segmentResults = new List<(Whisper.net.SegmentData result, double adjustedStart, double adjustedEnd)>();
                        
                        await foreach (var result in processor.ProcessAsync(segmentStream))
                        {
                            if (string.IsNullOrWhiteSpace(result.Text)) continue;
                            
                            // Skip very short transcriptions (likely hallucinations)
                            if (result.Text.Trim().Length < 3) continue;
                            
                            // Adjust timestamps to match original audio timing
                            var adjustedStart = audioSegment.StartTime + result.Start.TotalSeconds;
                            var adjustedEnd = audioSegment.StartTime + result.End.TotalSeconds;
                            
                            segmentResults.Add((result, adjustedStart, adjustedEnd));
                        }
                        
                        // Filter and validate segment results
                        foreach (var (result, adjustedStart, adjustedEnd) in segmentResults)
                        {
                            var cleanText = result.Text.Trim();
                            var normalizedText = NormalizeSentence(cleanText);
                            
                            // Enhanced hallucination detection
                            if (IsLikelyHallucination(cleanText))
                            {
                                _logger.LogDebug("Skipping likely hallucination: {Text}", cleanText);
                                continue;
                            }
                            
                            // Skip if we've seen this exact text before (repetition detection)
                            if (seenTexts.Contains(normalizedText))
                            {
                                _logger.LogDebug("Skipping repeated text: {Text}", cleanText);
                                continue;
                            }
                            
                            // Add to seen texts
                            seenTexts.Add(normalizedText);
                            
                            // Add to full text only if it's not repetitive
                            fullText.Append(cleanText + " ");

                            var segment = new TranscriptionSegment
                            {
                                Id = segmentId++,
                                Start = adjustedStart * 1000, // Convert to milliseconds
                                End = adjustedEnd * 1000, // Convert to milliseconds
                                Text = cleanText,
                                Temperature = 0.2,
                                AvgLogprob = -0.5,
                                CompressionRatio = 2.4,
                                NoSpeechProb = 0.1,
                                Seek = 0,
                                Tokens = null
                            };

                            transcriptionSegments.Add(segment);

                            // Extract words if word-level timestamps are requested
                            if (internalRequest.TranscriptionRequest.TimestampGranularities?.Contains(TimestampGranularity.Word) == true)
                            {
                                var wordsInSegment = cleanText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                var wordDurationSeconds = result.End.TotalSeconds - result.Start.TotalSeconds;
                                var wordDurationPerWord = wordDurationSeconds / wordsInSegment.Length;

                                for (int i = 0; i < wordsInSegment.Length; i++)
                                {
                                    var wordStart = adjustedStart + (i * wordDurationPerWord);
                                    var wordEnd = adjustedStart + ((i + 1) * wordDurationPerWord);
                                    
                                    words.Add(new TranscriptionWord
                                    {
                                        Word = wordsInSegment[i],
                                        Start = wordStart * 1000, // Convert to milliseconds
                                        End = wordEnd * 1000 // Convert to milliseconds
                                    });
                                }
                            }
                        }

                        // Clean up segment file
                        try { File.Delete(segmentFile); } catch { }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to transcribe audio segment {Start}-{End}", 
                            audioSegment.StartTime, audioSegment.EndTime);
                    }
                }

                // Build final text from the filtered segments to ensure consistency
                var finalText = string.Join(" ", transcriptionSegments.Select(s => s.Text)).Trim();
                
                // Apply final cleanup to remove any remaining repetitive patterns
                finalText = CleanRepetitiveText(finalText);

                _logger.LogInformation("Transcribed {SegmentCount} segments with {TextLength} characters", 
                    transcriptionSegments.Count, finalText.Length);

                // Get total duration from the audio segments
                var totalDuration = audioSegments.Any() ? audioSegments.Max(s => s.EndTime) : 0.0;

                var response = new AudioTranscriptionResponse
                {
                    Text = finalText,
                    Task = "transcribe",
                    Language = internalRequest.TranscriptionRequest.Language ?? "auto",
                    Duration = totalDuration,
                    Segments = transcriptionSegments
                };

                // Include segments and words based on response format
                if (internalRequest.TranscriptionRequest.ResponseFormat == AudioResponseFormat.VerboseJson)
                {
                    response.Segments = transcriptionSegments;
                    if (internalRequest.TranscriptionRequest.TimestampGranularities?.Contains(TimestampGranularity.Word) == true)
                    {
                        response.Words = words;
                    }
                }

                return new WhisperInternalResponse
                {
                    TranscriptionResponse = response
                };
            }
            finally
            {
                // Clean up temporary files
                try { if (File.Exists(tempWavFile)) File.Delete(tempWavFile); } catch { }
                try { if (!string.IsNullOrEmpty(normalizedFile) && File.Exists(normalizedFile)) File.Delete(normalizedFile); } catch { }
            }
        }

        /// <summary>
        /// Extracts a specific time segment from an audio file with audio enhancement and loudness normalization
        /// </summary>
        private async Task<string> ExtractAudioSegmentAsync(string sourceFile, AudioSegment segment)
        {
            var outputFile = Path.GetTempFileName() + ".wav";
            var tempSegmentFile = Path.GetTempFileName() + ".wav";
            
            try
            {
                // Add small padding to segment boundaries
                var startTime = Math.Max(segment.StartTime - 0.2, 0); // Smaller padding to avoid silence
                var duration = segment.Duration + 0.4; // Minimal padding
                
                // First extract the segment
                await FFMpegArguments
                    .FromFileInput(sourceFile)
                    .OutputToFile(tempSegmentFile, true, options => options
                        .Seek(TimeSpan.FromSeconds(startTime))
                        .WithDuration(TimeSpan.FromSeconds(duration))
                        .WithCustomArgument("-ar 16000") // 16kHz sample rate
                        .WithCustomArgument("-ac 1") // Mono
                        .WithCustomArgument("-acodec pcm_s16le") // 16-bit PCM
                        .ForceFormat("wav"))
                    .ProcessAsynchronously();
                
                // Apply speech-optimized preprocessing
                await ApplyAudioPreprocessingAsync(tempSegmentFile, outputFile, optimizeForSilenceDetection: false);
                
                // Clean up temporary file
                try { File.Delete(tempSegmentFile); } catch { }
                
                return outputFile;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to extract audio segment {Start}-{End}", 
                    segment.StartTime, segment.EndTime);
                
                // Clean up temporary files
                try { File.Delete(tempSegmentFile); } catch { }
                try { File.Delete(outputFile); } catch { }
                
                throw;
            }
        }

        /// <summary>
        /// Validates if an audio segment has sufficient energy to contain meaningful speech
        /// </summary>
        /// <param name="audioFilePath">Path to the audio file to analyze</param>
        /// <returns>True if the audio has sufficient energy, false otherwise</returns>
        private async Task<bool> HasSufficientAudioEnergyAsync(string audioFilePath)
        {
            try
            {
                // Use ffmpeg to analyze audio levels
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-i \"{audioFilePath}\" -af volumedetect -f null -",
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(processInfo);
                var stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                // Parse the volume detection output
                // Look for mean_volume and max_volume
                var lines = stderr.Split('\n');
                double meanVolume = -100; // Default to very low
                double maxVolume = -100;

                foreach (var line in lines)
                {
                    if (line.Contains("mean_volume:"))
                    {
                        var parts = line.Split("mean_volume:");
                        if (parts.Length > 1 && double.TryParse(parts[1].Trim().Split(' ')[0], out double mean))
                        {
                            meanVolume = mean;
                        }
                    }
                    else if (line.Contains("max_volume:"))
                    {
                        var parts = line.Split("max_volume:");
                        if (parts.Length > 1 && double.TryParse(parts[1].Trim().Split(' ')[0], out double max))
                        {
                            maxVolume = max;
                        }
                    }
                }

                // Adjust thresholds for loudness-normalized audio
                // With loudness normalization, we can use more relaxed thresholds
                // Mean volume should be above -50dB and max volume above -30dB for meaningful speech
                var hasSufficientEnergy = meanVolume > -50.0 && maxVolume > -30.0;
                
                if (!hasSufficientEnergy)
                {
                    _logger.LogDebug("Audio segment rejected: mean_volume={MeanVolume}dB, max_volume={MaxVolume}dB", 
                        meanVolume, maxVolume);
                }

                return hasSufficientEnergy;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to analyze audio energy, assuming sufficient");
                return true; // Default to processing if analysis fails
            }
        }

        /// <summary>
        /// Processes streaming transcription asynchronously
        /// </summary>
        private async IAsyncEnumerable<AudioTranscriptionChunk> ProcessStreamingTranscriptionAsync(
            WhisperProcessor processor,
            Stream audioStream,
            string requestId)
        {
            var segmentCount = 0;
            await foreach (var result in processor.ProcessAsync(audioStream))
            {
                yield return new AudioTranscriptionChunk
                {
                    Text = result.Text,
                    Start = result.Start.TotalSeconds,
                    End = result.End.TotalSeconds,
                    IsFinal = false
                };
                segmentCount++;
            }

            // Send final chunk
            yield return new AudioTranscriptionChunk
            {
                Text = "",
                Start = 0,
                End = 0,
                IsFinal = true
            };
        }

        /// <summary>
        /// Public method to queue a transcription request and get the result asynchronously
        /// </summary>
        /// <param name="audioStream">Input audio stream</param>
        /// <param name="request">Transcription request parameters</param>
        /// <returns>Full transcription response</returns>
        public async Task<AudioTranscriptionResponse> TranscribeAsync(Stream audioStream, AudioTranscriptionRequest request)
        {
            var requestId = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<WhisperInternalResponse>();

            var internalRequest = new WhisperInternalRequest
            {
                RequestId = requestId,
                AudioStream = audioStream,
                TranscriptionRequest = request,
                IsStreaming = false
            };

            _pendingRequests[requestId] = tcs;

            try
            {
                await _requestWriter.WriteAsync(internalRequest);
                var response = await tcs.Task;

                if (response.Error != null)
                    throw response.Error;

                return response.TranscriptionResponse!;
            }
            catch
            {
                _pendingRequests.TryRemove(requestId, out _);
                throw;
            }
        }

        /// <summary>
        /// Public method to queue a streaming transcription request
        /// </summary>
        /// <param name="audioStream">Input audio stream</param>
        /// <param name="request">Transcription request parameters</param>
        /// <returns>Async stream of transcription chunks</returns>
        public async IAsyncEnumerable<AudioTranscriptionChunk> TranscribeStreamAsync(Stream audioStream, AudioTranscriptionRequest request)
        {
            var requestId = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<WhisperInternalResponse>();

            var internalRequest = new WhisperInternalRequest
            {
                RequestId = requestId,
                AudioStream = audioStream,
                TranscriptionRequest = request,
                IsStreaming = true
            };

            _pendingRequests[requestId] = tcs;

            await _requestWriter.WriteAsync(internalRequest);
            WhisperInternalResponse response;

            try
            {
                response = await tcs.Task;

                if (response.Error != null)
                    throw response.Error;
            }
            catch
            {
                _pendingRequests.TryRemove(requestId, out _);
                throw;
            }

            await foreach (var chunk in response.StreamingResponse!)
            {
                yield return chunk;
            }
        }

        /// <summary>
        /// Formats a transcription response into the requested output format (text, json, srt, vtt, etc.).
        /// </summary>
        /// <param name="response">Transcription response to format</param>
        /// <param name="format">Desired output format</param>
        /// <returns>Formatted transcription output</returns>
        public string FormatResponse(AudioTranscriptionResponse response, AudioResponseFormat format)
        {
            return format switch
            {
                AudioResponseFormat.Text => response.Text ?? "",
                AudioResponseFormat.Json => JsonSerializer.Serialize(new { text = response.Text }, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                }),
                AudioResponseFormat.VerboseJson => JsonSerializer.Serialize(response, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                }),
                AudioResponseFormat.Srt => FormatAsSrt(response.Segments),
                AudioResponseFormat.Vtt => FormatAsVtt(response.Segments),
                _ => response.Text ?? ""
            };
        }

        /// <summary>
        /// Applies audio preprocessing with fallback options
        /// </summary>
        /// <param name="inputFile">Input audio file path</param>
        /// <param name="outputFile">Output audio file path</param>
        /// <param name="optimizeForSilenceDetection">Whether to optimize for silence detection vs speech recognition</param>
        /// <returns>True if advanced preprocessing succeeded, false if basic conversion was used</returns>
        private async Task<bool> ApplyAudioPreprocessingAsync(string inputFile, string outputFile, bool optimizeForSilenceDetection = false)
        {
            var baseArgs = "-ar 16000 -ac 1 -acodec pcm_s16le";
            
            var filterOptions = optimizeForSilenceDetection 
                ? "loudnorm=I=-23:TP=-2:LRA=7" // Conservative for silence detection
                : "highpass=f=80,lowpass=f=8000,loudnorm=I=-16:TP=-1.5:LRA=11,dynaudnorm=f=500:g=31"; // Full speech optimization

            try
            {
                // Try advanced preprocessing first
                await FFMpegArguments
                    .FromFileInput(inputFile)
                    .OutputToFile(outputFile, true, options => options
                        .WithCustomArgument(baseArgs)
                        .WithCustomArgument($"-af {filterOptions}")
                        .ForceFormat("wav"))
                    .ProcessAsynchronously();
                
                _logger.LogDebug("Applied {ProcessingType} audio preprocessing", 
                    optimizeForSilenceDetection ? "silence detection" : "speech recognition");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Advanced preprocessing failed, trying basic normalization");
                
                try
                {
                    // Fallback to basic loudness normalization
                    await FFMpegArguments
                        .FromFileInput(inputFile)
                        .OutputToFile(outputFile, true, options => options
                            .WithCustomArgument(baseArgs)
                            .WithCustomArgument("-af loudnorm=I=-16:TP=-1.5:LRA=11")
                            .ForceFormat("wav"))
                        .ProcessAsynchronously();
                    
                    _logger.LogDebug("Applied basic loudness normalization");
                    return true;
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogWarning(fallbackEx, "Basic preprocessing failed, using simple conversion");
                    
                    // Final fallback to simple conversion
                    await FFMpegArguments
                        .FromFileInput(inputFile)
                        .OutputToFile(outputFile, true, options => options
                            .WithCustomArgument(baseArgs)
                            .ForceFormat("wav"))
                        .ProcessAsynchronously();
                    
                    return false;
                }
            }
        }

        /// <summary>
        /// Determines the audio format from the content type to help FFmpeg detect the format correctly.
        /// </summary>
        /// <param name="contentType">The MIME content type (e.g., "audio/mpeg", "audio/mp4")</param>
        /// <returns>FFmpeg format string or null if not recognized</returns>
        private string? GetAudioFormatFromContentType(string contentType)
        {
            return contentType?.ToLower().Split(';')[0].Trim() switch
            {
                "audio/mpeg" => "mp3",
                "audio/mp3" => "mp3",
                "audio/wav" => "wav",
                "audio/wave" => "wav",
                "audio/x-wav" => "wav",
                "audio/mp4" => "mp4",
                "audio/x-m4a" => "aac",
                "audio/m4a" => "aac",
                "audio/aac" => "aac",
                "audio/webm" => "webm",
                "audio/ogg" => "ogg",
                "audio/flac" => "flac",
                "audio/x-flac" => "flac",
                "audio/wma" => "wma",
                "audio/x-ms-wma" => "wma",
                "audio/opus" => "opus",
                _ => null,
            };
        }

        /// <summary>
        /// Converts any audio format to WAV 16kHz mono format suitable for Whisper processing.
        /// Removes silent audio segments to prevent model repetition issues.
        /// </summary>
        /// <param name="inputAudioStream">Input audio stream in any format</param>
        /// <param name="contentType">Content type of the audio (e.g., "audio/mpeg")</param>
        /// <returns>Stream containing WAV 16kHz mono audio with silence removed</returns>
        public async Task<Stream> ConvertToWav16kHzAsync(Stream inputAudioStream, string contentType)
        {
            try
            {
                // Create temporary files for input, intermediate, and output
                var tempInputFile = Path.GetTempFileName();
                var tempOutputFile = Path.ChangeExtension(Path.GetTempFileName(), ".wav");

                try
                {
                    // Save input stream to temporary file
                    using (var fileStream = File.Create(tempInputFile))
                    {
                        await inputAudioStream.CopyToAsync(fileStream);
                    }

                    // Detect input format from content type
                    var inputFormat = GetAudioFormatFromContentType(contentType);

                    // Convert to WAV 16kHz mono using FFMpeg (without loudness normalization for silence detection)
                    var ffmpegArgs = FFMpegArguments
                        .FromFileInput(tempInputFile);

                    // Specify input format if detected to avoid misdetection issues
                    if (!string.IsNullOrEmpty(inputFormat))
                    {
                        ffmpegArgs = FFMpegArguments
                            .FromFileInput(tempInputFile, false, input => input.WithCustomArgument($"-f {inputFormat}"));
                    }

                    await ffmpegArgs
                        .OutputToFile(tempOutputFile, true, options => options
                            .WithCustomArgument("-ar 16000") // 16kHz sample rate
                            .WithCustomArgument("-ac 1") // Mono
                            .WithCustomArgument("-acodec pcm_s16le") // 16-bit PCM
                            .ForceFormat("wav")) // WAV format
                        .ProcessAsynchronously();

                    // Read converted file into memory stream
                    var outputStream = new MemoryStream();
                    using (var fileStream = File.OpenRead(tempOutputFile))
                    {
                        await fileStream.CopyToAsync(outputStream);
                    }

                    outputStream.Position = 0;
                    _logger.LogInformation("Successfully converted audio (content-type: {ContentType}) to WAV 16kHz mono", contentType);
                    return outputStream;
                }
                finally
                {
                    // Clean up temporary files
                    try
                    {
                        if (File.Exists(tempInputFile)) File.Delete(tempInputFile);
                        if (File.Exists(tempOutputFile)) File.Delete(tempOutputFile);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to clean up temporary files");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to convert audio file (content-type: {ContentType}) to WAV 16kHz", contentType);
                throw new InvalidOperationException($"Audio conversion failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Detects audio segments with speech by analyzing the WAV file for silence.
        /// Returns a list of time segments that contain audio content.
        /// </summary>
        /// <param name="wavFilePath">Path to the WAV file to analyze</param>
        /// <returns>List of audio segments with speech content</returns>
        private async Task<List<AudioSegment>> DetectAudioSegmentsAsync(string wavFilePath)
        {
            var segments = new List<AudioSegment>();
            var tempOutputFile = Path.GetTempFileName();

            try
            {
                // Use ffmpeg silencedetect filter to detect silence periods on normalized audio
                // Adjusted parameters for loudness-normalized audio: -50dB threshold, 2.0 seconds minimum silence
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-i \"{wavFilePath}\" -af silencedetect=noise=-15dB:duration=2.0 -f null -",
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(processInfo);
                var stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                // Parse the silence detection output
                segments = ParseSilenceDetectionOutput(stderr, wavFilePath);

                // Filter segments: minimum 0.75 seconds duration to avoid short segments that cause hallucinations
                segments = segments.Where(s => s.Duration >= 0.75).ToList();
                
                _logger.LogInformation("Detected {SegmentCount} audio segments in file (after filtering)", segments.Count);
                return segments;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to detect audio segments");
                // Fallback: return the entire audio duration as one segment
                var audioDuration = await GetAudioDurationAsync(wavFilePath);
                return new List<AudioSegment> 
                { 
                    new AudioSegment { StartTime = 0, EndTime = audioDuration } 
                };
            }
            finally
            {
                try
                {
                    if (File.Exists(tempOutputFile)) File.Delete(tempOutputFile);
                }
                catch { }
            }
        }

        /// <summary>
        /// Parses the output from ffmpeg silencedetect filter to extract audio segments
        /// </summary>
        private List<AudioSegment> ParseSilenceDetectionOutput(string ffmpegOutput, string wavFilePath)
        {
            var segments = new List<AudioSegment>();
            var lines = ffmpegOutput.Split('\n');
            
            var silenceStarts = new List<double>();
            var silenceEnds = new List<double>();
            double totalDuration = 0;

            // Parse silence start and end times
            foreach (var line in lines)
            {
                if (line.Contains("silence_start:"))
                {
                    var parts = line.Split("silence_start:");
                    if (parts.Length > 1 && double.TryParse(parts[1].Trim().Split(' ')[0], out double start))
                    {
                        silenceStarts.Add(start);
                    }
                }
                else if (line.Contains("silence_end:"))
                {
                    var parts = line.Split("silence_end:");
                    if (parts.Length > 1 && double.TryParse(parts[1].Trim().Split(' ')[0], out double end))
                    {
                        silenceEnds.Add(end);
                    }
                }
                else if (line.Contains("Duration:"))
                {
                    // Try to extract total duration from format: Duration: 00:00:10.23
                    var match = System.Text.RegularExpressions.Regex.Match(line, @"Duration: (\d{2}):(\d{2}):(\d{2})\.(\d{2})");
                    if (match.Success)
                    {
                        var hours = int.Parse(match.Groups[1].Value);
                        var minutes = int.Parse(match.Groups[2].Value);
                        var seconds = int.Parse(match.Groups[3].Value);
                        var centiseconds = int.Parse(match.Groups[4].Value);
                        totalDuration = hours * 3600 + minutes * 60 + seconds + centiseconds / 100.0;
                    }
                }
            }

            // If we couldn't get duration from parsing, get it directly
            if (totalDuration == 0)
            {
                totalDuration = GetAudioDurationAsync(wavFilePath).Result;
            }

            // Create audio segments based on silence periods
            if (silenceStarts.Count == 0 && silenceEnds.Count == 0)
            {
                // No silence detected, entire file is one segment
                segments.Add(new AudioSegment { StartTime = 0, EndTime = totalDuration });
                _logger.LogDebug("No silence detected, using entire audio as single segment: 0s - {Duration}s", totalDuration);
            }
            else
            {
                _logger.LogDebug("Detected {SilenceStarts} silence starts and {SilenceEnds} silence ends", 
                    silenceStarts.Count, silenceEnds.Count);
                
                double currentStart = 0;

                // Combine silence starts and ends to create audio segments
                var allSilenceEvents = new List<(double time, bool isStart)>();
                silenceStarts.ForEach(s => allSilenceEvents.Add((s, true)));
                silenceEnds.ForEach(e => allSilenceEvents.Add((e, false)));
                allSilenceEvents = allSilenceEvents.OrderBy(e => e.time).ToList();

                foreach (var (time, isStart) in allSilenceEvents)
                {
                    if (isStart && currentStart < time)
                    {
                        // Found start of silence, add audio segment before it
                        segments.Add(new AudioSegment { StartTime = currentStart, EndTime = time });
                        // Update currentStart to the silence start position to track our position
                        currentStart = time;
                    }
                    else if (!isStart)
                    {
                        // Found end of silence, update current start to where audio resumes
                        currentStart = time;
                    }
                }

                // Add final segment if there's audio after the last silence
                // Only add if currentStart is before the end and it's not at a silence start position
                if (currentStart < totalDuration)
                {
                    // Check if the last event was a silence start (meaning audio ends with silence)
                    var lastEvent = allSilenceEvents.LastOrDefault();
                    bool endsWithSilence = lastEvent.isStart; // Last event is silence_start with no corresponding end
                    
                    if (!endsWithSilence)
                    {
                        segments.Add(new AudioSegment { StartTime = currentStart, EndTime = totalDuration });
                        _logger.LogDebug("Added final audio segment: {Start}s - {End}s", currentStart, totalDuration);
                    }
                    else
                    {
                        _logger.LogDebug("Audio ends with silence starting at {SilenceStart}s, not adding final segment", 
                            lastEvent.time);
                    }
                }
                else
                {
                    _logger.LogDebug("No final segment needed: currentStart={CurrentStart}s >= totalDuration={TotalDuration}s", 
                        currentStart, totalDuration);
                }
            }

            // Filter out very short segments and merge nearby segments
            segments = segments.Where(s => s.Duration >= 1.0).ToList();
            
            // Merge segments that are very close together (less than 3 seconds apart)
            var mergedSegments = new List<AudioSegment>();
            for (int i = 0; i < segments.Count; i++)
            {
                var current = segments[i];
                
                // Look ahead to see if we can merge with next segments
                while (i + 1 < segments.Count && segments[i + 1].StartTime - current.EndTime < 3.0)
                {
                    current = new AudioSegment 
                    { 
                        StartTime = current.StartTime, 
                        EndTime = segments[i + 1].EndTime 
                    };
                    i++;
                }
                
                mergedSegments.Add(current);
            }

            return mergedSegments;
        }

        /// <summary>
        /// Gets the duration of an audio file
        /// </summary>
        private async Task<double> GetAudioDurationAsync(string filePath)
        {
            try
            {
                var mediaInfo = await FFProbe.AnalyseAsync(filePath);
                return mediaInfo.Duration.TotalSeconds;
            }
            catch
            {
                // Fallback: assume reasonable default
                return 30.0;
            }
        }

        /// <summary>
        /// Formats transcription segments as subtitles (SRT or WebVTT format)
        /// </summary>
        /// <param name="segments">Transcription segments to format</param>
        /// <param name="isVtt">True for WebVTT format, false for SRT format</param>
        /// <returns>Formatted subtitle string</returns>
        private string FormatAsSubtitles(List<TranscriptionSegment>? segments, bool isVtt = false)
        {
            if (segments == null || segments.Count == 0)
                return isVtt ? "WEBVTT\n\n" : "";

            var subtitle = new StringBuilder(isVtt ? "WEBVTT\n\n" : "");
            
            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                
                if (!isVtt)
                {
                    subtitle.AppendLine($"{i + 1}");
                }
                
                subtitle.AppendLine($"{FormatTime(segment.Start, isVtt)} --> {FormatTime(segment.End, isVtt)}");
                subtitle.AppendLine(segment.Text);
                subtitle.AppendLine();
            }
            
            return subtitle.ToString();
        }

        /// <summary>
        /// Formats subtitle timestamps for SRT format.
        /// </summary>
        /// <param name="segments">Transcription segments to format</param>
        /// <returns>SRT-formatted subtitle string</returns>
        private string FormatAsSrt(List<TranscriptionSegment>? segments)
        {
            return FormatAsSubtitles(segments, isVtt: false);
        }

        /// <summary>
        /// Formats subtitle timestamps for WebVTT format.
        /// </summary>
        /// <param name="segments">Transcription segments to format</param>
        /// <returns>WebVTT-formatted subtitle string</returns>
        private string FormatAsVtt(List<TranscriptionSegment>? segments)
        {
            return FormatAsSubtitles(segments, isVtt: true);
        }

        /// <summary>
        /// Formats time in different subtitle formats
        /// </summary>
        private string FormatTime(double seconds, bool isVtt = false)
        {
            var time = TimeSpan.FromSeconds(seconds);
            var separator = isVtt ? "." : ",";
            return $"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2}{separator}{time.Milliseconds:D3}";
        }

        /// <summary>
        /// Cleans up repetitive patterns in transcribed text to reduce model hallucination.
        /// </summary>
        /// <param name="text">Input text with potential repetitive patterns</param>
        /// <returns>Cleaned text with repetitive patterns removed</returns>
        private string CleanRepetitiveText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            // Split into sentences for processing
            var sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(s => s.Trim())
                               .Where(s => !string.IsNullOrEmpty(s))
                               .ToList();

            if (sentences.Count <= 1)
                return text;

            var cleanedSentences = new List<string>();
            var recentSentences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var sentence in sentences)
            {
                // Normalize the sentence for comparison (remove extra spaces, punctuation)
                var normalizedSentence = NormalizeSentence(sentence);

                // Skip if we've seen this exact sentence recently (within last 3 sentences)
                if (!recentSentences.Contains(normalizedSentence))
                {
                    cleanedSentences.Add(sentence);
                    recentSentences.Add(normalizedSentence);

                    // Keep only recent sentences in memory (sliding window of 3)
                    if (recentSentences.Count > 3)
                    {
                        recentSentences.Clear();
                        // Re-add the last 2 sentences
                        var lastSentences = cleanedSentences.TakeLast(2).Select(NormalizeSentence);
                        foreach (var lastSent in lastSentences)
                        {
                            recentSentences.Add(lastSent);
                        }
                    }
                }
            }

            return string.Join(". ", cleanedSentences) + (cleanedSentences.Count > 0 ? "." : "");
        }

        /// <summary>
        /// Normalizes a sentence for comparison by removing extra whitespace and common punctuation.
        /// </summary>
        /// <param name="sentence">Input sentence</param>
        /// <returns>Normalized sentence</returns>
        private string NormalizeSentence(string sentence)
        {
            if (string.IsNullOrWhiteSpace(sentence))
                return "";

            // Remove common punctuation and normalize whitespace
            var normalized = sentence.Trim()
                                   .Replace(",", "")
                                   .Replace(";", "")
                                   .Replace(":", "")
                                   .Replace("\"", "")
                                   .Replace("'", "")
                                   .ToLowerInvariant();

            // Normalize multiple spaces to single spaces
            return System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ").Trim();
        }

        /// <summary>
        /// Detects if a transcribed text is likely a hallucination based on common patterns
        /// </summary>
        /// <param name="text">Text to analyze</param>
        /// <returns>True if the text appears to be a hallucination</returns>
        private bool IsLikelyHallucination(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return true;

            var cleanText = text.Trim().ToLowerInvariant();
            
            // Check for very short fragments that are likely hallucinations
            if (cleanText.Length < 4)
                return true;

            // Common hallucination patterns in Whisper (consolidated list)
            var hallucinationPatterns = new HashSet<string>
            {
                // Polite phrases and fillers
                "thank you", "thanks", "bye", "goodbye", "have a nice day", "have a good day",
                "see you", "see you later", "take care", "you're welcome", "welcome",
                "hmm", "mm", "mm-hmm", "uh", "ah", "eh", "oh", "okay", "ok", "right", "yes", "yeah", "yep",
                
                // Media artifacts
                "music", "applause", "clapping", "noise", "static", "beep", "beeping",
                "[music]", "[applause]", "[noise]", "[static]", "[beep]", "(music)", "(applause)",
                "subtitles", "captions", "transcript", "transcription", "closed captions",
                
                // Web artifacts and single words
                "www.", ".com", ".org", ".net", "http", "https",
                "english", "spanish", "french", "german", "chinese", "japanese", "korean",
                "...", ".", "..", "....", ".....", 
                "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with", "by"
            };

            // Quick pattern check
            if (hallucinationPatterns.Contains(cleanText))
                return true;

            // Check for repetitive patterns or insufficient letter content
            if ((cleanText.Length >= 3 && cleanText.All(c => c == cleanText[0])) ||
                (cleanText.Length <= 10 && IsVeryRepetitive(cleanText)) ||
                (System.Text.RegularExpressions.Regex.IsMatch(cleanText, @"^[^a-z]*$") && cleanText.Length < 10))
                return true;

            // Check letter density
            var letterCount = cleanText.Count(char.IsLetter);
            return letterCount == 0 || (double)letterCount / cleanText.Length < 0.5;
        }

        /// <summary>
        /// Checks if a short text is very repetitive (indicating hallucination)
        /// </summary>
        private bool IsVeryRepetitive(string text)
        {
            if (text.Length < 4) return false;

            // Check for repeated substrings
            for (int len = 1; len <= text.Length / 2; len++)
            {
                var substring = text.Substring(0, len);
                var repeated = string.Concat(Enumerable.Repeat(substring, text.Length / len));
                if (repeated == text.Substring(0, repeated.Length))
                    return true;
            }
            
            return false;
        }

        public override void Dispose()
        {
            try
            {
                _requestWriter.Complete();
                _modelManager?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during disposal");
            }

            base.Dispose();
        }
    }
}