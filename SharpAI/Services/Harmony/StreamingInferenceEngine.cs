using LLama;
using LLama.Common;
using SharpAI.Models;
using SharpAI.Services.Harmony;
using System.Runtime.CompilerServices;
using System.Text;

namespace SharpAI.Services.Harmony;

/// <summary>
/// Handles streaming inference with browser tool integration using the Harmony format.
/// This version uses only the Harmony library methods without any fallback parsing.
/// </summary>
public class StreamingInferenceEngine
{
    private readonly ILogger<StreamingInferenceEngine> _logger;
    private readonly HarmonyFormatter _formatter;
    private readonly HarmonyParser _parser;
    private readonly BrowserToolHandler _browserHandler;

    public StreamingInferenceEngine(
        ILogger<StreamingInferenceEngine> logger,
        HarmonyFormatter formatter,
        HarmonyParser parser,
        BrowserToolHandler browserHandler)
    {
        _logger = logger;
        _formatter = formatter;
        _parser = parser;
        _browserHandler = browserHandler;
    }

    /// <summary>
    /// Generates a streaming response with browser tool integration.
    /// Uses only Harmony library parsing methods.
    /// </summary>
    public async IAsyncEnumerable<StreamingInferenceResult> GenerateStreamAsync(
        List<ChatMessage> messages,
        ChatCompletionRequest request,
        ModelInstance modelInstance,
        InferenceParams inferenceParams,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting streaming inference for model: {Model} with {MessageCount} messages",
            request.Model, messages.Count);



        // Check if tools are available and should be used
        var shouldUseFunctions = request.Tools?.Any() == true &&
                                 (request.ToolChoice != ToolChoice.None);

        // Apply harmony format to the prompt
        var formattedPrompt = _formatter.FormatPrompt(
            messages,
            shouldUseFunctions,
            request.ReasoningEffort ?? ReasoningEffortLevel.Low,
            request.Tools,
            request.ResponseFormat);

        var rawResponseBuilder = new StringBuilder();
        // Generate response using the stateless executor

        // Pass the predefined next turn role to the parser
        var nextTurnToken = new[] { "<|start|>", "assistant" };

        foreach (var t in nextTurnToken)
        {
            _parser.ParseToken(t);
            rawResponseBuilder.Append(t);
        }

        // Create a fresh stateless executor to avoid state contamination from previous inferences
        var executor = new StatelessExecutor(modelInstance.Weights!, modelInstance.Context!.Params);
        
        await foreach (var token in executor.InferAsync(formattedPrompt, inferenceParams))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Streaming inference cancelled by user");
                break;
            }

            rawResponseBuilder.Append(token);
            var message = _parser.ParseToken(token);
            if (message != null)
            {
                if (!_parser.IsComplete)
                {
                    yield return new StreamingInferenceResult
                    {
                        Delta = message,
                        IsComplete = false,
                        RawResponse = string.Empty // Raw response tracking would need to be implemented
                    };
                }
                else
                {
                    // Completed message
                    break;
                }
            }

        }

        var completeMessages = _parser.GetResponse();
        var rawResponse = rawResponseBuilder.ToString();

        // Handle tool calls internally
        if (completeMessages.ToolCall != null)
        {
            ChatMessage? toolResultMessage = null;
            
            if (BrowserToolHandler.IsBrowserToolCall(completeMessages.ToolCall.Recipient))
            {
                _logger.LogInformation("Executing browser tool call");
                toolResultMessage = await HandleBrowserToolCallAsync(completeMessages);
            }

            if (toolResultMessage != null)
            {
                var assistantToolCallMessage = new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Content = null,
                    ToolCalls = new List<ToolCall>
                    {
                        new ToolCall
                        {
                            Type = ToolType.Function,
                            Function = new FunctionCall
                            {
                                Name = completeMessages.ToolCall.Recipient,
                                Namespace = completeMessages.ToolCall.Namespace,
                                Arguments = completeMessages.ToolCall.Arguments ?? "{}"
                            }
                        }
                    }
                };

                // Build new message list with original messages + assistant tool call + tool results
                var continuedMessages = new List<ChatMessage>(messages);
                continuedMessages.Add(assistantToolCallMessage);
                continuedMessages.Add(toolResultMessage);

                _logger.LogInformation("Continuing inference with tool results");

                // We need to reset the parser state for continued inference
                _parser.ResetParserState();

                // Continue inference with tool results using recursive call
                await foreach (var continuedResult in GenerateStreamAsync(
                    continuedMessages, request, modelInstance, inferenceParams, cancellationToken))
                {
                    yield return continuedResult;
                }

                // Return early as the continued inference handles the final result
                yield break;
            }
        }

        // Apply response format validation if needed
        if (request.ResponseFormat != null)
        {
            // Note: Response format processing would need to be implemented for HarmonyResponse
            // For now, we'll skip this as it requires access to content that may be in channels
        }

        // Send final result
        yield return new StreamingInferenceResult
        {
            HarmonyResponse = completeMessages,
            IsComplete = true,
            RawResponse = string.Empty // Raw response tracking would need to be implemented
        };
    }

    /// <summary>
    /// Processes response format validation for JSON responses.
    /// </summary>
    private string ProcessResponseFormat(string content, ResponseFormat responseFormat)
    {
        if (responseFormat.Type == ResponseFormatType.JsonObject)
        {
            try
            {
                // Validate that the response is valid JSON
                var jsonDoc = System.Text.Json.JsonDocument.Parse(content);
                return content;
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogWarning(ex, "Response format validation failed for JSON object");
                // Return a valid JSON object with the content
                return System.Text.Json.JsonSerializer.Serialize(new { content = content });
            }
        }
        else if (responseFormat.Type == ResponseFormatType.JsonSchema && responseFormat.JsonSchema?.Schema != null)
        {
            // For JSON schema validation, we would need a JSON schema validator library
            // For now, just validate it's valid JSON
            try
            {
                var jsonDoc = System.Text.Json.JsonDocument.Parse(content);
                return content;
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogWarning(ex, "Response format validation failed for JSON schema");
                return System.Text.Json.JsonSerializer.Serialize(new { content = content });
            }
        }

        return content;
    }

    /// <summary>
    /// Executes browser tool calls and returns the results as ChatMessage objects.
    /// Browser tool calls are handled internally and their results are prepared for continued inference.
    /// </summary>
    private async Task<ChatMessage> HandleBrowserToolCallAsync(HarmonyResponse harmonyResponse)
    {
        try
        {
            // Generate an ID for this tool call
            var toolCallId = Guid.NewGuid().ToString();

            var functionName = BrowserToolHandler.ExtractBrowserFunction(harmonyResponse.ToolCall.Recipient);
            var result = await _browserHandler.HandleBrowserToolCall(functionName, harmonyResponse.ToolCall.Arguments);

            // Create a tool result message
            var toolResultMessage = new ChatMessage
            {
                Role = ChatRole.Tool,
                Content = result ?? "Tool execution completed successfully",
                ToolCallId = toolCallId,
                Name = harmonyResponse.ToolCall.Recipient
            };

            _logger.LogDebug("Browser tool executed successfully: {Function}", functionName);

            return toolResultMessage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing browser tool call: {Recipient}", harmonyResponse.ToolCall.Recipient);

            // Generate an ID for error case too
            var toolCallId = Guid.NewGuid().ToString();

            // Create error result message
            var errorMessage = new ChatMessage
            {
                Role = ChatRole.Tool,
                Content = $"Error executing browser tool: {ex.Message}",
                ToolCallId = toolCallId,
                Name = harmonyResponse.ToolCall.Recipient
            };

            return errorMessage;
        }
    }

    /// <summary>
    /// Extracts user-readable content from harmony response messages
    /// </summary>
    private string ExtractUserReadableContent(HarmonyResponse harmonyResponse)
    {
        var content = new StringBuilder();

        // Extract content from final channel if available
        var channels = harmonyResponse.Messages.Where(m => m.Role == HarmonyRole.Assistant)
            .SelectMany(m => m.Content.OfType<HarmonyAssistantContent>());

        var finalChannel = channels.FirstOrDefault(c => c.Channel == HarmonyChannel.Final);

        if (finalChannel != null)
        {
            content.Append(finalChannel.Content);
        }
        else if (channels.Any())
        {
            // If no final channel, use the last channel's content
            content.Append(channels.Last().Content);
        }

        return content.ToString().Trim();
    }
}

/// <summary>
/// Represents a single result from the streaming inference process.
/// Contains either incremental delta updates or the final parsed response.
/// </summary>
public class StreamingInferenceResult
{
    public HarmonyMessage? Delta { get; set; }
    public HarmonyResponse? HarmonyResponse { get; set; }
    public bool IsComplete { get; set; }
    public string RawResponse { get; set; } = string.Empty;
}