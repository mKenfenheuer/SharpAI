using Microsoft.AspNetCore.Mvc;
using SharpAI.Models;
using SharpAI.Services;
using System.Text.Json;

namespace SharpAI.Api.Chat
{
    /// <summary>
    /// Provides OpenAI-compatible chat completion endpoints.
    /// Handles both streaming and non-streaming chat completions with tool support.
    /// </summary>
    [ApiController]
    [Route("v1/chat")]
    public class ChatController : ControllerBase
    {
        private readonly LLamaSharpService _llamaService;
        private readonly ILogger<ChatController> _logger;

        /// <summary>
        /// Initializes a new instance of the ChatController.
        /// </summary>
        /// <param name="llamaService">LLamaSharp service for chat completions</param>
        /// <param name="logger">Logger for controller operations</param>
        public ChatController(LLamaSharpService llamaService, ILogger<ChatController> logger)
        {
            _llamaService = llamaService;
            _logger = logger;
        }

        /// <summary>
        /// Creates a chat completion, either as a single response or a stream.
        /// Compatible with OpenAI's chat completions API.
        /// </summary>
        /// <param name="request">The chat completion request</param>
        /// <returns>A chat completion response</returns>
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

        /// <summary>
        /// Handles non-streaming chat completion requests.
        /// </summary>
        /// <param name="request">The chat completion request</param>
        /// <returns>A complete chat completion response</returns>
        private async Task<IActionResult> HandleNonStreamingResponse(ChatCompletionRequest request)
        {
            var response = await _llamaService.GenerateChatCompletionAsync(request);
            return Ok(response);
        }
    }
}
