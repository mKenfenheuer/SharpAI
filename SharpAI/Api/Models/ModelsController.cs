using Microsoft.AspNetCore.Mvc;
using SharpAI.Services;

namespace SharpAI.Api.Models
{
    [ApiController]
    [Route("v1/models")]
    public class ModelsController : ControllerBase
    {
        private readonly LLamaModelManager _modelManager;
        private readonly WhisperModelManager _whisperModelManager;
        private readonly ILogger<ModelsController> _logger;

        public ModelsController(LLamaModelManager modelManager, WhisperModelManager whisperModelManager, ILogger<ModelsController> logger)
        {
            _modelManager = modelManager;
            _whisperModelManager = whisperModelManager;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult ListModels()
        {
            try
            {
                var chatModels = _modelManager.GetAvailableModels();
                var whisperModels = _whisperModelManager.GetAvailableModels();
                
                // Add whisper-1 as an alias for the default model
                var allWhisperModels = whisperModels.Concat(new[] { "whisper-1" }).ToList();
                
                var allModels = chatModels.Concat(allWhisperModels).ToList();
                
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

        [HttpGet("chat")]
        public IActionResult ListChatModels()
        {
            try
            {
                var availableModels = _modelManager.GetAvailableModels();
                
                var response = new
                {
                    @object = "list",
                    data = availableModels.Select(modelName => new
                    {
                        id = modelName,
                        @object = "model",
                        created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                        owned_by = "huggingface",
                        type = "chat"
                    }).ToArray()
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing chat models");
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
                var chatModels = _modelManager.GetAvailableModels();
                var whisperModels = _whisperModelManager.GetAvailableModels();
                var allWhisperModels = whisperModels.Concat(new[] { "whisper-1" }).ToList();
                
                var isWhisperModel = allWhisperModels.Contains(modelId, StringComparer.OrdinalIgnoreCase);
                var isChatModel = chatModels.Contains(modelId, StringComparer.OrdinalIgnoreCase);
                
                if (!isWhisperModel && !isChatModel)
                {
                    return NotFound(new { error = new { message = $"Model '{modelId}' not found", type = "invalid_request_error" } });
                }

                var response = new
                {
                    id = modelId,
                    @object = "model",
                    created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    owned_by = isWhisperModel ? "openai" : "huggingface",
                    type = isWhisperModel ? "audio" : "chat"
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