using Microsoft.AspNetCore.Mvc;
using SharpAI.Services;

namespace SharpAI.Api.Models
{
    [ApiController]
    [Route("v1/models")]
    public class ModelsController : ControllerBase
    {
        private readonly WhisperModelManager _whisperModelManager;
        private readonly ILogger<ModelsController> _logger;

        public ModelsController(WhisperModelManager whisperModelManager, ILogger<ModelsController> logger)
        {
            _whisperModelManager = whisperModelManager;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult ListModels()
        {
            try
            {
                var whisperModels = _whisperModelManager.GetAvailableModels();
                
                // Add whisper-1 as an alias for the default model
                var allModels = whisperModels.Concat(new[] { "whisper-1" }).ToList();
                
                var response = new
                {
                    @object = "list",
                    data = allModels.Select(modelName => new
                    {
                        id = modelName,
                        @object = "model",
                        created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        owned_by = whisperModels.Contains(modelName) || modelName == "whisper-1" ? "openai" : "huggingface"
                    }).ToArray()
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing models");
                return StatusCode(500, new { error = new { message = "Internal server error", type = "internal_error" } });
            }
        }

        [HttpGet("audio")]
        public IActionResult ListAudioModels()
        {
            try
            {
                var whisperModels = _whisperModelManager.GetAvailableModels();
                
                // Add whisper-1 as an alias for the default model
                var allWhisperModels = whisperModels.Concat(new[] { "whisper-1" }).ToList();
                
                var response = new
                {
                    @object = "list",
                    data = allWhisperModels.Select(modelName => new
                    {
                        id = modelName,
                        @object = "model",
                        created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        owned_by = "openai",
                        type = "audio"
                    }).ToArray()
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing audio models");
                return StatusCode(500, new { error = new { message = "Internal server error", type = "internal_error" } });
            }
        }

        [HttpGet("{modelId}")]
        public IActionResult GetModel(string modelId)
        {
            try
            {
                var whisperModels = _whisperModelManager.GetAvailableModels();
                var allWhisperModels = whisperModels.Concat(new[] { "whisper-1" }).ToList();
                
                var isWhisperModel = allWhisperModels.Contains(modelId, StringComparer.OrdinalIgnoreCase);
                
                if (!isWhisperModel)
                {
                    return NotFound(new { error = new { message = $"Model '{modelId}' not found", type = "invalid_request_error" } });
                }

                var response = new
                {
                    id = modelId,
                    @object = "model",
                    created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    owned_by = "openai",
                    type = "audio"
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting model {ModelId}", modelId);
                return StatusCode(500, new { error = new { message = "Internal server error", type = "internal_error" } });
            }
        }
    }
}