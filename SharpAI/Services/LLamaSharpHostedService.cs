using LLama;
using LLama.Common;
using Microsoft.Extensions.DependencyInjection;
using SharpAI.Models;
using SharpAI.Services.Harmony;
using System.Text;
using System.Threading.Channels;
using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;

namespace SharpAI.Services
{
    /// <summary>
    /// Hosted service that manages model selection and generates chat completions using LLamaSharp backends.
    /// Accepts requests asynchronously but processes them sequentially.
    /// Provides both synchronous completion and streaming completion compatible with OpenAI-style responses.
    /// </summary>
    public class LLamaSharpHostedService : BackgroundService
    {
        private readonly LLamaModelManager _modelManager;
        private readonly ILogger<LLamaSharpHostedService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly RequestLogger _requestLogger;
        private readonly Channel<LLamaSharpInternalRequest> _requestChannel;
        private readonly ChannelWriter<LLamaSharpInternalRequest> _requestWriter;
        private readonly ChannelReader<LLamaSharpInternalRequest> _requestReader;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<LLamaSharpInternalResponse>> _pendingRequests;

        /// <summary>
        /// Initializes a new instance of the LLamaSharpHostedService.
        /// </summary>
        /// <param name="modelManager">Model manager for loading and managing models</param>
        /// <param name="logger">Logger for service operations</param>
        /// <param name="serviceProvider">Service provider for retrieving scoped services</param>
        /// <param name="requestLogger">Request logger for JSONL logging</param>
        public LLamaSharpHostedService(
            LLamaModelManager modelManager, 
            ILogger<LLamaSharpHostedService> logger,
            IServiceProvider serviceProvider,
            RequestLogger requestLogger)
        {
            _modelManager = modelManager;
            _logger = logger;
            _serviceProvider = serviceProvider;
            _requestLogger = requestLogger;
            
            var options = new BoundedChannelOptions(50)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            };
            
            _requestChannel = Channel.CreateBounded<LLamaSharpInternalRequest>(options);
            _requestWriter = _requestChannel.Writer;
            _requestReader = _requestChannel.Reader;
            _pendingRequests = new ConcurrentDictionary<string, TaskCompletionSource<LLamaSharpInternalResponse>>();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("LLamaSharpHostedService started, processing chat completion requests...");
                
                await foreach (var request in _requestReader.ReadAllAsync(stoppingToken))
                {
                    try
                    {
                        var result = await ProcessChatCompletionRequestAsync(request);
                        
                        if (_pendingRequests.TryRemove(request.RequestId, out var tcs))
                        {
                            tcs.SetResult(result);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing chat completion request {RequestId}", request.RequestId);
                        
                        if (_pendingRequests.TryRemove(request.RequestId, out var tcs))
                        {
                            tcs.SetException(ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fatal error in LLamaSharpHostedService");
            }
        }

        /// <summary>
        /// Processes a chat completion request and returns the streaming response
        /// </summary>
        private async Task<LLamaSharpInternalResponse> ProcessChatCompletionRequestAsync(LLamaSharpInternalRequest internalRequest)
        {
            using var scope = _serviceProvider.CreateScope();
            var streamingEngine = scope.ServiceProvider.GetRequiredService<StreamingInferenceEngine>();
            var harmonyFormatter = scope.ServiceProvider.GetRequiredService<HarmonyFormatter>();

            var requestedModel = internalRequest.CompletionRequest.Model ?? "gpt-4";
            _logger.LogInformation("[{RequestId}] Requested model: {RequestedModel}", internalRequest.RequestId, requestedModel);

            // Validate tool definitions if provided
            if (internalRequest.CompletionRequest.Tools?.Any() == true)
            {
                var validationResults = FunctionSchemaValidator.ValidateToolDefinitions(internalRequest.CompletionRequest.Tools);
                if (validationResults.Any())
                {
                    var errorMessage = "Invalid function definitions: " + 
                        string.Join("; ", validationResults.SelectMany(kvp => kvp.Value.Select(err => $"{kvp.Key}: {err}")));
                    _logger.LogError("[{RequestId}] Tool validation failed: {ValidationErrors}", internalRequest.RequestId, errorMessage);
                    return new LLamaSharpInternalResponse
                    {
                        Error = new ArgumentException(errorMessage)
                    };
                }
            }

            ModelInstance modelInstance;
            try
            {
                modelInstance = await _modelManager.GetModelAsync(requestedModel);
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "[{RequestId}] Invalid model requested: {ModelName}", internalRequest.RequestId, requestedModel);
                return new LLamaSharpInternalResponse
                {
                    Error = new ArgumentException($"Model '{requestedModel}' is not available. Use /v1/models to see available models.", ex)
                };
            }

            try
            {
                // Prepare messages with default system prompt if needed
                var allMessages = PrepareMessages(internalRequest.CompletionRequest.Messages);

                // Use unified streaming engine and return the async enumerable
                return new LLamaSharpInternalResponse
                {
                    StreamingResponse = streamingEngine.GenerateStreamAsync(
                        allMessages, 
                        internalRequest.CompletionRequest, 
                        modelInstance, 
                        internalRequest.RequestId, 
                        internalRequest.CancellationToken)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{RequestId}] Error processing chat completion", internalRequest.RequestId);
                return new LLamaSharpInternalResponse
                {
                    Error = ex
                };
            }
        }

        /// <summary>
        /// Public method to queue a chat completion request and get the streaming result asynchronously
        /// </summary>
        /// <param name="request">Chat completion request</param>
        /// <param name="requestId">Unique identifier for request tracking</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>Chat completion response stream</returns>
        public async IAsyncEnumerable<ChatCompletionResponse> GenerateChatCompletionAsync(
            ChatCompletionRequest request, 
            string requestId, 
            CancellationToken token)
        {
            var tcs = new TaskCompletionSource<LLamaSharpInternalResponse>();
            
            var internalRequest = new LLamaSharpInternalRequest
            {
                RequestId = requestId,
                CompletionRequest = request,
                CancellationToken = token
            };

            _pendingRequests[requestId] = tcs;

            await _requestWriter.WriteAsync(internalRequest, token);
            LLamaSharpInternalResponse response;
            
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
                    
            await foreach (var completionResponse in response.StreamingResponse!.WithCancellation(token))
            {
                yield return completionResponse;
            }
        }

        /// <summary>
        /// Ensures messages include a default system prompt and returns the prepared message list.
        /// </summary>
        /// <param name="messages">Original chat messages</param>
        /// <returns>Messages with default system prompt if none provided</returns>
        private List<ChatMessage> PrepareMessages(List<ChatMessage> messages)
        {
            var allMessages = new List<ChatMessage>(messages);

            // Add default system prompt if none provided
            if (!allMessages.Any(m => m.Role == ChatRole.System))
            {
                var defaultSystemPrompt = "You are a helpful and knowledgeable assistant.";
                allMessages.Insert(0, new ChatMessage { Role = ChatRole.System, Content = defaultSystemPrompt });
            }

            return allMessages;
        }

        /// <summary>
        /// Extracts readable content from a HarmonyMessage
        /// </summary>
        /// <param name="message">The HarmonyMessage to extract content from</param>
        /// <returns>Extracted content string</returns>
        private string ExtractContentFromHarmonyMessage(HarmonyMessage message)
        {
            if (message?.Content == null)
                return string.Empty;

            // Get text content from the message
            var textContent = message.Content
                .OfType<HarmonyTextContent>()
                .FirstOrDefault();

            return textContent?.Text ?? string.Empty;
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