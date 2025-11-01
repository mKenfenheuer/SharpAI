using Microsoft.AspNetCore.Mvc;
using SharpAI.Models;
using SharpAI.Services;
using System.Text.Json;

namespace SharpAI.Api.Chat
{
    /// <summary>
    /// Provides OpenAI-compatible chat completion endpoints with extensions.
    /// 
    /// <para><strong>OpenAI Compatibility:</strong></para>
    /// <para>This API is fully compatible with OpenAI's Chat Completions API (v1/chat/completions).</para>
    /// <para>Standard OpenAI fields are supported: role, content, refusal, tool_calls, tool_call_id, etc.</para>
    /// 
    /// <para><strong>Extensions:</strong></para>
    /// <para>• <c>reasoning</c> field: Contains chain-of-thought reasoning content from analysis channel</para>
    /// <para>• <c>commentary</c> field: Contains meta-commentary about the response process</para>
    /// <para>• Multi-channel support: Harmony library's final, analysis, and commentary channels</para>
    /// 
    /// <para><strong>Streaming:</strong></para>
    /// <para>Supports OpenAI-compatible Server-Sent Events with delta structure.</para>
    /// <para>Extended delta fields include reasoning and commentary for enhanced transparency.</para>
    /// 
    /// <para><strong>Usage:</strong></para>
    /// <para>Use with any OpenAI-compatible client. Extended fields will be ignored by standard clients.</para>
    /// </summary>
    [ApiController]
    [Route("v1/chat")]
    public class ChatController : ControllerBase
    {
        private readonly LLamaSharpService _llamaService;
        private readonly ILogger<ChatController> _logger;
        private readonly RequestLogger _requestLogger;

        /// <summary>
        /// Initializes a new instance of the ChatController.
        /// </summary>
        /// <param name="llamaService">LLamaSharp service for chat completions</param>
        /// <param name="logger">Logger for controller operations</param>
        /// <param name="requestLogger">Request logger for JSONL logging</param>
        public ChatController(LLamaSharpService llamaService, ILogger<ChatController> logger, RequestLogger requestLogger)
        {
            _llamaService = llamaService;
            _logger = logger;
            _requestLogger = requestLogger;
        }

        /// <summary>
        /// Creates a chat completion, either as a single response or a stream.
        /// 
        /// <para><strong>OpenAI Compatibility:</strong></para>
        /// <para>Fully compatible with OpenAI's POST /v1/chat/completions endpoint.</para>
        /// <para>Supports all standard parameters: model, messages, temperature, max_tokens, etc.</para>
        /// 
        /// <para><strong>Extensions:</strong></para>
        /// <para>Response includes additional fields:</para>
        /// <para>• <c>reasoning</c>: Chain-of-thought reasoning content (from analysis channel)</para>
        /// <para>• <c>commentary</c>: Meta-commentary about response process (from commentary channel)</para>
        /// 
        /// <para><strong>Streaming:</strong></para>
        /// <para>When stream=true, returns Server-Sent Events with OpenAI-compatible delta structure.</para>
        /// <para>Delta objects include extended reasoning and commentary fields for real-time transparency.</para>
        /// 
        /// <para><strong>Content Channels:</strong></para>
        /// <para>• Final response → content field (OpenAI standard)</para>
        /// <para>• Analysis/reasoning → reasoning field (extension)</para>
        /// <para>• Commentary → commentary field (extension)</para>
        /// </summary>
        /// <param name="request">The chat completion request (OpenAI-compatible)</param>
        /// <returns>A chat completion response with optional extensions</returns>
        [HttpPost("completions")]
        public async Task<IActionResult> CreateChatCompletion([FromBody] ChatCompletionRequest request)
        {
            var requestId = Guid.NewGuid().ToString("N");
            
            try
            {
                _logger.LogInformation("[{RequestId}] Received chat completion request for model: {Model}, Stream: {Stream}", 
                    requestId, request.Model, request.Stream);
                _logger.LogDebug("[{RequestId}] Request details: {Request}", 
                    requestId, JsonSerializer.Serialize(request));
                
                // Log request to JSONL file
                await _requestLogger.LogRequestAsync(requestId, request);
                
                // Validate request
                if (request.Messages == null || !request.Messages.Any())
                {
                    var error = new { error = new { message = "Messages cannot be empty", type = "invalid_request_error" } };
                    await _requestLogger.LogErrorAsync(requestId, "Messages cannot be empty");
                    return BadRequest(error);
                }

                // Check if streaming is requested
                if (request.Stream == true)
                {
                    return await HandleStreamingResponse(request, requestId);
                }
                else
                {
                    return await HandleNonStreamingResponse(request, requestId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{RequestId}] Error processing chat completion request", requestId);
                await _requestLogger.LogErrorAsync(requestId, ex.Message, ex.StackTrace);
                return StatusCode(500, new { error = new { message = ex.Message, type = ex.GetType().Name } });
            }
        }

        /// <summary>
        /// Handles non-streaming chat completion requests.
        /// </summary>
        /// <param name="request">The chat completion request</param>
        /// <param name="requestId">Unique identifier for this request</param>
        /// <returns>A complete chat completion response</returns>
        private async Task<IActionResult> HandleNonStreamingResponse(ChatCompletionRequest request, string requestId)
        {
            _logger.LogInformation("[{RequestId}] Processing non-streaming request", requestId);
            
            CancellationTokenSource cts = new CancellationTokenSource();
            Request.HttpContext.RequestAborted.Register(() => cts.Cancel());
                
            ChatCompletionResponse? response = null;

            await foreach (var result in _llamaService.GenerateChatCompletionAsync(request, requestId, cts.Token))
            {
                response = result;
            }

            // Log final response
            _logger.LogInformation("[{RequestId}] Completed non-streaming request", requestId);
            _logger.LogDebug("[{RequestId}] Final response: {Response}", requestId, JsonSerializer.Serialize(response));
            await _requestLogger.LogFinalResponseAsync(requestId, response);
            
            // For non-streaming responses, the channel handling is already done in ToChatMessages
            // The final response will have content in the appropriate fields (Content vs Reasoning)
            return Ok(response);
        }

        /// <summary>
        /// Handles streaming chat completion requests using Server-Sent Events.
        /// </summary>
        /// <param name="request">The chat completion request</param>
        /// <param name="requestId">Unique identifier for this request</param>
        /// <returns>A streaming response</returns>
        private async Task<IActionResult> HandleStreamingResponse(ChatCompletionRequest request, string requestId)
        {
            _logger.LogInformation("[{RequestId}] Processing streaming request", requestId);
            
            Response.ContentType = "text/event-stream";
            Response.Headers.Add("Cache-Control", "no-cache");
            Response.Headers.Add("Connection", "keep-alive");

            CancellationTokenSource cts = new CancellationTokenSource();
            Request.HttpContext.RequestAborted.Register(() => cts.Cancel());

            try
            {
                bool isFirstChunk = true;
                string? streamId = null;
                var completeResponse = new System.Text.StringBuilder();

                await foreach (var chunk in _llamaService.GenerateChatCompletionAsync(request, requestId, cts.Token))
                {
                    // Use the same ID for all chunks in this stream
                    streamId ??= chunk.Id;

                    // Process the response as a streaming chunk
                    var assistantChoice = chunk.Choices?.FirstOrDefault(c => c.Message?.Role == ChatRole.Assistant);
                    
                    if (assistantChoice?.Message != null)
                    {
                        var delta = new ChatCompletionDelta();

                        // For the first chunk, include the role
                        if (isFirstChunk)
                        {
                            delta.Role = ChatRole.Assistant;
                            isFirstChunk = false;
                        }

                        // Map content to delta fields
                        if (!string.IsNullOrEmpty(assistantChoice.Message.Content))
                        {
                            delta.Content = assistantChoice.Message.Content;
                            completeResponse.Append(assistantChoice.Message.Content);
                        }
                        
                        if (!string.IsNullOrEmpty(assistantChoice.Message.Reasoning))
                        {
                            delta.Reasoning = assistantChoice.Message.Reasoning;
                        }
                        
                        if (!string.IsNullOrEmpty(assistantChoice.Message.Commentary))
                        {
                            delta.Commentary = assistantChoice.Message.Commentary;
                        }

                        // Handle tool calls
                        if (assistantChoice.Message.ToolCalls?.Any() == true)
                        {
                            delta.ToolCalls = new List<ToolCallDelta>();
                            
                            for (int i = 0; i < assistantChoice.Message.ToolCalls.Count; i++)
                            {
                                var toolCall = assistantChoice.Message.ToolCalls[i];
                                var toolCallDelta = new ToolCallDelta
                                {
                                    Index = i,
                                    Id = toolCall.CallId,
                                    Type = toolCall.Type.ToString().ToLower()
                                };
                                if (toolCall.Function != null)
                                {
                                    toolCallDelta.Function = new FunctionCallDelta
                                    {
                                        Name = toolCall.Function.Namespace != "functions" ? 
                                            $"{toolCall.Function.Namespace}.{toolCall.Function.Name}" : 
                                            toolCall.Function.Name,
                                        Arguments = toolCall.Function.Arguments
                                    };
                                }

                                delta.ToolCalls.Add(toolCallDelta);
                            }
                        }

                        // Create the streaming response only if there's actual content
                        if (!string.IsNullOrEmpty(delta.Content) || 
                            !string.IsNullOrEmpty(delta.Reasoning) ||
                            !string.IsNullOrEmpty(delta.Commentary) ||
                            delta.ToolCalls?.Any() == true)
                        {
                            var streamingResponse = new ChatCompletionStreamingResponse
                            {
                                Id = streamId,
                                Object = chunk.Object,
                                Created = chunk.Created,
                                Model = chunk.Model,
                                Choices = new List<ChatCompletionStreamingChoice>
                                {
                                    new ChatCompletionStreamingChoice
                                    {
                                        Index = 0,
                                        Delta = delta,
                                        FinishReason = assistantChoice.FinishReason
                                    }
                                },
                                Usage = chunk.Usage
                            };

                            var chunkJson = JsonSerializer.Serialize(streamingResponse);
                            await Response.WriteAsync($"data: {chunkJson}\n\n");
                            await Response.Body.FlushAsync();
                        }
                    }
                }

                // Send the final [DONE] message to indicate the stream has ended
                await Response.WriteAsync("data: [DONE]\n\n");
                await Response.Body.FlushAsync();
                
                // Log final response
                _logger.LogInformation("[{RequestId}] Completed streaming request", requestId);
                _logger.LogDebug("[{RequestId}] Complete streamed response: {Response}", requestId, completeResponse.ToString());
                await _requestLogger.LogFinalResponseAsync(requestId, new { 
                    StreamId = streamId, 
                    CompleteResponse = completeResponse.ToString() 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{RequestId}] Error during streaming chat completion", requestId);
                await _requestLogger.LogErrorAsync(requestId, ex.Message, ex.StackTrace);
                
                var errorResponse = new { error = new { message = "Stream error", type = "internal_error" } };
                var errorJson = JsonSerializer.Serialize(errorResponse);
                cts.Cancel();
                await Response.WriteAsync($"data: {errorJson}\n\n");
                await Response.Body.FlushAsync();
            }

            await Response.CompleteAsync();
            return new EmptyResult();
        }
    }
}
