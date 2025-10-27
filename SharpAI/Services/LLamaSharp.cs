using LLama;
using LLama.Common;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the LLamaSharpService.
    /// </summary>
    /// <param name="modelManager">Model manager for loading and managing models</param>
    /// <param name="logger">Logger for service operations</param>
    /// <param name="serviceProvider">Service provider for retrieving scoped services</param>
    public LLamaSharpService(
        LLamaModelManager modelManager, 
        ILogger<LLamaSharpService> logger,
        IServiceProvider serviceProvider)
    {
        _modelManager = modelManager;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Generates a chat completion for the provided request and returns the final response.
    /// </summary>
    /// <param name="request">Chat completion request</param>
    /// <returns>Chat completion response</returns>
    public async Task<ChatCompletionResponse> GenerateChatCompletionAsync(ChatCompletionRequest request)
    {
        using var scope = _serviceProvider.CreateScope();
        var streamingEngine = scope.ServiceProvider.GetRequiredService<StreamingInferenceEngine>();
        var harmonyFormatter = scope.ServiceProvider.GetRequiredService<HarmonyFormatter>();

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
        HarmonyResponse? finalHarmonyResponse = null;
        var reasoningTokens = 0;

        await foreach (var result in streamingEngine.GenerateStreamAsync(allMessages, request, modelInstance, inferenceParams))
        {
            if (result.IsComplete && result.HarmonyResponse != null)
            {
                finalHarmonyResponse = result.HarmonyResponse;
                break;
            }

            if (result.Delta != null)
            {
                // Extract content from harmony message delta
                var deltaContent = ExtractContentFromHarmonyMessage(result.Delta);
                if (!string.IsNullOrEmpty(deltaContent))
                {
                    contentBuilder.Append(deltaContent);
                }
            }
        }

        // Use the harmony response from streaming engine
        var harmonyResponse = finalHarmonyResponse ?? new HarmonyResponse
        {
            Messages = new List<HarmonyMessage>()
        };

        // Extract final content and tool calls from harmony response
        var finalContent = harmonyResponse.ExtractFinalContent();
        var toolCalls = harmonyResponse.ToolCall != null ? new List<HarmonyToolCall> { harmonyResponse.ToolCall } : new List<HarmonyToolCall>();

        // Use content builder if no final content extracted
        if (string.IsNullOrEmpty(finalContent))
        {
            finalContent = contentBuilder.ToString();
        }

        var assistantMessage = new ChatMessage { Role = ChatRole.Assistant, Content = finalContent };
        var finishReason = "stop";

        // Check if the response contains function calls
        if (toolCalls.Any())
        {
            assistantMessage.ToolCalls = LLamaUtilities.ConvertHarmonyToolCalls(toolCalls);
            assistantMessage.Content = null;
            finishReason = "tool_calls";

            // If parallel tool calls are disabled, only keep the first one
            if (request.ParallelToolCalls == false)
            {
                assistantMessage.ToolCalls = assistantMessage.ToolCalls.Take(1).ToList();
            }
        }

        // Calculate reasoning tokens if reasoning was used
        var reasoningContent = harmonyResponse.ExtractReasoningContent();
        if (request.ReasoningEffort.HasValue && !string.IsNullOrEmpty(reasoningContent))
        {
            reasoningTokens = LLamaUtilities.EstimateReasoningTokens(reasoningContent, request.ReasoningEffort.Value);
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
                PromptTokens = LLamaUtilities.EstimateTokens(harmonyFormatter.FormatPrompt(allMessages, request.Tools?.Any() == true, request.ReasoningEffort ?? ReasoningEffortLevel.Medium, request.Tools, request.ResponseFormat)),
                CompletionTokens = LLamaUtilities.EstimateTokens(finalContent ?? ""),
                ReasoningTokens = reasoningTokens > 0 ? reasoningTokens : null,
                TotalTokens = 0 // Will be calculated
            }
        };

        response.Usage.TotalTokens = response.Usage.PromptTokens + response.Usage.CompletionTokens + reasoningTokens;
        return response;
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

    public void Dispose()
    {
        _modelManager?.Dispose();
    }
}