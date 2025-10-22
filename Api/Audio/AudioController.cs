using Microsoft.AspNetCore.Mvc;
using SharpAI.Models;
using SharpAI.Services;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpAI.Api.Audio
{
    [ApiController]
    [Route("v1/audio")]
    public class AudioController : ControllerBase
    {
        private readonly WhisperService _whisperService;
        private readonly ILogger<AudioController> _logger;

        public AudioController(WhisperService whisperService, ILogger<AudioController> logger)
        {
            _whisperService = whisperService;
            _logger = logger;
        }

        [HttpPost("transcriptions")]
        [RequestSizeLimit(100_000_000)] // 100MB limit for audio files
        [DisableRequestSizeLimit]
        public async Task<IActionResult> CreateTranscription([FromForm] AudioTranscriptionFormRequest formRequest)
        {
            try
            {
                // Validate file
                if (formRequest.File == null || formRequest.File.Length == 0)
                {
                    return BadRequest(new { error = new { message = "No audio file provided", type = "invalid_request_error" } });
                }

                // Validate file type
                var allowedTypes = new[] { "audio/mpeg", "audio/mp4", "audio/wav", "audio/webm", "audio/m4a", "audio/ogg", "audio/flac" };
                if (!allowedTypes.Contains(formRequest.File.ContentType?.ToLower()))
                {
                    return BadRequest(new { error = new { message = "Invalid audio file type", type = "invalid_request_error" } });
                }

                _logger.LogInformation("Processing audio transcription for file: {FileName} ({Size} bytes)", 
                    formRequest.File.FileName, formRequest.File.Length);

                // Convert form request to model request
                var request = new AudioTranscriptionRequest
                {
                    File = formRequest.File,
                    Model = formRequest.Model ?? "whisper-1",
                    Language = formRequest.Language,
                    Prompt = formRequest.Prompt,
                    ResponseFormat = ParseResponseFormat(formRequest.ResponseFormat),
                    Temperature = formRequest.Temperature,
                    TimestampGranularities = ParseTimestampGranularities(formRequest.TimestampGranularities)
                };

                // Check if streaming is requested (custom extension)
                if (formRequest.Stream == true)
                {
                    return await HandleStreamingTranscription(request);
                }
                else
                {
                    return await HandleTranscription(request);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing audio transcription");
                return StatusCode(500, new { error = new { message = "Internal server error", type = "internal_error" } });
            }
        }

        private async Task<IActionResult> HandleTranscription(AudioTranscriptionRequest request)
        {
            using var originalAudioStream = request.File!.OpenReadStream();
            
            // Convert audio to WAV 16kHz format before transcription
            using var convertedAudioStream = await _whisperService.ConvertToWav16kHzAsync(
                originalAudioStream, 
                request.File.FileName ?? "audio");
            
            var response = await _whisperService.TranscribeAsync(convertedAudioStream, request);

            var responseFormat = request.ResponseFormat ?? AudioResponseFormat.Json;

            // Handle different response formats
            return responseFormat switch
            {
                AudioResponseFormat.Text => Ok(response.Text),
                AudioResponseFormat.Srt => Content(_whisperService.FormatResponse(response, responseFormat), "text/plain"),
                AudioResponseFormat.Vtt => Content(_whisperService.FormatResponse(response, responseFormat), "text/vtt"),
                AudioResponseFormat.VerboseJson => Ok(response), // Return full response object for verbose_json
                AudioResponseFormat.Json => Ok(new 
                {
                    segments = response.Segments?.Select(s => new
                    {
                        id = s.Id,
                        start = (int)s.Start, // Convert to int milliseconds as shown in example
                        end = (int)s.End,     // Convert to int milliseconds as shown in example
                        text = s.Text
                    }).ToArray() ?? Array.Empty<object>(),
                    text = response.Text
                }),
                _ => Ok(new 
                {
                    segments = response.Segments?.Select(s => new
                    {
                        id = s.Id,
                        start = (int)s.Start,
                        end = (int)s.End,
                        text = s.Text
                    }).ToArray() ?? Array.Empty<object>(),
                    text = response.Text
                })
            };
        }

        private async Task<IActionResult> HandleStreamingTranscription(AudioTranscriptionRequest request)
        {
            Response.Headers["Content-Type"] = "text/event-stream";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["Connection"] = "keep-alive";

            using var originalAudioStream = request.File!.OpenReadStream();
            
            // Convert audio to WAV 16kHz format before transcription
            using var convertedAudioStream = await _whisperService.ConvertToWav16kHzAsync(
                originalAudioStream, 
                request.File.FileName ?? "audio");

            await foreach (var chunk in _whisperService.TranscribeStreamAsync(convertedAudioStream, request))
            {
                var json = JsonSerializer.Serialize(chunk, new JsonSerializerOptions 
                { 
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });
                
                await Response.WriteAsync($"data: {json}\n\n");
                await Response.Body.FlushAsync();

                if (chunk.IsFinal)
                    break;
            }

            await Response.WriteAsync("data: [DONE]\n\n");
            await Response.Body.FlushAsync();

            return new EmptyResult();
        }

        [HttpPost("translations")]
        [RequestSizeLimit(100_000_000)] // 100MB limit for audio files
        [DisableRequestSizeLimit]
        public async Task<IActionResult> CreateTranslation([FromForm] AudioTranscriptionFormRequest formRequest)
        {
            // For now, translation uses the same logic as transcription
            // In a full implementation, you would configure Whisper for translation mode
            try
            {
                // Validate file
                if (formRequest.File == null || formRequest.File.Length == 0)
                {
                    return BadRequest(new { error = new { message = "No audio file provided", type = "invalid_request_error" } });
                }

                _logger.LogInformation("Processing audio translation for file: {FileName}", formRequest.File.FileName);

                // Force language to English for translation
                var request = new AudioTranscriptionRequest
                {
                    File = formRequest.File,
                    Model = formRequest.Model ?? "whisper-1",
                    Language = "en", // Translation always outputs English
                    Prompt = formRequest.Prompt,
                    ResponseFormat = ParseResponseFormat(formRequest.ResponseFormat),
                    Temperature = formRequest.Temperature,
                    TimestampGranularities = ParseTimestampGranularities(formRequest.TimestampGranularities)
                };

                using var originalAudioStream = request.File!.OpenReadStream();
                
                // Convert audio to WAV 16kHz format before transcription
                using var convertedAudioStream = await _whisperService.ConvertToWav16kHzAsync(
                    originalAudioStream, 
                    request.File.FileName ?? "audio");
                
                var response = await _whisperService.TranscribeAsync(convertedAudioStream, request);

                // Override task for translation
                response.Task = "translate";

                var responseFormat = request.ResponseFormat ?? AudioResponseFormat.Json;

                // Handle different response formats
                return responseFormat switch
                {
                    AudioResponseFormat.Text => Ok(response.Text),
                    AudioResponseFormat.Srt => Content(_whisperService.FormatResponse(response, responseFormat), "text/plain"),
                    AudioResponseFormat.Vtt => Content(_whisperService.FormatResponse(response, responseFormat), "text/vtt"),
                    AudioResponseFormat.VerboseJson => Ok(response), // Return full response object for verbose_json
                    AudioResponseFormat.Json => Ok(new 
                    {
                        segments = response.Segments?.Select(s => new
                        {
                            id = s.Id,
                            start = (int)s.Start, // Convert to int milliseconds as shown in example
                            end = (int)s.End,     // Convert to int milliseconds as shown in example
                            text = s.Text
                        }).ToArray() ?? Array.Empty<object>(),
                        text = response.Text
                    }),
                    _ => Ok(new 
                    {
                        segments = response.Segments?.Select(s => new
                        {
                            id = s.Id,
                            start = (int)s.Start,
                            end = (int)s.End,
                            text = s.Text
                        }).ToArray() ?? Array.Empty<object>(),
                        text = response.Text
                    })
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing audio translation");
                return StatusCode(500, new { error = new { message = "Internal server error", type = "internal_error" } });
            }
        }

        private AudioResponseFormat ParseResponseFormat(string? responseFormat)
        {
            if (string.IsNullOrEmpty(responseFormat))
                return AudioResponseFormat.Json;

            return responseFormat.ToLower() switch
            {
                "json" => AudioResponseFormat.Json,
                "text" => AudioResponseFormat.Text,
                "srt" => AudioResponseFormat.Srt,
                "verbose_json" => AudioResponseFormat.VerboseJson,
                "vtt" => AudioResponseFormat.Vtt,
                _ => AudioResponseFormat.Json
            };
        }

        private List<TimestampGranularity>? ParseTimestampGranularities(string? timestampGranularities)
        {
            if (string.IsNullOrEmpty(timestampGranularities))
                return null;

            var granularities = new List<TimestampGranularity>();
            var parts = timestampGranularities.Split(',', StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                var trimmed = part.Trim().ToLower();
                if (trimmed == "word")
                    granularities.Add(TimestampGranularity.Word);
                else if (trimmed == "segment")
                    granularities.Add(TimestampGranularity.Segment);
            }

            return granularities.Count > 0 ? granularities : null;
        }
    }

    // Form request model for multipart/form-data
    public class AudioTranscriptionFormRequest
    {
        public IFormFile? File { get; set; }
        public string? Model { get; set; }
        public string? Language { get; set; }
        public string? Prompt { get; set; }
        
        [JsonPropertyName("response_format")]
        public string? ResponseFormat { get; set; }
        
        public double? Temperature { get; set; }
        
        [JsonPropertyName("timestamp_granularities")]
        public string? TimestampGranularities { get; set; }
        
        // Custom extension for streaming
        public bool? Stream { get; set; }
    }
}