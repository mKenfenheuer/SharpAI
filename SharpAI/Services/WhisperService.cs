using Whisper.net;
using Whisper.net.Ggml;
using SharpAI.Models;
using System.Text;
using System.Text.Json;
using FFMpegCore;
using FFMpegCore.Enums;
using Whisper.net.Logger;

namespace SharpAI.Services
{
    /// <summary>
    /// Service for audio transcription using Whisper speech recognition models.
    /// Supports multiple response formats, streaming transcription, and audio format conversion.
    /// </summary>
    public class WhisperService : IDisposable
    {
        private readonly WhisperModelManager _modelManager;
        private readonly ILogger<WhisperService> _logger;

        /// <summary>
        /// Initializes a new instance of the WhisperService.
        /// </summary>
        /// <param name="modelManager">Whisper model manager for loading models</param>
        /// <param name="logger">Logger for service operations</param>
        public WhisperService(WhisperModelManager modelManager, ILogger<WhisperService> logger)
        {
            _modelManager = modelManager;
            _logger = logger;
        }

    /// <summary>
    /// Transcribes an audio stream synchronously and returns the full transcription response.
    /// </summary>
    /// <param name="audioStream">Input audio stream</param>
    /// <param name="request">Transcription request parameters</param>
    /// <returns>Full transcription response</returns>
    public async Task<AudioTranscriptionResponse> TranscribeAsync(Stream audioStream, AudioTranscriptionRequest request)
        {
            var modelName = request.Model ?? "medium"; // Default to whisper-medium (medium model)
            _logger.LogInformation("Using Whisper model: {ModelName}", modelName);
            
            WhisperModelInstance modelInstance;
            try
            {
                modelInstance = await _modelManager.GetModelAsync(modelName);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning("Invalid Whisper model requested: {ModelName} using whisper-medium", modelName);
                modelInstance = await _modelManager.GetModelAsync("medium");
            }

            try
            {
                var builder = modelInstance.Factory!.CreateBuilder()
                    .WithLanguage(request.Language ?? "auto");

                if (request.Temperature.HasValue)
                {
                    builder = builder.WithTemperature((float)request.Temperature.Value);
                }

                if (!string.IsNullOrEmpty(request.Prompt))
                {
                    builder = builder.WithPrompt(request.Prompt);
                }

                using var processor = builder.Build();

                var segments = new List<TranscriptionSegment>();
                var words = new List<TranscriptionWord>();
                var fullText = new StringBuilder();

                var segmentId = 0;
                await foreach (var result in processor.ProcessAsync(audioStream))
                {
                    fullText.Append(result.Text);

                    var segment = new TranscriptionSegment
                    {
                        Id = segmentId++,
                        Start = result.Start.TotalMilliseconds, // Convert to milliseconds to match the format
                        End = result.End.TotalMilliseconds,     // Convert to milliseconds to match the format
                        Text = result.Text,
                        Temperature = 0.0, // Whisper.net doesn't provide this
                        AvgLogprob = 0.0,   // Whisper.net doesn't provide this
                        CompressionRatio = 0.0, // Whisper.net doesn't provide this
                        NoSpeechProb = 0.0, // Whisper.net doesn't provide this
                        Seek = 0, // Whisper.net doesn't provide this
                        Tokens = null // Remove tokens from response
                    };

                    segments.Add(segment);

                    // Extract words if word-level timestamps are requested
                    if (request.TimestampGranularities?.Contains(TimestampGranularity.Word) == true)
                    {
                        // Simple word extraction - in production, you might want more sophisticated word boundary detection
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

                var response = new AudioTranscriptionResponse
                {
                    Text = fullText.ToString().Trim(),
                    Task = "transcribe",
                    Language = request.Language ?? "auto", // Whisper.net doesn't return detected language
                    Duration = (segments.LastOrDefault()?.End ?? 0.0) / 1000.0, // Convert back to seconds for duration
                    Segments = segments
                };

                // Include segments and words based on response format
                if (request.ResponseFormat == AudioResponseFormat.VerboseJson)
                {
                    response.Segments = segments;
                    if (request.TimestampGranularities?.Contains(TimestampGranularity.Word) == true)
                    {
                        response.Words = words;
                    }
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during audio transcription");
                throw;
            }
        }

    /// <summary>
    /// Streams transcription chunks as the audio is processed.
    /// </summary>
    /// <param name="audioStream">Input audio stream</param>
    /// <param name="request">Transcription request parameters</param>
    /// <returns>Async stream of transcription chunks</returns>
    public async IAsyncEnumerable<AudioTranscriptionChunk> TranscribeStreamAsync(Stream audioStream, AudioTranscriptionRequest request)
        {
            var modelName = request.Model ?? "whisper-1"; // Default to whisper-1 (base model)
            _logger.LogInformation("Using Whisper model for streaming: {ModelName}", modelName);
            
            WhisperModelInstance modelInstance;
            try
            {
                modelInstance = await _modelManager.GetModelAsync(modelName);
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "Invalid Whisper model requested for streaming: {ModelName}", modelName);
                throw new ArgumentException($"Whisper model '{modelName}' is not available. Available models: {string.Join(", ", _modelManager.GetAvailableModels())}", ex);
            }

            var builder = modelInstance.Factory!.CreateBuilder()
                .WithLanguage(request.Language ?? "auto");

            if (request.Temperature.HasValue)
            {
                builder = builder.WithTemperature((float)request.Temperature.Value);
            }

            if (!string.IsNullOrEmpty(request.Prompt))
            {
                builder = builder.WithPrompt(request.Prompt);
            }

            using var processor = builder.Build();

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
        /// Converts any audio format to WAV 16kHz mono format suitable for Whisper processing
        /// </summary>
        /// <param name="inputAudioStream">Input audio stream in any format</param>
        /// <param name="inputFileName">Original filename to help determine format</param>
        /// <returns>Stream containing WAV 16kHz mono audio</returns>
    /// <summary>
    /// Converts any audio format to WAV 16kHz mono format suitable for Whisper processing.
    /// </summary>
    /// <param name="inputAudioStream">Input audio stream in any format</param>
    /// <param name="inputFileName">Original filename to help determine format</param>
    /// <returns>Stream containing WAV 16kHz mono audio</returns>
    public async Task<Stream> ConvertToWav16kHzAsync(Stream inputAudioStream, string inputFileName)
        {
            try
            {
                // Create temporary files for input and output
                var tempInputFile = Path.GetTempFileName();
                var tempOutputFile = Path.ChangeExtension(Path.GetTempFileName(), ".wav");

                try
                {
                    // Save input stream to temporary file
                    using (var fileStream = File.Create(tempInputFile))
                    {
                        await inputAudioStream.CopyToAsync(fileStream);
                    }

                    // Convert to WAV 16kHz mono using FFMpeg
                    await FFMpegArguments
                        .FromFileInput(tempInputFile)
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
                    _logger.LogInformation("Successfully converted audio from {InputFileName} to WAV 16kHz mono", inputFileName);
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
                _logger.LogError(ex, "Failed to convert audio file {InputFileName} to WAV 16kHz", inputFileName);
                throw new InvalidOperationException($"Audio conversion failed: {ex.Message}", ex);
            }
        }

        public void Dispose()
        {
            _modelManager?.Dispose();
        }
    }
}