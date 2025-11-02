using Microsoft.AspNetCore.Mvc;
using SharpAI.Models;
using SharpAI.Services;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpAI.Api.Audio;

[ApiController]
[Route("v1/audio")]
public class AudioController : ControllerBase
{
    private readonly WhisperHostedService _whisperService;
    private readonly ChatterboxService _chatterboxService;
    private readonly ILogger<AudioController> _logger;

    public AudioController(WhisperHostedService whisperService, ChatterboxService chatterboxService, ILogger<AudioController> logger)
    {
        _whisperService = whisperService;
        _chatterboxService = chatterboxService;
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
            return StatusCode(500, new { error = new { message = ex.Message, type = ex.GetType().Name } });
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
            return StatusCode(500, new { error = new { message = ex.Message, type = ex.GetType().Name } });
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

    /// <summary>
    /// Creates audio from text using text-to-speech.
    /// OpenAI-compatible endpoint with SharpAI extensions for emotion control and voice cloning.
    /// </summary>
    /// <param name="request">TTS request containing text, voice, and options</param>
    /// <returns>Audio file in the requested format</returns>
    [HttpPost("speech")]
    public async Task<IActionResult> CreateSpeech([FromBody] TtsRequest request)
    {
        try
        {
            _logger.LogInformation("Processing TTS request for text: {TextPreview}...",
                request.Input.Length > 50 ? request.Input[..50] + "..." : request.Input);

            // Validate input
            if (string.IsNullOrWhiteSpace(request.Input))
            {
                return BadRequest(new { error = new { message = "Input text is required", type = "invalid_request_error" } });
            }

            if (request.Input.Length > 4096)
            {
                return BadRequest(new { error = new { message = "Input text cannot exceed 4096 characters", type = "invalid_request_error" } });
            }

            // Validate voice (OpenAI compatibility)
            var supportedVoices = new[] { "alloy", "echo", "fable", "onyx", "nova", "shimmer" };
            if (!supportedVoices.Contains(request.Voice?.ToLower()))
            {
                return BadRequest(new { error = new { message = $"Invalid voice '{request.Voice}'. Supported voices: {string.Join(", ", supportedVoices)}", type = "invalid_request_error" } });
            }

            // Validate speed
            if (request.Speed < 0.25 || request.Speed > 4.0)
            {
                return BadRequest(new { error = new { message = "Speed must be between 0.25 and 4.0", type = "invalid_request_error" } });
            }

            // Validate emotion intensity (SharpAI extension)
            if (request.EmotionIntensity < 0.0f || request.EmotionIntensity > 1.0f)
            {
                return BadRequest(new { error = new { message = "Emotion intensity must be between 0.0 and 1.0", type = "invalid_request_error" } });
            }

            // Validate cfg_weight (SharpAI extension)
            if (request.CfgWeight < 0.0f || request.CfgWeight > 1.0f)
            {
                return BadRequest(new { error = new { message = "CFG weight must be between 0.0 and 1.0", type = "invalid_request_error" } });
            }

            // Validate exaggeration (SharpAI extension)
            if (request.Exaggeration < 0.0f || request.Exaggeration > 1.0f)
            {
                return BadRequest(new { error = new { message = "Exaggeration must be between 0.0 and 1.0", type = "invalid_request_error" } });
            }

            // Generate audio using ChatterboxService
            using var audioStream = await _chatterboxService.SynthesizeToStreamAsync(
                request.Input,
                languageId: null, // Auto-detect language
                emotionIntensity: request.EmotionIntensity ?? 0.5f,
                referenceAudioPath: request.ReferenceAudio,
                cfgWeight: request.CfgWeight ?? 0.5f,
                exaggeration: request.Exaggeration ?? 0.5f);

            // Return audio file
            return File(audioStream.ToArray(), GetContentType(request.ResponseFormat), $"speech.{request.ResponseFormat}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing TTS request");
            return StatusCode(500, new { error = new { message = ex.Message, type = ex.GetType().Name } });
        }
    }

    /// <summary>
    /// Creates audio from text using text-to-speech with form data (supports reference audio upload).
    /// SharpAI extension that allows uploading reference audio for voice cloning.
    /// </summary>
    /// <param name="formRequest">Form request with text and optional reference audio file</param>
    /// <returns>Audio file in the requested format</returns>
    [HttpPost("speech/form")]
    [RequestSizeLimit(50_000_000)] // 50MB limit for reference audio files
    public async Task<IActionResult> CreateSpeechWithForm([FromForm] TtsFormRequest formRequest)
    {
        try
        {
            _logger.LogInformation("Processing TTS form request for text: {TextPreview}...",
                formRequest.Input.Length > 50 ? formRequest.Input[..50] + "..." : formRequest.Input);

            // Validate input
            if (string.IsNullOrWhiteSpace(formRequest.Input))
            {
                return BadRequest(new { error = new { message = "Input text is required", type = "invalid_request_error" } });
            }

            if (formRequest.Input.Length > 4096)
            {
                return BadRequest(new { error = new { message = "Input text cannot exceed 4096 characters", type = "invalid_request_error" } });
            }

            // Handle reference audio if provided
            string? referenceAudioPath = null;
            if (formRequest.ReferenceAudio != null && formRequest.ReferenceAudio.Length > 0)
            {
                // Save reference audio to temporary file
                var tempPath = Path.GetTempFileName();
                var extension = Path.GetExtension(formRequest.ReferenceAudio.FileName) ?? ".wav";
                referenceAudioPath = Path.ChangeExtension(tempPath, extension);

                using var fileStream = new FileStream(referenceAudioPath, FileMode.Create);
                await formRequest.ReferenceAudio.CopyToAsync(fileStream);

                _logger.LogInformation("Saved reference audio to: {Path}", referenceAudioPath);
            }

            try
            {
                // Generate audio using ChatterboxService
                using var audioStream = await _chatterboxService.SynthesizeToStreamAsync(
                    formRequest.Input,
                    languageId: null, // Auto-detect language
                    emotionIntensity: formRequest.EmotionIntensity ?? 0.5f,
                    referenceAudioPath: referenceAudioPath,
                    cfgWeight: formRequest.CfgWeight ?? 0.5f,
                    exaggeration: formRequest.Exaggeration ?? 0.5f);

                // Return audio file
                return File(audioStream.ToArray(), GetContentType(formRequest.ResponseFormat), $"speech.{formRequest.ResponseFormat}");
            }
            finally
            {
                // Clean up temporary reference audio file
                if (referenceAudioPath != null && System.IO.File.Exists(referenceAudioPath))
                {
                    try
                    {
                        System.IO.File.Delete(referenceAudioPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete temporary reference audio file: {Path}", referenceAudioPath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing TTS form request");
            return StatusCode(500, new { error = new { message = ex.Message, type = ex.GetType().Name } });
        }
    }
    private string GetContentType(string format)
    {
        return format switch
        {
            "mp3" => "audio/mpeg",
            "wav" => "audio/wav",
            _ => "audio/wav"
        };
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