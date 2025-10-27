using Microsoft.AspNetCore.Mvc;
using SharpAI.Models;
using SharpAI.Services;
using System.Text.Json;

namespace SharpAI.Api.Chat
{
    [ApiController]
    [Route("v1/chat")]
    public class ChatController : ControllerBase
    {
        private readonly LLamaSharpService _llamaService;
        private readonly ILogger<ChatController> _logger;

        public ChatController(LLamaSharpService llamaService, ILogger<ChatController> logger)
        {
            _llamaService = llamaService;
            _logger = logger;
        }

        [HttpPost("completions")]
        public async Task<IActionResult> CreateChatCompletion([FromBody] ChatCompletionRequest request)
        {
            try
            {
                _logger.LogInformation("Received chat completion request for model: {Model}", request.Model);

                // Validate request
                if (request.Messages == null || !request.Messages.Any())
                {
                    return BadRequest(new { error = new { message = "Messages cannot be empty", type = "invalid_request_error" } });
                }

                return await HandleNonStreamingResponse(request);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing chat completion request");
                return StatusCode(500, new { error = new { message = "Internal server error", type = "internal_error" } });
            }
        }

        private async Task<IActionResult> HandleNonStreamingResponse(ChatCompletionRequest request)
        {
            var response = await _llamaService.GenerateChatCompletionAsync(request);
            return Ok(response);
        }
    }
}
