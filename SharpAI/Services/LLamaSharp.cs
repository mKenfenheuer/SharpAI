using LLama;
using LLama.Common;
using SharpAI.Models;
using SharpAI.Services.Harmony;
using System.Text;

namespace SharpAI.Services;

/// <summary>
/// Service that manages model selection and generates chat completions using LLamaSharp backends.
/// Provides both synchronous completion and streaming completion compatible with OpenAI-style responses.
/// </summary>
public class LLamaSharpService : IDisposable
{
    private readonly LLamaModelManager _modelManager;
    private readonly ILogger<LLamaSharpService> _logger;
    private readonly StreamingInferenceEngine _streamingEngine;
    private readonly HarmonyParser _harmonyParser;
    private readonly HarmonyFormatter _harmonyFormatter;

    /// <summary>
    /// Initializes a new instance of the LLamaSharpService.
    /// </summary>
    /// <param name="modelManager">Model manager for loading and managing models</param>
    /// <param name="logger">Logger for service operations</param>
    /// <param name="streamingEngine">Streaming inference engine</param>
    /// <param name="harmonyParser">Parser for harmony format responses</param>
    /// <param name="harmonyFormatter">Formatter for harmony format prompts</param>
    public LLamaSharpService(
        LLamaModelManager modelManager, 
        ILogger<LLamaSharpService> logger,
        StreamingInferenceEngine streamingEngine,
        HarmonyParser harmonyParser,
        HarmonyFormatter harmonyFormatter)
    {
        _modelManager = modelManager;
        _logger = logger;
        _streamingEngine = streamingEngine;
        _harmonyParser = harmonyParser;
        _harmonyFormatter = harmonyFormatter;
    }

    /// <summary>
    /// Generates a chat completion for the provided request and returns the final response.
    /// </summary>
    /// <param name="request">Chat completion request</param>
    /// <returns>Chat completion response</returns>
    public async Task<ChatCompletionResponse> GenerateChatCompletionAsync(ChatCompletionRequest request)
    {
        var requestedModel = request.Model ?? "gpt-4";
        _logger.LogInformation("Requested model: {RequestedModel}", requestedModel);

        // Validate tool definitions if provided
        if (request.Tools?.Any() == true)
        {
            var validationResults = FunctionSchemaValidator.ValidateToolDefinitions(request.Tools);
            if (validationResults.Any())
            {
                var errorMessage = "Invalid function definitions: " + 
                    string.Join("; ", validationResults.SelectMany(kvp => kvp.Value.Select(err => $"{kvp.Key}: {err}")));
                _logger.LogError("Tool validation failed: {ValidationErrors}", errorMessage);
                throw new ArgumentException(errorMessage);
            }
        }

        ModelInstance modelInstance;
        try
        {
            modelInstance = await _modelManager.GetModelAsync(requestedModel);
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Invalid model requested: {ModelName}", requestedModel);
            throw new ArgumentException($"Model '{requestedModel}' is not available. Use /v1/models to see available models.", ex);
        }

        var inferenceParams = LLamaUtilities.CreateInferenceParams(request, modelInstance);
        
        // Prepare messages with default system prompt if needed
        var allMessages = PrepareMessages(request.Messages);

        // Use unified streaming engine but collect all results
        var contentBuilder = new StringBuilder();
        ParsedResponse? finalParsedResponse = null;
        var reasoningTokens = 0;

        await foreach (var result in _streamingEngine.GenerateStreamAsync(allMessages, request, modelInstance, inferenceParams))
        {
            if (result.IsComplete && result.ParsedResponse != null)
            {
                finalParsedResponse = result.ParsedResponse;
                break;
            }

            if (result.Delta?.FinalContent != null)
            {
                contentBuilder.Append(result.Delta.FinalContent);
            }
        }

        // Use the parsed response from streaming engine
        var parsedResponse = finalParsedResponse ?? new ParsedResponse
        {
            Content = contentBuilder.ToString(),
            ToolCalls = new List<HarmonyToolCall>(),
            FunctionCall = false,
            Reasoning = null
        };

        var assistantMessage = new ChatMessage { Role = ChatRole.Assistant, Content = parsedResponse.Content };
        var finishReason = "stop";

        // Check if the response contains function calls
        if (parsedResponse.ToolCalls.Any() || parsedResponse.FunctionCall)
        {
            assistantMessage.ToolCalls = LLamaUtilities.ConvertHarmonyToolCalls(parsedResponse.ToolCalls);
            assistantMessage.Content = null;
            finishReason = "tool_calls";

            // If parallel tool calls are disabled, only keep the first one
            if (request.ParallelToolCalls == false)
            {
                assistantMessage.ToolCalls = assistantMessage.ToolCalls.Take(1).ToList();
            }
        }

        // Calculate reasoning tokens if reasoning was used
        if (request.ReasoningEffort.HasValue && !string.IsNullOrEmpty(parsedResponse.Reasoning))
        {
            reasoningTokens = LLamaUtilities.EstimateReasoningTokens(parsedResponse.Reasoning, request.ReasoningEffort.Value);
        }

        var response = new ChatCompletionResponse
        {
            Id = LLamaUtilities.CreateCompletionId(),
            Object = CompletionObjectType.ChatCompletion,
            Created = LLamaUtilities.GetCurrentUnixTimestamp(),
            Model = request.Model,
            Choices = new List<ChatCompletionChoice>
            {
                new ChatCompletionChoice
                {
                    Index = 0,
                    Message = assistantMessage,
                    FinishReason = finishReason
                }
            },
            Usage = new TokenUsage
            {
                PromptTokens = LLamaUtilities.EstimateTokens(_harmonyFormatter.FormatPrompt(allMessages, request.Tools?.Any() == true, request.ReasoningEffort ?? ReasoningEffortLevel.Medium, request.Tools, request.ResponseFormat)),
                CompletionTokens = LLamaUtilities.EstimateTokens(parsedResponse.Content ?? ""),
                ReasoningTokens = reasoningTokens > 0 ? reasoningTokens : null,
                TotalTokens = 0 // Will be calculated
            }
        };

        response.Usage.TotalTokens = response.Usage.PromptTokens + response.Usage.CompletionTokens + reasoningTokens;
        return response;
    }

    /// <summary>
    /// Generates a chat completion as an async stream of chunks. Each chunk follows the OpenAI streaming format.
    /// </summary>
    /// <param name="request">Chat completion request</param>
    /// <returns>Async enumerable of chat completion chunks</returns>
    public async IAsyncEnumerable<ChatCompletionChunk> GenerateChatCompletionStreamAsync(ChatCompletionRequest request)
    {
        var requestedModel = request.Model ?? "gpt-4";
        _logger.LogInformation("Requested model for streaming: {RequestedModel}", requestedModel);
        
        // Validate tool definitions if provided
        if (request.Tools?.Any() == true)
        {
            var validationResults = FunctionSchemaValidator.ValidateToolDefinitions(request.Tools);
            if (validationResults.Any())
            {
                var errorMessage = "Invalid function definitions: " + 
                    string.Join("; ", validationResults.SelectMany(kvp => kvp.Value.Select(err => $"{kvp.Key}: {err}")));
                _logger.LogError("Tool validation failed: {ValidationErrors}", errorMessage);
                throw new ArgumentException(errorMessage);
            }
        }
        
        ModelInstance modelInstance;
        try
        {
            modelInstance = await _modelManager.GetModelAsync(requestedModel);
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Invalid model requested for streaming: {ModelName}", requestedModel);
            throw new ArgumentException($"Model '{requestedModel}' is not available. Use /v1/models to see available models.", ex);
        }
        
        var inferenceParams = LLamaUtilities.CreateInferenceParams(request, modelInstance);
        var completionId = LLamaUtilities.CreateCompletionId();
        var created = LLamaUtilities.GetCurrentUnixTimestamp();

        // Prepare messages with default system prompt if needed
        var allMessages = PrepareMessages(request.Messages);

        // Send initial chunk
        yield return new ChatCompletionChunk
        {
            Id = completionId,
            Object = CompletionObjectType.ChatCompletionChunk,
            Created = created,
            Model = request.Model,
            Choices = new List<ChatCompletionChunkChoice>
            {
                new ChatCompletionChunkChoice
                {
                    Index = 0,
                    Delta = new ChatMessage { Role = ChatRole.Assistant, Content = "" },
                    FinishReason = null
                }
            }
        };

        // Use unified streaming engine
        ParsedResponse? finalParsedResponse = null;

        await foreach (var result in _streamingEngine.GenerateStreamAsync(allMessages, request, modelInstance, inferenceParams))
        {
            if (result.IsComplete && result.ParsedResponse != null)
            {
                finalParsedResponse = result.ParsedResponse;
                break;
            }

            // Stream content deltas
            if (result.Delta?.FinalContent != null)
            {
                yield return new ChatCompletionChunk
                {
                    Id = completionId,
                    Object = CompletionObjectType.ChatCompletionChunk,
                    Created = created,
                    Model = request.Model,
                    Choices = new List<ChatCompletionChunkChoice>
                    {
                        new ChatCompletionChunkChoice
                        {
                            Index = 0,
                            Delta = new ChatMessage { Role = ChatRole.Assistant, Content = result.Delta.FinalContent },
                            FinishReason = null
                        }
                    }
                };
            }
        }

        // Send tool calls if present
        if (finalParsedResponse?.ToolCalls.Any() == true)
        {
            var toolCalls = LLamaUtilities.ConvertHarmonyToolCalls(finalParsedResponse.ToolCalls);
            
            foreach (var toolCall in toolCalls)
            {
                yield return new ChatCompletionChunk
                {
                    Id = completionId,
                    Object = CompletionObjectType.ChatCompletionChunk,
                    Created = created,
                    Model = request.Model,
                    Choices = new List<ChatCompletionChunkChoice>
                    {
                        new ChatCompletionChunkChoice
                        {
                            Index = 0,
                            Delta = new ChatMessage 
                            { 
                                Role = ChatRole.Assistant, 
                                ToolCalls = new List<ToolCall> { toolCall }
                            },
                            FinishReason = null
                        }
                    }
                };
            }
        }

        // Send final chunk
        var finishReason = finalParsedResponse?.ToolCalls.Any() == true ? "tool_calls" : "stop";
        yield return new ChatCompletionChunk
        {
            Id = completionId,
            Object = CompletionObjectType.ChatCompletionChunk,
            Created = created,
            Model = request.Model,
            Choices = new List<ChatCompletionChunkChoice>
            {
                new ChatCompletionChunkChoice
                {
                    Index = 0,
                    Delta = new ChatMessage { Role = ChatRole.Assistant },
                    FinishReason = finishReason
                }
            }
        };
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

    public void Dispose()
    {
        _modelManager?.Dispose();
    }
}