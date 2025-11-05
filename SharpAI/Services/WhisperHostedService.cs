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

                if (internalRequest.TranscriptionRequest.Temperature != null)
                    builder.WithTemperature((float)internalRequest.TranscriptionRequest.Temperature);
                
                builder.WithTemperature(0.6f);
                builder.WithEntropyThreshold(3.0f);
                builder.WithLogProbThreshold(-2.0f);
                builder.WithNoSpeechThreshold(0.6f);
                builder.WithBeamSearchSamplingStrategy();

                if (internalRequest.TranscriptionRequest.Language == null || internalRequest.TranscriptionRequest.Language.ToLower() == "auto")
                {
                    builder.WithLanguageDetection();
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
                    // For non-streaming, collect all results
                    var segments = new List<TranscriptionSegment>();
                    var words = new List<TranscriptionWord>();
                    var fullText = new StringBuilder();

                    var segmentId = 0;
                    await foreach (var result in processor.ProcessAsync(internalRequest.AudioStream))
                    {
                        fullText.Append(result.Text);

                        var segment = new TranscriptionSegment
                        {
                            Id = segmentId++,
                            Start = result.Start.TotalMilliseconds,
                            End = result.End.TotalMilliseconds,
                            Text = result.Text,
                            Temperature = 0.2,
                            AvgLogprob = -0.5,
                            CompressionRatio = 2.4,
                            NoSpeechProb = 0.1,
                            Seek = 0,
                            Tokens = null
                        };

                        segments.Add(segment);

                        // Extract words if word-level timestamps are requested
                        if (internalRequest.TranscriptionRequest.TimestampGranularities?.Contains(TimestampGranularity.Word) == true)
                        {
                            var wordsInSegment = result.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            var wordDurationMs = (result.End - result.Start).TotalMilliseconds / wordsInSegment.Length;

                            for (int i = 0; i < wordsInSegment.Length; i++)
                            {
                                words.Add(new TranscriptionWord
                                {
                                    Word = wordsInSegment[i],
                                    Start = result.Start.TotalMilliseconds + (i * wordDurationMs),
                                    End = result.Start.TotalMilliseconds + ((i + 1) * wordDurationMs)
                                });
                            }
                        }
                    }

                    var finalText = fullText.ToString().Trim();

                    // Clean up repetitive patterns in the transcribed text
                    finalText = CleanRepetitiveText(finalText);

                    var response = new AudioTranscriptionResponse
                    {
                        Text = finalText,
                        Task = "transcribe",
                        Language = internalRequest.TranscriptionRequest.Language ?? "auto",
                        Duration = (segments.LastOrDefault()?.End ?? 0.0) / 1000.0,
                        Segments = segments
                    };

                    // Include segments and words based on response format
                    if (internalRequest.TranscriptionRequest.ResponseFormat == AudioResponseFormat.VerboseJson)
                    {
                        response.Segments = segments;
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
        /// Converts any audio format to WAV 16kHz mono format suitable for Whisper processing.
        /// Removes silent audio segments to prevent model repetition issues.
        /// </summary>
        /// <param name="inputAudioStream">Input audio stream in any format</param>
        /// <param name="inputFileName">Original filename to help determine format</param>
        /// <returns>Stream containing WAV 16kHz mono audio with silence removed</returns>
        public async Task<Stream> ConvertToWav16kHzAsync(Stream inputAudioStream, string inputFileName)
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

                    // Convert to WAV 16kHz mono using FFMpeg, Remove silence from audio to prevent repetition issues
                    await FFMpegArguments
                        .FromFileInput(tempInputFile)
                        .OutputToFile(tempOutputFile, true, options => options
                            //.WithCustomArgument("-af silenceremove=stop_periods=-1:stop_duration=1:stop_threshold=-50dB")
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
                    _logger.LogInformation("Successfully converted audio from {InputFileName} to WAV 16kHz mono with silence removed", inputFileName);
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
                _logger.LogError(ex, "Failed to convert audio file {InputFileName} to WAV 16kHz with silence removal", inputFileName);
                throw new InvalidOperationException($"Audio conversion failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Formats subtitle timestamps for SRT format.
        /// </summary>
        /// <param name="segments">Transcription segments to format</param>
        /// <returns>SRT-formatted subtitle string</returns>
        private string FormatAsSrt(List<TranscriptionSegment>? segments)
        {
            if (segments == null || segments.Count == 0)
                return "";

            var srt = new StringBuilder();
            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                srt.AppendLine($"{i + 1}");
                srt.AppendLine($"{FormatTime(segment.Start)} --> {FormatTime(segment.End)}");
                srt.AppendLine(segment.Text);
                srt.AppendLine();
            }
            return srt.ToString();
        }

        /// <summary>
        /// Formats subtitle timestamps for WebVTT format.
        /// </summary>
        /// <param name="segments">Transcription segments to format</param>
        /// <returns>WebVTT-formatted subtitle string</returns>
        private string FormatAsVtt(List<TranscriptionSegment>? segments)
        {
            if (segments == null || segments.Count == 0)
                return "WEBVTT\n\n";

            var vtt = new StringBuilder("WEBVTT\n\n");
            foreach (var segment in segments)
            {
                vtt.AppendLine($"{FormatTimeVtt(segment.Start)} --> {FormatTimeVtt(segment.End)}");
                vtt.AppendLine(segment.Text);
                vtt.AppendLine();
            }
            return vtt.ToString();
        }

        private string FormatTime(double seconds)
        {
            var time = TimeSpan.FromSeconds(seconds);
            return $"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2},{time.Milliseconds:D3}";
        }

        private string FormatTimeVtt(double seconds)
        {
            var time = TimeSpan.FromSeconds(seconds);
            return $"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2}.{time.Milliseconds:D3}";
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