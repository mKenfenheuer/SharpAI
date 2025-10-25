using LLama;
using LLama.Common;
using SharpAI.Models;
using SharpAI.Services.Harmony;
using System.Runtime.CompilerServices;
using System.Text;

namespace SharpAI.Services.Harmony;

/// <summary>
/// Handles streaming inference with browser tool integration using the Harmony format.
/// Supports recursive tool calling where browser tools are executed internally and their
/// results are fed back to the model for continued inference.
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
    /// If browser tools are called, they are executed internally and the results
    /// are fed back to the model for continued inference.
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

        // Initialize streaming state
        var streamState = new HarmonyStreamState();
        var responseBuilder = new StringBuilder();

        // Generate response using the stateless executor
        await foreach (var token in modelInstance.Executor!.InferAsync(formattedPrompt, inferenceParams))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Streaming inference cancelled by user");
                break;
            }

            responseBuilder.Append(token);
            var currentResponse = responseBuilder.ToString();

            // Parse incremental response to get new content
            var delta = _parser.ParseIncrementalResponse(streamState, token);

            if (delta != null)
            {
                yield return new StreamingInferenceResult
                {
                    Delta = delta,
                    IsComplete = false,
                    RawResponse = currentResponse
                };
            }

            // Check if we hit a completion stop token
            if (currentResponse.Contains("<|call|>") || currentResponse.Contains("<|return|>"))
            {
                break;
            }
        }

        // Parse final complete response
        var completeResponse = responseBuilder.ToString().Trim();
        var parsedResponse = _parser.ParseCompleteResponse(completeResponse);

        // Handle browser tool calls internally
        if (parsedResponse.ToolCalls.Any(tc => BrowserToolHandler.IsBrowserToolCall(tc.Recipient)))
        {
            var browserToolCount = parsedResponse.ToolCalls.Count(tc => BrowserToolHandler.IsBrowserToolCall(tc.Recipient));
            _logger.LogInformation("Executing {ToolCount} browser tool call(s)", browserToolCount);
            
            var toolResultMessages = await HandleBrowserToolCallsAsync(parsedResponse);
            
            // If we have tool results, continue inference with them
            if (toolResultMessages.Any())
            {
                // Get browser tool calls and ensure they have IDs
                var browserToolCalls = parsedResponse.ToolCalls
                    .Where(tc => BrowserToolHandler.IsBrowserToolCall(tc.Recipient))
                    .ToList();

                // Assign IDs to tool calls if they don't have them
                foreach (var toolCall in browserToolCalls)
                {
                    if (string.IsNullOrEmpty(toolCall.Id))
                    {
                        toolCall.Id = Guid.NewGuid().ToString();
                    }
                }

                // Create assistant message with tool calls - preserve original Harmony format
                // For browser tool calls, we need to preserve the complete chain-of-thought with proper channels
                var assistantContent = completeResponse; // Use the raw response to preserve channel formatting
                
                var assistantToolCallMessage = new ChatMessage
                {
                    Role = ChatRole.Assistant,
                    Content = assistantContent,
                    ToolCalls = browserToolCalls.Select(tc => new ToolCall
                    {
                        Id = tc.Id!,
                        Type = ToolType.Function,
                        Function = new FunctionCall
                        {
                            Name = tc.Name,
                            Arguments = tc.Arguments
                        }
                    }).ToList()
                };

                // Build new message list with original messages + assistant tool call + tool results
                // This preserves the entire conversation history and simply continues the chat
                var continuedMessages = new List<ChatMessage>(messages);
                continuedMessages.Add(assistantToolCallMessage);
                continuedMessages.AddRange(toolResultMessages);

                _logger.LogInformation("Continuing inference with tool results: {ResultCount} tool result(s)", toolResultMessages.Count);

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
            parsedResponse.Content = ProcessResponseFormat(parsedResponse.Content, request.ResponseFormat);
        }

        // Send final result
        yield return new StreamingInferenceResult
        {
            ParsedResponse = parsedResponse,
            IsComplete = true,
            RawResponse = completeResponse
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
    private async Task<List<ChatMessage>> HandleBrowserToolCallsAsync(ParsedResponse parsedResponse)
    {
        var browserToolCalls = parsedResponse.ToolCalls
            .Where(tc => BrowserToolHandler.IsBrowserToolCall(tc.Recipient))
            .ToList();

        var toolResultMessages = new List<ChatMessage>();

        foreach (var toolCall in browserToolCalls)
        {
            try
            {
                // Ensure tool call has an ID
                if (string.IsNullOrEmpty(toolCall.Id))
                {
                    toolCall.Id = Guid.NewGuid().ToString();
                }

                var functionName = BrowserToolHandler.ExtractBrowserFunction(toolCall.Recipient);
                var result = await _browserHandler.HandleBrowserToolCall(functionName, toolCall.Arguments);
                
                // Create a tool result message
                var toolResultMessage = new ChatMessage
                {
                    Role = ChatRole.Tool,
                    Content = result ?? "Tool execution completed successfully",
                    ToolCallId = toolCall.Id,
                    Name = toolCall.Recipient
                };

                toolResultMessages.Add(toolResultMessage);

                _logger.LogDebug("Browser tool executed successfully: {Function}", functionName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing browser tool call: {Recipient}", toolCall.Recipient);
                
                // Ensure tool call has an ID even for errors
                if (string.IsNullOrEmpty(toolCall.Id))
                {
                    toolCall.Id = Guid.NewGuid().ToString();
                }

                // Create error result message
                var errorMessage = new ChatMessage
                {
                    Role = ChatRole.Tool,
                    Content = $"Error executing browser tool: {ex.Message}",
                    ToolCallId = toolCall.Id,
                    Name = toolCall.Recipient
                };

                toolResultMessages.Add(errorMessage);
            }
        }

        // Remove browser tool calls from the response since they're handled internally
        parsedResponse.ToolCalls = parsedResponse.ToolCalls
            .Where(tc => !BrowserToolHandler.IsBrowserToolCall(tc.Recipient))
            .ToList();

        if (!toolResultMessages.Any())
        {
            toolResultMessages.Add(new ChatMessage
            {
                Role = ChatRole.Tool,
                Content = "No browser tool calls were able to be executed.",
            });
        }

        return toolResultMessages;
    }
}

/// <summary>
/// Represents a single result from the streaming inference process.
/// Contains either incremental delta updates or the final parsed response.
/// </summary>
public class StreamingInferenceResult
{
    public HarmonyStreamDelta? Delta { get; set; }
    public ParsedResponse? ParsedResponse { get; set; }
    public bool IsComplete { get; set; }
    public string RawResponse { get; set; } = string.Empty;
}