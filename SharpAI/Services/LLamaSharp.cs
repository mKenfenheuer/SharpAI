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
    public async IAsyncEnumerable<ChatCompletionResponse> GenerateChatCompletionAsync(ChatCompletionRequest request, CancellationToken token)
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
        // Prepare messages with default system prompt if needed
        var allMessages = PrepareMessages(request.Messages);

        // Use unified streaming engine but collect all results
        ChatCompletionResponse? finalResponse = null;
        var reasoningTokens = 0;

        await foreach (var result in streamingEngine.GenerateStreamAsync(allMessages, request, modelInstance, cancellationToken: token))
        {
            yield return result;
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

    public void Dispose()
    {
        _modelManager?.Dispose();
    }
}